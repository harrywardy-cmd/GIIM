using System.Linq.Expressions;
using Giim.Domain.Assets;
using Giim.Domain.Cases;
using Giim.Domain.Common;
using Giim.Domain.People;
using Giim.Domain.Requests;
using Giim.Infrastructure.Persistence;
using Giim.Infrastructure.Provisioning;
using Giim.Infrastructure.Requests;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Cases;

/// <summary>Raised when the checklist changed after the user loaded it.</summary>
public sealed class CaseChangedException(string message) : Exception(message);

/// <summary>Someone not yet in the staff directory (their AD account doesn't exist yet).</summary>
public sealed record NewStarterPerson(string EmployeeId, string DisplayName, Guid DepartmentId, string? JobTitle, Guid? ManagerId, string? Email);

public sealed record StarterDetails(
    Guid? PersonId,
    NewStarterPerson? NewPerson,
    DateOnly StartDate,
    ProvisioningTrack? Track,
    Guid? ProfileId,
    string? TicketNumber,
    string? Notes);

public enum CaseView { Starters, Leavers, Completed, Cancelled }

public sealed record CaseListItem(
    Guid Id, CaseType Type, CaseStatus Status, Guid PersonId, string Person, string? Department, DateOnly? DueDate,
    string? TicketNumber, int Tasks, int Finished, DateTimeOffset CreatedAt);

