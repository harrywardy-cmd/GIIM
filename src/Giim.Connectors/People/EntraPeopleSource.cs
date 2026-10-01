using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Azure.Core;
using Giim.Connectors.Graph;
using Giim.Domain.People;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Giim.Connectors.People;

/// <summary>
/// Reads staff from Entra ID through Microsoft Graph with app-only auth: the application permission User.Read.All
/// (read-only), plus User-LifeCycleInfo.Read.All if leave dates are read. Accounts synced from on-prem AD carry the
/// employee ID, department, job title and manager. Accounts without an employee ID (rooms, shared mailboxes,
/// service accounts) are skipped. See docs/staff-directory.md.
/// </summary>
public sealed partial class EntraPeopleSource(
    HttpClient http, TokenCredential credential, IOptions<PeopleOptions> options, TimeProvider clock, ILogger<EntraPeopleSource> logger)
    : IPeopleSource
{
    internal const string NoDepartmentCode = "NONE";
    internal const string NoDepartmentName = "No department in Entra";
    private const int DepartmentCodeLength = 20;

    private EntraDirectoryOptions O => options.Value.Entra;

    public async IAsyncEnumerable<DirectoryPerson> GetPeopleAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var codes = DepartmentCodeMap(O.DepartmentCodes);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(O.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        DateOnly? Date(DateTimeOffset? value) =>
            value is { } v && v.Year > 1900 ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(v, zone).DateTime) : null;

        var read = 0;
        var skipped = 0;
        await foreach (var user in GraphReader.ReadAllAsync<GraphUser>(http, credential, UsersUrl(), O.MaxRetries, logger, cancellationToken))
        {
            read++;
            if (string.IsNullOrWhiteSpace(user.EmployeeId))
            {
                skipped++;
                continue;
            }

            var start = Date(user.EmployeeHireDate);
            var end = O.ReadLeaveDates ? Date(user.EmployeeLeaveDateTime) : null;
            var department = Clean(user.Department);
            var upn = Clean(user.UserPrincipalName)?.ToLowerInvariant();

            yield return new DirectoryPerson(
                EmployeeId: user.EmployeeId.Trim(),
                DisplayName: Clean(user.DisplayName) ?? upn ?? user.EmployeeId.Trim(),
                UserPrincipalName: upn,
                DepartmentCode: DepartmentCode(department, codes),
                DepartmentName: department ?? NoDepartmentName,
                JobTitle: Clean(user.JobTitle),
                Location: Clean(user.OfficeLocation),
                Status: Status(user.AccountEnabled, start, end, today),
                StartDate: start,
                EndDate: end,
                Track: Clean(O.TrackAttribute) is { } attribute ? Clean(user.OnPremisesExtensionAttributes?.GetValueOrDefault(attribute)) : null,
                ManagerUserPrincipalName: Clean(user.Manager?.UserPrincipalName)?.ToLowerInvariant(),
                Email: Clean(user.Mail)?.ToLowerInvariant(),
                EntraObjectId: Guid.TryParse(user.Id, out var id) ? id : null);
        }

        LogRead(logger, read, skipped);
    }

    private Uri UsersUrl()
    {
        var fields = "id,displayName,userPrincipalName,mail,employeeId,department,jobTitle,officeLocation,accountEnabled,"
            + "employeeHireDate,onPremisesExtensionAttributes" + (O.ReadLeaveDates ? ",employeeLeaveDateTime" : "");
        var filter = "userType eq 'Member'" + (string.IsNullOrWhiteSpace(O.Filter) ? "" : $" and ({O.Filter.Trim()})");
        // Pages of 100: Graph keeps pages small when the manager is expanded.
        return new Uri(O.GraphBaseUrl,
            $"users?$select={fields}&$expand=manager($select=id,userPrincipalName)&$filter={Uri.EscapeDataString(filter)}&$top=100");
    }

    /// <summary>
    /// Pending until the hire date; Left once the account is disabled or the leave date has passed; Leaving while a
    /// leave date is set; otherwise Active.
    /// </summary>
    internal static string Status(bool? accountEnabled, DateOnly? start, DateOnly? end, DateOnly today) =>
        start > today ? nameof(PersonStatus.Pending)
        : accountEnabled == false ? nameof(PersonStatus.Left)
        : end is { } leaves ? (leaves < today ? nameof(PersonStatus.Left) : nameof(PersonStatus.Leaving))
        : nameof(PersonStatus.Active);

    /// <summary>The configured code for an Entra department name, or one made from the name (stable between syncs).</summary>
    internal static string DepartmentCode(string? name, IReadOnlyDictionary<string, string> configured)
    {
        if (name is null) return NoDepartmentCode;
        if (configured.TryGetValue(name, out var code)) return code;

        var made = new StringBuilder();
        foreach (var c in name.ToUpperInvariant())
            if (char.IsAsciiLetterOrDigit(c)) made.Append(c);
            else if (made.Length > 0 && made[^1] != '-') made.Append('-');
        var text = made.ToString().Trim('-');
        if (text.Length == 0) text = "DEPT";
        if (text.Length <= DepartmentCodeLength && string.Equals(text, name, StringComparison.OrdinalIgnoreCase)) return text;

        // Shortened or changed: add a short fingerprint of the full name, so two long names never share a code.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.ToUpperInvariant())))[..4];
        var keep = Math.Min(text.Length, DepartmentCodeLength - 5);
        return $"{text[..keep].TrimEnd('-')}-{hash}";
    }

    internal static Dictionary<string, string> DepartmentCodeMap(IReadOnlyDictionary<string, string> configured)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, code) in configured)
        {
            var clean = Clean(code)?.ToUpperInvariant()
                ?? throw new InvalidOperationException($"People:Entra:DepartmentCodes has no code for “{name}”.");
            if (clean.Length > DepartmentCodeLength)
                throw new InvalidOperationException($"People:Entra:DepartmentCodes: “{clean}” is longer than {DepartmentCodeLength} characters.");
            map[name.Trim()] = clean;
        }
        return map;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Information, Message = "Read {Read} Entra accounts; {Skipped} skipped because they have no employee ID.")]
    private static partial void LogRead(ILogger logger, int read, int skipped);

    internal sealed record GraphUser(
        string Id,
        string? DisplayName,
        string? UserPrincipalName,
        string? Mail,
        string? EmployeeId,
        string? Department,
        string? JobTitle,
        string? OfficeLocation,
        bool? AccountEnabled,
        DateTimeOffset? EmployeeHireDate,
        DateTimeOffset? EmployeeLeaveDateTime,
        Dictionary<string, string?>? OnPremisesExtensionAttributes,
        GraphManager? Manager);

    internal sealed record GraphManager(string? Id, string? UserPrincipalName);
}
