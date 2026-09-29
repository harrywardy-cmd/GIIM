using System.Security.Claims;

namespace Giim.Api.Security;

/// <summary>Turns Okta group names into GIIM roles using the configured group-to-role map.</summary>
internal static class GroupRoleMapper
{
    public const string GroupClaim = "groups";

    public static IReadOnlyList<string> RolesFor(IEnumerable<string> groups, IReadOnlyDictionary<string, string> roleGroups)
    {
        var memberOf = groups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return roleGroups
            .Where(rg => Roles.All.Contains(rg.Key) && memberOf.Contains(rg.Value))
            .Select(rg => rg.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Adds role claims for the user's Okta groups; called once when they sign in.</summary>
    public static void AddRoleClaims(ClaimsIdentity identity, IReadOnlyDictionary<string, string> roleGroups)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var groups = identity.FindAll(GroupClaim).Select(c => c.Value);
        foreach (var role in RolesFor(groups, roleGroups))
            identity.AddClaim(new Claim(identity.RoleClaimType, role));
    }
}
