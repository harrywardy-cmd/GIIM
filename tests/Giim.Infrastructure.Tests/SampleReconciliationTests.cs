using System.Text.Json;
using Giim.Connectors.Intune;
using Giim.Domain.Assets;
using Giim.Domain.Importing;
using Giim.Domain.Reconciliation;
using Giim.Infrastructure.Importing;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Tests;

/// <summary>
/// Imports the fake register, reads the fake Intune export through the real file connector, and checks the
/// reconciliation finds exactly the devices the generator planted (samples/expected-reconciliation.json).
/// </summary>
public class SampleReconciliationTests
{
    // The sample data is dated relative to 29 Sep 2026; a fixed "now" keeps the test stable as time passes.
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Samples = Path.Combine(SampleRegisterImportTests.RepoRoot, "samples");

    private sealed record Expected(string[] IntuneOnlySerials, string[] StaleSerials);

    private static readonly Lazy<(IReadOnlyList<RegisterEntry> Register, IReadOnlyList<IntuneEntry> Intune, IReadOnlyList<ReconciliationRow> Rows, Expected Expected)> Data = new(() =>
    {
        var categories = AssetCategory.Defaults.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        using var file = File.OpenRead(Path.Combine(Samples, "legacy-asset-register.xlsx"));
        var sheet = ExcelSheetReader.Read(file);
        var register = AssetImportAnalyzer
            .Analyze(sheet.Rows, ColumnMapping.Suggest(sheet.Headers), new HashSet<string>(), new HashSet<string>(),
                categories.ToDictionary(c => c.Key, c => c.Value.Id, StringComparer.OrdinalIgnoreCase))
            .Where(r => r.Outcome == ImportRowOutcome.New)
            .Select(r =>
            {
                var asset = r.ToAsset();
                var category = categories[r.CategoryName!];
                return new RegisterEntry(asset.Id, asset.SerialNumber, asset.AssetTag, category.Name, category.IsIntuneManaged, asset.Status, asset.LegacyAssignedTo);
            })
            .ToList();

        var client = new FileIntuneClient(Options.Create(new IntuneOptions { FilePath = Path.Combine(Samples, "intune-devices.json") }));
        var intune = client.GetManagedDevicesAsync().ToBlockingEnumerable()
            .Select(d => new IntuneEntry(d.Id, ImportNormalizer.Serial(d.SerialNumber), d.DeviceName ?? "", d.Model, d.UserPrincipalName, d.LastSyncDateTime))
            .ToList();

        var expected = JsonSerializer.Deserialize<Expected>(File.ReadAllText(Path.Combine(Samples, "expected-reconciliation.json")), JsonSerializerOptions.Web)!;
        return (register, intune, Reconciler.Reconcile(register, intune, Now), expected);
    });

    private static HashSet<string> SerialsWith(Finding finding) =>
        Data.Value.Rows.Where(r => r.Findings.Contains(finding)).Select(r => r.SerialNumber!).ToHashSet();

    private static HashSet<string> RegisterSerials => Data.Value.Register.Select(r => r.SerialNumber).ToHashSet();

    [Fact]
    public void Every_planted_intune_only_device_is_found()
    {
        Assert.Subset(SerialsWith(Finding.IntuneOnly), Data.Value.Expected.IntuneOnlySerials.ToHashSet());
    }

    [Fact]
    public void Intune_only_is_exactly_the_devices_missing_from_the_register()
    {
        var expected = Data.Value.Intune.Select(d => d.SerialNumber!).Where(s => !RegisterSerials.Contains(s)).ToHashSet();

        Assert.Equal(expected, SerialsWith(Finding.IntuneOnly));
    }

    [Fact]
    public void Stale_is_exactly_the_planted_stale_devices_that_are_in_the_register()
    {
        var expected = Data.Value.Expected.StaleSerials.Where(RegisterSerials.Contains).ToHashSet();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, SerialsWith(Finding.Stale));
    }

    [Fact]
    public void Returned_devices_still_checking_in_are_status_conflicts()
    {
        var intuneSerials = Data.Value.Intune.Select(d => d.SerialNumber).ToHashSet();
        var expected = Data.Value.Register
            .Where(r => r.Status == AssetStatus.Returned && intuneSerials.Contains(r.SerialNumber))
            .Select(r => r.SerialNumber)
            .ToHashSet();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, SerialsWith(Finding.StatusConflict));
    }

    [Fact]
    public void Owners_in_the_register_match_intune()
    {
        // The generator gives each device the same owner in Excel and Intune, so any mismatch is a false alarm.
        Assert.Empty(SerialsWith(Finding.OwnerMismatch));
    }

    [Fact]
    public void Monitors_and_docks_are_never_reported_missing_from_intune()
    {
        Assert.Empty(SerialsWith(Finding.NotInIntune));
        Assert.Contains(Data.Value.Register, r => !r.IsIntuneManaged);
    }
}
