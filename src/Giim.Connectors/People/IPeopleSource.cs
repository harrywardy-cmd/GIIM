namespace Giim.Connectors.People;

/// <summary>
/// A directory of staff. Today a CSV export; on-prem Active Directory plugs in behind the same interface
/// (via the outbound-only on-prem agent) without changing the sync.
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
    string? ManagerUserPrincipalName);

public enum PeopleSource
{
    /// <summary>CSV export (samples/people.csv format). For development and tests.</summary>
    File,
}

public sealed class PeopleOptions
{
    public const string SectionName = "People";

    public PeopleSource Source { get; set; } = PeopleSource.File;
    public string FilePath { get; set; } = "samples/people.csv";

    /// <summary>Optional JSON with department codes and names (samples/departments-and-profiles.json format).</summary>
    public string? DepartmentsFilePath { get; set; } = "samples/departments-and-profiles.json";
}
