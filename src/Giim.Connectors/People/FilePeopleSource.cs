using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Giim.Connectors.People;

/// <summary>
/// Reads staff from a CSV with the header
/// EmployeeId,FirstName,LastName,UserPrincipalName,DepartmentCode,JobTitle,Location,Status,StartDate,EndDate,Track,ManagerUpn.
/// </summary>
public sealed class FilePeopleSource(IOptions<PeopleOptions> options) : IPeopleSource
{
    private static readonly string[] RequiredColumns = ["EmployeeId", "FirstName", "LastName", "DepartmentCode", "Status"];

    public async IAsyncEnumerable<DirectoryPerson> GetPeopleAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var departments = await LoadDepartmentNamesAsync(cancellationToken);
        var lines = await File.ReadAllLinesAsync(SamplePath.Resolve(options.Value.FilePath), Encoding.UTF8, cancellationToken);
        if (lines.Length == 0) yield break;

        var header = Csv.Split(lines[0]);
        var missing = RequiredColumns.Where(c => !header.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException($"People file is missing column(s): {string.Join(", ", missing)}");
        int Col(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));

        foreach (var line in lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var cells = Csv.Split(line);
            string? Cell(string name) => Col(name) is var i and >= 0 && i < cells.Length && cells[i].Length > 0 ? cells[i].Trim() : null;

            var code = Cell("DepartmentCode")!;
            yield return new DirectoryPerson(
                EmployeeId: Cell("EmployeeId")!,
                DisplayName: $"{Cell("FirstName")} {Cell("LastName")}".Trim(),
                UserPrincipalName: Cell("UserPrincipalName")?.ToLowerInvariant(),
                DepartmentCode: code,
                DepartmentName: departments.GetValueOrDefault(code),
                JobTitle: Cell("JobTitle"),
                Location: Cell("Location"),
                Status: Cell("Status")!,
                StartDate: ParseDate(Cell("StartDate")),
                EndDate: ParseDate(Cell("EndDate")),
                Track: Cell("Track"),
                ManagerUserPrincipalName: Cell("ManagerUpn")?.ToLowerInvariant());
        }
    }

    private async Task<Dictionary<string, string>> LoadDepartmentNamesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.DepartmentsFilePath)) return [];

        await using var stream = File.OpenRead(SamplePath.Resolve(options.Value.DepartmentsFilePath));
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return json.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("department"))
            .ToDictionary(d => d.GetProperty("code").GetString()!, d => d.GetProperty("name").GetString()!, StringComparer.OrdinalIgnoreCase);
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

/// <summary>Minimal RFC 4180 field splitter: handles quoted fields containing commas and doubled quotes.</summary>
internal static class Csv
{
    public static string[] Split(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }

        fields.Add(current.ToString());
        return [.. fields];
    }
}
