using Giim.Domain.Common;
using Giim.Domain.Locations;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Locations;

public sealed class LocationService(GiimDbContext db)
{
    /// <summary>Loads a location for an action; null id means "no location given".</summary>
    public async Task<Location?> FindAsync(Guid? id, CancellationToken cancellationToken) =>
        id is { } locationId
            ? await db.Locations.FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken)
              ?? throw new DomainException("That location doesn't exist.")
            : null;

    public async Task<Location> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await FindAsync(id, cancellationToken) ?? throw new DomainException("Choose a location.");

    /// <summary>
    /// Maps location names (e.g. from an imported spreadsheet) to managed locations, creating any that don't exist.
    /// Matching ignores case and extra spaces. New locations are added to the context but not saved.
    /// </summary>
    public async Task<(Dictionary<string, Location> ByName, IReadOnlyList<string> Created)> ResolveAsync(
        IEnumerable<string> names, bool create, CancellationToken cancellationToken)
    {
        var wanted = names.Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(Location.CleanName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = await db.Locations.ToListAsync(cancellationToken);
        var byName = existing.ToDictionary(l => l.Name, StringComparer.OrdinalIgnoreCase);
        var created = new List<string>();

        foreach (var name in wanted.Where(n => !byName.ContainsKey(n)))
        {
            created.Add(name);
            if (!create) continue;

            var kind = Location.GuessKind(name);
            var location = new Location { Name = name, Kind = kind, HoldsStock = kind is LocationKind.ItStoreRoom or LocationKind.DistributionCentre };
            db.Locations.Add(location);
            byName[name] = location;
        }

        return (byName, created);
    }

    public static string Key(string name) => Location.CleanName(name);
}
