using Giim.Connectors.People;
using Giim.Domain.Devices;
using Giim.Domain.People;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.People;

/// <summary>
/// Copies staff and departments from the directory into GIIM. The directory is the source of truth for who
/// people are; GIIM never deletes anyone, because their asset history must stay intact after they leave.
/// </summary>
public sealed class PeopleSyncService(GiimDbContext db, IPeopleSource source, TimeProvider clock)
{
    public const string Source = "People";

    public async Task<SyncRun> SyncAsync(CancellationToken cancellationToken)
    {
        var startedAt = clock.GetUtcNow();
        var run = new SyncRun { Source = Source, StartedAt = startedAt };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var departments = await db.Departments.ToDictionaryAsync(d => d.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
            var people = await db.People.ToDictionaryAsync(p => p.EmployeeId, StringComparer.OrdinalIgnoreCase, cancellationToken);
            var managerUpns = new Dictionary<Person, string?>();

            await foreach (var record in source.GetPeopleAsync(cancellationToken))
            {
                run.DevicesSeen++;  // SyncRun counts "records seen"; for this source they are people

                if (!departments.TryGetValue(record.DepartmentCode, out var department))
                {
                    department = new Department { Code = record.DepartmentCode, Name = record.DepartmentName ?? record.DepartmentCode };
                    db.Departments.Add(department);
                    departments[department.Code] = department;
                }
                else if (record.DepartmentName is not null && department.Name != record.DepartmentName)
                {
                    department.Name = record.DepartmentName;
                }

                if (!people.TryGetValue(record.EmployeeId, out var person))
                {
                    person = new Person { EmployeeId = record.EmployeeId, DisplayName = record.DisplayName };
                    db.People.Add(person);
                    people[person.EmployeeId] = person;
                    run.Added++;
                }
                else
                {
                    run.Updated++;
                }

                person.DisplayName = record.DisplayName;
                person.UserPrincipalName = record.UserPrincipalName;
                person.Email ??= record.UserPrincipalName;
                person.JobTitle = record.JobTitle;
                person.Location = record.Location;
                person.DepartmentId = department.Id;
                person.Status = Enum.TryParse<PersonStatus>(record.Status, ignoreCase: true, out var status) ? status : PersonStatus.Active;
                person.Track = Enum.TryParse<ProvisioningTrack>(record.Track, ignoreCase: true, out var track) ? track : ProvisioningTrack.Full;
                person.StartDate = record.StartDate;
                person.EndDate = record.EndDate;
                person.LastSyncedAt = startedAt;
                person.UpdatedAt = startedAt;
                managerUpns[person] = record.ManagerUserPrincipalName;
            }

            // Managers are resolved once everyone is loaded, since a manager may appear after their team.
            var byUpn = people.Values.Where(p => p.UserPrincipalName is not null)
                .GroupBy(p => p.UserPrincipalName!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var (person, managerUpn) in managerUpns)
                person.ManagerId = managerUpn is not null && byUpn.TryGetValue(managerUpn, out var manager) && manager != person
                    ? manager.Id : null;

            // Not deleted and not marked as leavers: the directory decides that. Just reported.
            run.Removed = people.Values.Count(p => p.LastSyncedAt is { } seen && seen < startedAt);

            run.Status = SyncRunStatus.Succeeded;
            run.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return run;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            db.SyncRuns.Attach(run);
            run.Status = SyncRunStatus.Failed;
            run.CompletedAt = clock.GetUtcNow();
            run.Error = e.Message.Length > 2000 ? e.Message[..2000] : e.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}
