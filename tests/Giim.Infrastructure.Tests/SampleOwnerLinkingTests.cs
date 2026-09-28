using Giim.Connectors.Intune;
using Giim.Connectors.People;
using Giim.Domain.Assets;
using Giim.Domain.Importing;
using Giim.Domain.People;
using Giim.Infrastructure.Importing;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Tests;

/// <summary>
/// Links the fake register's "Assigned To" names to the fake directory, and checks every automatic link against
/// what Intune says about who actually uses the device. The sample has 428 names shared by several people.
/// </summary>
public class SampleOwnerLinkingTests
{
    private static readonly string Samples = Path.Combine(SampleRegisterImportTests.RepoRoot, "samples");

    private static readonly Lazy<(IReadOnlyList<OwnerMatch> Matches, Dictionary<Guid, string> AssetSerials,
        Dictionary<Guid, string?> PersonUpns, Dictionary<string, string> IntuneUsers)> Data = new(() =>
    {
        var categories = AssetCategory.Defaults.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        using var file = File.OpenRead(Path.Combine(Samples, "legacy-asset-register.xlsx"));
        var sheet = ExcelSheetReader.Read(file);
        var assets = AssetImportAnalyzer
            .Analyze(sheet.Rows, ColumnMapping.Suggest(sheet.Headers), new HashSet<string>(), new HashSet<string>(), categories)
            .Where(r => r.Outcome == ImportRowOutcome.New)
            .Select(r => r.ToAsset())
            .Where(a => a.Status == AssetStatus.Assigned && a.LegacyAssignedTo is not null)
            .ToList();

        var intune = new FileIntuneClient(Options.Create(new IntuneOptions { FilePath = Path.Combine(Samples, "intune-devices.json") }))
            .GetManagedDevicesAsync().ToBlockingEnumerable()
            .Where(d => d.SerialNumber is not null && d.UserPrincipalName is not null)
            .GroupBy(d => ImportNormalizer.Serial(d.SerialNumber)!)
            .ToDictionary(g => g.Key, g => g.MaxBy(d => d.LastSyncDateTime)!.UserPrincipalName!, StringComparer.Ordinal);

        var people = new FilePeopleSource(Options.Create(new PeopleOptions
            {
                FilePath = Path.Combine(Samples, "people.csv"),
                DepartmentsFilePath = Path.Combine(Samples, "departments-and-profiles.json"),
            }))
            .GetPeopleAsync().ToBlockingEnumerable()
            .Select(p => (Id: Guid.NewGuid(), p))
            .ToList();

        var matches = LegacyOwnerMatcher.Match(
            assets.Select(a => new LegacyOwnedAsset(a.Id, a.LegacyAssignedTo!, intune.GetValueOrDefault(a.SerialNumber), a.LegacyDepartment)),
            people.Select(x => new OwnerCandidate(x.Id, x.p.DisplayName, x.p.UserPrincipalName, x.p.DepartmentCode, x.p.DepartmentName)));

        return (matches, assets.ToDictionary(a => a.Id, a => a.SerialNumber), people.ToDictionary(x => x.Id, x => x.p.UserPrincipalName), intune);
    });

    [Fact]
    public void No_automatic_link_contradicts_intune()
    {
        var (matches, serials, upns, intune) = Data.Value;

        var checkable = matches
            .Where(m => m.Outcome == OwnerMatchOutcome.Matched && intune.ContainsKey(serials[m.AssetId]))
            .ToList();

        Assert.NotEmpty(checkable);
        Assert.All(checkable, m => Assert.Equal(intune[serials[m.AssetId]], upns[m.PersonId!.Value], ignoreCase: true));
    }

    [Fact]
    public void Department_resolves_most_shared_names()
    {
        var matches = Data.Value.Matches;

        Assert.Contains(matches, m => m.MatchedBy == "name and department");
        // Only same-name-same-department cases should be left for a technician (a few percent of assets).
        Assert.InRange(matches.Count(m => m.Outcome == OwnerMatchOutcome.Ambiguous), 1, matches.Count / 20);
    }

    [Fact]
    public void Every_register_owner_exists_in_the_directory()
    {
        Assert.DoesNotContain(Data.Value.Matches, m => m.Outcome == OwnerMatchOutcome.NotFound);
    }
}