/// <summary>
/// Starter and leaver checklists. Starters come from their department's profile; leavers from what they actually
/// hold. Every change is saved with the checklist's concurrency check, so two people can't overwrite each other.
/// </summary>
public sealed class CaseService(GiimDbContext db, ProfileService profiles, RequestService requests, TimeProvider clock)
{
    public async Task<ServiceCase> StartOnboardingAsync(StarterDetails details, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        var person = details.PersonId is { } personId
            ? await db.People.FirstOrDefaultAsync(p => p.Id == personId, cancellationToken) ?? throw new DomainException("Choose the new starter.")
            : await AddStarterAsync(details.NewPerson ?? throw new DomainException("Choose the new starter, or enter their details."),
                details.StartDate, details.Track ?? ProvisioningTrack.Full, cancellationToken);
        if (person.Status == PersonStatus.Left)
            throw new DomainException($"{person.DisplayName} has left the organisation.");
        await EnsureNoOpenCaseAsync(person, CaseType.Onboarding, cancellationToken);

        var profile = details.ProfileId is { } chosen
            ? await db.RoleProfiles.AsNoTracking().Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == chosen, cancellationToken)
                ?? throw new DomainException("That starter profile doesn't exist.")
            : await profiles.ChooseAsync(person.DepartmentId, person.JobTitle, details.Track ?? person.Track, cancellationToken)
                ?? throw new DomainException("This department has no starter profile yet. Set one up under Setup → Starter profiles, or choose one.");
        profile.Items.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));

        var serviceCase = ChecklistGenerator.ForOnboarding(person, profile, details.TicketNumber, actor);
        serviceCase.DueDate = details.StartDate;
        serviceCase.Notes = Clean(details.Notes);
        db.Cases.Add(serviceCase);
        await db.SaveChangesAsync(cancellationToken);
        return serviceCase;
    }

    public async Task<ServiceCase> StartOffboardingAsync(Guid personId, DateOnly lastDay, string? ticketNumber, string? notes, string actor,
        CancellationToken cancellationToken)
    {
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId, cancellationToken)
            ?? throw new DomainException("Choose the person who is leaving.");
        await EnsureNoOpenCaseAsync(person, CaseType.Offboarding, cancellationToken);

        // Built from what they actually hold today, not from a template.
        var assets = await db.Assets.AsNoTracking().Include(a => a.Category)
            .Where(a => a.AssignedToPersonId == personId).OrderBy(a => a.Category!.Name).ThenBy(a => a.Model).ToListAsync(cancellationToken);
        var apps = await db.Applications.AsNoTracking()
            .Where(app => db.Assignments.Any(x => x.PersonId == personId && x.ApplicationId == app.Id && x.EndedAt == null))
            .OrderBy(app => app.Name).ToListAsync(cancellationToken);

        var serviceCase = ChecklistGenerator.ForOffboarding(person, assets, apps, ticketNumber, lastDay, actor);
        serviceCase.Notes = Clean(notes);
        db.Cases.Add(serviceCase);
        await db.SaveChangesAsync(cancellationToken);
        return serviceCase;
    }

    /// <summary>Applies one change to a checklist (complete, skip, reopen, approve, add, cancel).</summary>
    public async Task ActAsync(Guid caseId, Action<ServiceCase, DateTimeOffset> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var serviceCase = await LoadAsync(caseId, cancellationToken);
        var before = serviceCase.Tasks.Select(t => t.Id).ToHashSet();
        action(serviceCase, clock.GetUtcNow());
        // A task added by the action is new; EF would otherwise treat it as an existing row to update.
        foreach (var task in serviceCase.Tasks.Where(t => !before.Contains(t.Id))) db.ChecklistTasks.Add(task);
        await SaveAsync(cancellationToken);
    }

    /// <summary>
    /// Raises a device request for a starter's hardware task (needed by their start date, approved by their manager)
    /// and links it, in one save. The task ticks itself off when the device is handed over.
    /// </summary>
    public async Task<DeviceRequest> RaiseDeviceRequestAsync(Guid caseId, Guid taskId, Guid? categoryId, string? deviceDescription,
        RequestActor actor, string baseUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var serviceCase = await LoadAsync(caseId, cancellationToken);
        if (serviceCase.Type != CaseType.Onboarding) throw new DomainException("Device requests are raised from starter checklists.");
        var task = serviceCase.Tasks.FirstOrDefault(t => t.Id == taskId) ?? throw new KeyNotFoundException("Task not found.");
        var category = categoryId ?? task.CategoryId ?? throw new DomainException("Choose the type of device.");
        var description = Clean(deviceDescription) ?? task.Title.Replace("Allocate and scan: ", "", StringComparison.Ordinal);

        var details = new NewDeviceRequest(serviceCase.PersonId, category, description, RequestReason.NewStarter,
            $"New starter{(serviceCase.DueDate is { } start ? $", starts {start:d MMMM yyyy}" : "")}",
            RequestPriority.High, NeededBy: serviceCase.DueDate, TicketNumber: serviceCase.ServiceDeskRequestId);
        try
        {
            return await requests.SubmitAsync(details, actor, baseUrl,
                request => serviceCase.LinkDeviceRequest(taskId, request.Id, request.Reference, actor.Login, clock.GetUtcNow()),
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CaseChangedException("Someone else updated this checklist at the same moment. Refresh and try again.");
        }
    }

    // ---- Queries -------------------------------------------------------------------------------------------------------

    public async Task<(IReadOnlyList<CaseListItem> Items, IReadOnlyDictionary<CaseView, int> Counts)> ListAsync(CaseView view, string? search,
        Guid? personId, CancellationToken cancellationToken)
    {
        var all = db.Cases.AsNoTracking().Where(c => c.Type == CaseType.Onboarding || c.Type == CaseType.Offboarding);
        if (personId is { } p) all = all.Where(c => c.PersonId == p);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            all = all.Where(c => (c.ServiceDeskRequestId != null && c.ServiceDeskRequestId.Contains(term))
                || db.People.Any(x => x.Id == c.PersonId && (x.DisplayName.Contains(term) || x.EmployeeId.Contains(term))));
        }

        var open = all.Where(c => c.Status != CaseStatus.Completed && c.Status != CaseStatus.Cancelled);
        var counts = new Dictionary<CaseView, int>
        {
            [CaseView.Starters] = await open.CountAsync(c => c.Type == CaseType.Onboarding, cancellationToken),
            [CaseView.Leavers] = await open.CountAsync(c => c.Type == CaseType.Offboarding, cancellationToken),
            [CaseView.Completed] = await all.CountAsync(c => c.Status == CaseStatus.Completed, cancellationToken),
            [CaseView.Cancelled] = await all.CountAsync(c => c.Status == CaseStatus.Cancelled, cancellationToken),
        };

        var filtered = view switch
        {
            CaseView.Starters => open.Where(c => c.Type == CaseType.Onboarding).OrderBy(c => c.DueDate),
            CaseView.Leavers => open.Where(c => c.Type == CaseType.Offboarding).OrderBy(c => c.DueDate),
            CaseView.Completed => all.Where(c => c.Status == CaseStatus.Completed).OrderByDescending(c => c.CompletedAt),
            _ => all.Where(c => c.Status == CaseStatus.Cancelled).OrderByDescending(c => c.UpdatedAt),
        };
        var items = await filtered.Take(200)
            .Select(c => new CaseListItem(c.Id, c.Type, c.Status, c.PersonId,
                db.People.Where(x => x.Id == c.PersonId).Select(x => x.DisplayName).First(),
                db.People.Where(x => x.Id == c.PersonId).Select(x => x.Department!.Name).FirstOrDefault(),
                c.DueDate, c.ServiceDeskRequestId, c.Tasks.Count,
                c.Tasks.Count(t => t.Status == TaskState.Done || t.Status == TaskState.Skipped), c.CreatedAt))
            .ToListAsync(cancellationToken);
        return (items, counts);
    }

    public Task<ServiceCase?> GetAsync(Guid caseId, CancellationToken cancellationToken) =>
        db.Cases.AsNoTracking().Include(c => c.Tasks).FirstOrDefaultAsync(c => c.Id == caseId, cancellationToken);

    // ---- Tasks that tick themselves off -------------------------------------------------------------------------------

    /// <summary>
    /// Completes open checklist tasks that something else has just finished (an asset returned, a requested device
    /// handed over). Called inside that other change's save, so both happen or neither does.
    /// </summary>
    internal static async Task CompleteLinkedTasksAsync(GiimDbContext db, Expression<Func<ChecklistTask, bool>> linked, string actor, string note,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var caseIds = await db.ChecklistTasks.Where(linked)
            .Where(t => t.Status != TaskState.Done && t.Status != TaskState.Skipped && !t.RequiresApproval)
            .Select(t => t.CaseId).Distinct().ToListAsync(cancellationToken);
        if (caseIds.Count == 0) return;

        var match = linked.Compile();
        var cases = await db.Cases.Include(c => c.Tasks)
            .Where(c => caseIds.Contains(c.Id) && c.Status != CaseStatus.Cancelled).ToListAsync(cancellationToken);
        foreach (var serviceCase in cases)
            foreach (var task in serviceCase.Tasks.Where(t => match(t) && t.Status is not (TaskState.Done or TaskState.Skipped) && !t.RequiresApproval).ToList())
                serviceCase.CompleteTask(task.Id, actor, note, now);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    private async Task<Person> AddStarterAsync(NewStarterPerson details, DateOnly startDate, ProvisioningTrack track, CancellationToken cancellationToken)
    {
        var employeeId = Clean(details.EmployeeId) ?? throw new DomainException("Enter the employee ID from HR.");
        var name = Clean(details.DisplayName) ?? throw new DomainException("Enter the starter's name.");
        if (await db.People.AnyAsync(p => p.EmployeeId == employeeId, cancellationToken))
            throw new DomainException($"Employee ID {employeeId} is already in the staff directory; choose that person instead.");
        if (!await db.Departments.AnyAsync(d => d.Id == details.DepartmentId, cancellationToken))
            throw new DomainException("Choose the starter's department.");
        if (details.ManagerId is { } managerId && !await db.People.AnyAsync(p => p.Id == managerId, cancellationToken))
            throw new DomainException("That manager isn't in the staff directory.");

        // Added by hand (not synced) and pending; the directory sync matches them by employee ID once their account exists.
        var person = new Person
        {
            EmployeeId = employeeId,
            DisplayName = name,
            DepartmentId = details.DepartmentId,
            JobTitle = Clean(details.JobTitle),
            ManagerId = details.ManagerId,
            Email = Clean(details.Email),
            Status = PersonStatus.Pending,
            StartDate = startDate,
            Track = track,
        };
        db.People.Add(person);
        return person;
    }

    private async Task EnsureNoOpenCaseAsync(Person person, CaseType type, CancellationToken cancellationToken)
    {
        if (await db.Cases.AnyAsync(c => c.PersonId == person.Id && c.Type == type
                && c.Status != CaseStatus.Completed && c.Status != CaseStatus.Cancelled, cancellationToken))
            throw new DomainException($"{person.DisplayName} already has an open {(type == CaseType.Onboarding ? "starter" : "leaver")} checklist.");
    }

    private async Task<ServiceCase> LoadAsync(Guid caseId, CancellationToken cancellationToken) =>
        await db.Cases.Include(c => c.Tasks).FirstOrDefaultAsync(c => c.Id == caseId, cancellationToken)
        ?? throw new KeyNotFoundException("Checklist not found.");

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CaseChangedException("Someone else updated this checklist at the same moment. Refresh and try again.");
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
