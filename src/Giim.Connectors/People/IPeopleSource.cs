namespace Giim.Connectors.People;

/// <summary>
/// A directory of staff: Entra ID through Microsoft Graph in Azure, or a CSV export on a developer PC. Read-only.
/// </summary>
public interface IPeopleSource
{
    IAsyncEnumerable<DirectoryPerson> GetPeopleAsync(CancellationToken cancellationToken = default);
}

public sealed record DirectoryPerson(
    string EmployeeId,
    string DisplayName,
    string? UserPrincipalName,
    string DepartmentCode,
    string? DepartmentName,
    string? JobTitle,
    string? Location,
    string Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Track,
    string? ManagerUserPrincipalName,
    string? Email = null,
    Guid? EntraObjectId = null);

public enum PeopleSource
{
    /// <summary>CSV export (samples/people.csv format). For development and tests.</summary>
    File,

    /// <summary>Entra ID user accounts through Microsoft Graph (User.Read.All, read-only). Used in Azure.</summary>
    Entra,
}

public sealed class PeopleOptions
{
    public const string SectionName = "People";

    public PeopleSource Source { get; set; } = PeopleSource.File;
    public string FilePath { get; set; } = "samples/people.csv";

    /// <summary>Optional JSON with department codes and names (samples/departments-and-profiles.json format).</summary>
    public string? DepartmentsFilePath { get; set; } = "samples/departments-and-profiles.json";

    /// <summary>How often the workers sync the directory (Entra source only). Zero disables the schedule.</summary>
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromHours(4);

    public EntraDirectoryOptions Entra { get; set; } = new();
}

/// <summary>How Entra ID accounts become GIIM staff records. See docs/staff-directory.md.</summary>
public sealed class EntraDirectoryOptions
{
    public Uri GraphBaseUrl { get; set; } = new("https://graph.microsoft.com/v1.0/");
    public int MaxRetries { get; set; } = 5;

    /// <summary>Extra OData filter on users, e.g. <c>companyName eq 'Contoso'</c>. Members only either way.</summary>
    public string? Filter { get; set; }

    /// <summary>
    /// Department name in Entra → GIIM department code (at most 20 characters). Departments not listed get a code made
    /// from their name. GIIM also matches an existing department by name, so codes from a profile import are kept.
    /// </summary>
    public Dictionary<string, string> DepartmentCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>On-premises extension attribute holding the track (Full or Light), e.g. <c>extensionAttribute5</c>. Optional.</summary>
    public string? TrackAttribute { get; set; }

    /// <summary>
    /// Read leave dates (employeeLeaveDateTime). Needs the extra permission User-LifeCycleInfo.Read.All; without it, a
    /// leaver is known from their leaver checklist and becomes Left when their account is disabled.
    /// </summary>
    public bool ReadLeaveDates { get; set; }

    /// <summary>Hire and leave dates are stored as moments; they are read as dates in this time zone.</summary>
    public string TimeZone { get; set; } = "Australia/Sydney";
}
