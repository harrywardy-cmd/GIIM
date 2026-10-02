using System.Text.Json;
using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Domain.People;
using Giim.Domain.Provisioning;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Provisioning;

public sealed record ProfileDetails(string Name, string? JobTitle, ProvisioningTrack Track);

public sealed record ProfileItemDetails(ProfileItemType Type, string Description, string? GroupName, Guid? CategoryId, Guid? StockItemId,
    bool CloudGroup = false);

public sealed record ProfileImportResult(int DepartmentsAdded, int ProfilesAdded, int ProfilesReplaced, int Items, IReadOnlyList<string> Warnings);

/// <summary>
/// Starter profiles: what a new starter in a department receives. Several per department are allowed (e.g. office
/// staff and floor staff); a job-title profile is picked first for someone with that title.
/// </summary>
public sealed class ProfileService(GiimDbContext db)
{
    public async Task<RoleProfile> CreateAsync(Guid departmentId, ProfileDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!await db.Departments.AnyAsync(d => d.Id == departmentId, cancellationToken))
            throw new DomainException("Choose a department.");
        var profile = new RoleProfile { Name = "", DepartmentId = departmentId };
        await ApplyAsync(profile, details, cancellationToken);
        db.RoleProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public async Task UpdateAsync(Guid profileId, ProfileDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        var profile = await LoadAsync(profileId, cancellationToken);
        await ApplyAsync(profile, details, cancellationToken);
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Checklists already created keep their tasks; they just stop pointing at the profile.</summary>
    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await db.RoleProfiles.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken)
            ?? throw new KeyNotFoundException("Profile not found.");
        db.ProfileItems.RemoveRange(profile.Items);
        db.RoleProfiles.Remove(profile);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProfileItem> AddItemAsync(Guid profileId, ProfileItemDetails details, CancellationToken cancellationToken)
    {
        await LoadAsync(profileId, cancellationToken);
        var item = new ProfileItem { RoleProfileId = profileId, Description = "" };
        await ApplyAsync(item, details, cancellationToken);
        db.ProfileItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task UpdateItemAsync(Guid profileId, Guid itemId, ProfileItemDetails details, CancellationToken cancellationToken)
    {
        var item = await db.ProfileItems.FirstOrDefaultAsync(i => i.Id == itemId && i.RoleProfileId == profileId, cancellationToken)
            ?? throw new KeyNotFoundException("Item not found.");
        await ApplyAsync(item, details, cancellationToken);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteItemAsync(Guid profileId, Guid itemId, CancellationToken cancellationToken)
    {
        var item = await db.ProfileItems.FirstOrDefaultAsync(i => i.Id == itemId && i.RoleProfileId == profileId, cancellationToken)
            ?? throw new KeyNotFoundException("Item not found.");
        db.ProfileItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Picks the profile for a starter: one for their job title if there is one, otherwise one for their track
    /// (full or light), preferring the department's general profile.
    /// </summary>
    public async Task<RoleProfile?> ChooseAsync(Guid departmentId, string? jobTitle, ProvisioningTrack track, CancellationToken cancellationToken)
    {
        var profiles = await db.RoleProfiles.AsNoTracking().Include(p => p.Items)
            .Where(p => p.DepartmentId == departmentId).ToListAsync(cancellationToken);
        return profiles.FirstOrDefault(p => p.JobTitle is not null && string.Equals(p.JobTitle, jobTitle?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? profiles.Where(p => p.JobTitle is null && p.Track == track).OrderBy(p => p.CreatedAt).FirstOrDefault()
            ?? profiles.Where(p => p.JobTitle is null).OrderBy(p => p.CreatedAt).FirstOrDefault();
    }

    /// <summary>
    /// Bulk set-up from a file in the samples/departments-and-profiles.json format. A profile with the same department
    /// and name is replaced; departments not yet known are added. Anything that can't be matched is reported, not guessed.
    /// </summary>
    public async Task<ProfileImportResult> ImportAsync(Stream json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new DomainException("The file should be a list of departments, each with its profiles.");

        var departments = await db.Departments.ToDictionaryAsync(d => d.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var categories = await db.AssetCategories.Where(c => c.IsActive).ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var stock = await db.StockItems.Where(s => s.IsActive).ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var warnings = new List<string>();
        int departmentsAdded = 0, added = 0, replaced = 0, items = 0;

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var dept = entry.GetProperty("department");
            var code = dept.GetProperty("code").GetString()?.Trim();
            if (string.IsNullOrEmpty(code)) { warnings.Add("A department without a code was skipped."); continue; }
            if (!departments.TryGetValue(code, out var department))
            {
                department = new Department { Code = code, Name = Text(dept, "name") ?? code };
                db.Departments.Add(department);
                departments[code] = department;
                departmentsAdded++;
            }

            foreach (var p in entry.GetProperty("profiles").EnumerateArray())
            {
                var name = Text(p, "name") ?? $"{department.Name} - Default";
                var jobTitle = Text(p, "jobTitle");
                var track = Enum.TryParse<ProvisioningTrack>(Text(p, "track"), ignoreCase: true, out var t) ? t : ProvisioningTrack.Full;

                var existing = await db.RoleProfiles.Include(x => x.Items)
                    .FirstOrDefaultAsync(x => x.DepartmentId == department.Id && x.Name == name, cancellationToken);
                var profile = existing ?? new RoleProfile { Name = name, DepartmentId = department.Id };
                if (existing is null) { db.RoleProfiles.Add(profile); added++; }
                else { db.ProfileItems.RemoveRange(existing.Items); existing.Items.Clear(); replaced++; }
                profile.JobTitle = jobTitle;
                profile.Track = track;

                foreach (var i in p.GetProperty("items").EnumerateArray())
                {
                    if (!Enum.TryParse<ProfileItemType>(Text(i, "type"), ignoreCase: true, out var type))
                    {
                        warnings.Add($"{name}: unknown item type “{Text(i, "type")}” skipped.");
                        continue;
                    }
                    var item = new ProfileItem
                    {
                        RoleProfileId = profile.Id, Type = type, Description = Text(i, "description") ?? type.ToString(),
                        GroupName = Text(i, "groupName"),
                        CloudGroup = i.TryGetProperty("cloudGroup", out var cloud) && cloud.ValueKind == JsonValueKind.True,
                    };
                    if (type == ProfileItemType.Hardware)
                    {
                        var categoryName = Text(i, "hardwareCategory");
                        if (categoryName is null || !categories.TryGetValue(categoryName, out var category))
                        {
                            warnings.Add($"{name}: “{item.Description}” skipped; there is no device category “{categoryName}”.");
                            continue;
                        }
                        item.CategoryId = category.Id;
                    }
                    if (type == ProfileItemType.StockItem)
                    {
                        var stockName = Text(i, "stockItem") ?? item.Description;
                        if (!stock.TryGetValue(stockName, out var stockItem))
                        {
                            warnings.Add($"{name}: “{item.Description}” skipped; there is no stock item “{stockName}”.");
                            continue;
                        }
                        item.StockItemId = stockItem.Id;
                    }
                    profile.Items.Add(item);
                    db.ProfileItems.Add(item);
                    items++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ProfileImportResult(departmentsAdded, added, replaced, items, warnings);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    private async Task<RoleProfile> LoadAsync(Guid profileId, CancellationToken cancellationToken) =>
        await db.RoleProfiles.FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken)
        ?? throw new KeyNotFoundException("Profile not found.");

    private async Task ApplyAsync(RoleProfile profile, ProfileDetails details, CancellationToken cancellationToken)
    {
        var name = Clean(details.Name) ?? throw new DomainException("Give the profile a name.");
        var jobTitle = Clean(details.JobTitle);
        if (await db.RoleProfiles.AnyAsync(p => p.Id != profile.Id && p.DepartmentId == profile.DepartmentId && p.Name == name, cancellationToken))
            throw new DomainException($"This department already has a profile called “{name}”.");
        if (jobTitle is not null
            && await db.RoleProfiles.AnyAsync(p => p.Id != profile.Id && p.DepartmentId == profile.DepartmentId && p.JobTitle == jobTitle, cancellationToken))
            throw new DomainException($"This department already has a profile for “{jobTitle}”.");
        profile.Name = name;
        profile.JobTitle = jobTitle;
        profile.Track = details.Track;
    }

    private async Task ApplyAsync(ProfileItem item, ProfileItemDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        var description = Clean(details.Description) ?? throw new DomainException("Describe the item.");
        var group = Clean(details.GroupName);
        Guid? categoryId = null, stockItemId = null;
        switch (details.Type)
        {
            case ProfileItemType.Hardware:
                categoryId = details.CategoryId is { } c && await db.AssetCategories.AnyAsync(x => x.Id == c && x.IsActive, cancellationToken)
                    ? c : throw new DomainException("Choose the device category (so a device request can be raised for it).");
                break;
            case ProfileItemType.StockItem:
                stockItemId = details.StockItemId is { } s && await db.StockItems.AnyAsync(x => x.Id == s && x.IsActive, cancellationToken)
                    ? s : throw new DomainException("Choose the stock item.");
                break;
            case ProfileItemType.SecurityGroup or ProfileItemType.LicenceGroup when group is null:
                throw new DomainException("Enter the group name.");
        }
        item.Type = details.Type;
        item.Description = description;
        item.GroupName = details.Type is ProfileItemType.Hardware or ProfileItemType.StockItem or ProfileItemType.ManualTask ? null : group;
        item.CloudGroup = item.GroupName is not null && details.CloudGroup;
        item.CategoryId = categoryId;
        item.StockItemId = stockItemId;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim() : null;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
