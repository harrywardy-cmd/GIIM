using Giim.Connectors.Intune;
using Giim.Domain.Devices;
using Giim.Domain.Importing;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Devices;

/// <summary>
/// Full read of Intune into the ManagedDevices snapshot. Never writes to Intune. On the register side it
/// only updates each asset's IntuneDeviceId and LastSeenInIntune; ownership and status are left to people.
/// </summary>
public sealed class IntuneSyncService(GiimDbContext db, IIntuneClient intune, TimeProvider clock)
{
    public const string Source = "Intune";
    private const int BatchSize = 1000;

    public async Task<SyncRun> SyncAsync(CancellationToken cancellationToken)
    {
        var startedAt = clock.GetUtcNow();
        if (await db.SyncRuns.AnyAsync(r => r.Source == Source && r.Status == SyncRunStatus.Running
                && r.StartedAt > startedAt.AddHours(-1), cancellationToken))
            throw new InvalidOperationException("An Intune sync is already running.");

        var run = new SyncRun { Source = Source, StartedAt = startedAt };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            // In batches: only one batch of records is in memory at a time, so 150,000 devices take little memory
            // and each save only checks its own batch.
            var batch = new List<IntuneDevice>(BatchSize);
            await foreach (var device in intune.GetManagedDevicesAsync(cancellationToken))
            {
                run.DevicesSeen++;
                batch.Add(device);
                if (batch.Count == BatchSize) await SaveBatchAsync(batch, run, startedAt, cancellationToken);
            }
            await SaveBatchAsync(batch, run, startedAt, cancellationToken);

            // Anything a full sync didn't return has been retired or deleted in Intune.
            run.Removed = await db.ManagedDevices
                .Where(d => d.LastSeenBySyncAt < startedAt && d.RemovedFromIntuneAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.RemovedFromIntuneAt, startedAt), cancellationToken);

            run.Status = SyncRunStatus.Succeeded;
            run.CompletedAt = clock.GetUtcNow();
            db.SyncRuns.Update(run);
            await db.SaveChangesAsync(cancellationToken);

            await UpdateAssetsAsync(cancellationToken);
            return run;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            run.Status = SyncRunStatus.Failed;
            run.CompletedAt = clock.GetUtcNow();
            run.Error = e.Message.Length > 2000 ? e.Message[..2000] : e.Message;
            db.SyncRuns.Update(run);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Adds or updates one batch of devices, saves it, and lets go of it.</summary>
    private async Task SaveBatchAsync(List<IntuneDevice> batch, SyncRun run, DateTimeOffset seenAt, CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;
        var ids = batch.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var records = await db.ManagedDevices.Where(d => ids.Contains(d.IntuneId))
            .ToDictionaryAsync(d => d.IntuneId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var device in batch)
        {
            if (records.TryGetValue(device.Id, out var record))
            {
                run.Updated++;
            }
            else
            {
                record = new ManagedDevice { IntuneId = device.Id, DeviceName = "" };
                db.ManagedDevices.Add(record);
                records[device.Id] = record;
                run.Added++;
            }
            Apply(device, record, seenAt);
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        batch.Clear();
    }

    private static void Apply(IntuneDevice source, ManagedDevice target, DateTimeOffset seenAt)
    {
        var serial = ImportNormalizer.Serial(source.SerialNumber);

        target.DeviceName = source.DeviceName ?? "";
        target.SerialNumber = serial is null || ManagedDevice.IsPlaceholderSerial(serial) ? null : serial;
        target.Manufacturer = ImportNormalizer.Manufacturer(source.Manufacturer);
        target.Model = ImportNormalizer.Text(source.Model);
        target.OperatingSystem = ImportNormalizer.Text(source.OperatingSystem);
        target.UserPrincipalName = ImportNormalizer.Text(source.UserPrincipalName)?.ToLowerInvariant();
        target.ComplianceState = source.ComplianceState;
        // Graph reports 0001-01-01 for "never"; store that as unknown.
        target.LastSyncDateTime = source.LastSyncDateTime?.Year > 2000 ? source.LastSyncDateTime : null;
        target.EnrolledDateTime = source.EnrolledDateTime?.Year > 2000 ? source.EnrolledDateTime : null;
        target.LastSeenBySyncAt = seenAt;
        target.RemovedFromIntuneAt = null;
        target.UpdatedAt = seenAt;
    }

    /// <summary>Links each asset to its latest active Intune record in one set-based update.</summary>
    private Task<int> UpdateAssetsAsync(CancellationToken cancellationToken)
    {
        var active = db.ManagedDevices.Where(d => d.RemovedFromIntuneAt == null && d.SerialNumber != null);

        return db.Assets.ExecuteUpdateAsync(s => s
            .SetProperty(a => a.IntuneDeviceId, a => active
                .Where(d => d.SerialNumber == a.SerialNumber)
                .OrderByDescending(d => d.LastSyncDateTime)
                .Select(d => d.IntuneId)
                .FirstOrDefault())
            .SetProperty(a => a.LastSeenInIntune, a => active
                .Where(d => d.SerialNumber == a.SerialNumber)
                .Max(d => d.LastSyncDateTime)),
            cancellationToken);
    }
}
