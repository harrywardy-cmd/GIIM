namespace Giim.Domain.People;

/// <summary>Splits a display name into the first name and surname Active Directory needs.</summary>
public static class PersonName
{
    /// <summary>"Priya Patel" → (Priya, Patel); "Mary Ann Smith" → (Mary Ann, Smith); "Patel, Priya" → (Priya, Patel).</summary>
    public static (string GivenName, string Surname) Split(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        var name = string.Join(' ', displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var comma = name.IndexOf(',', StringComparison.Ordinal);
        if (comma > 0)
            return (name[(comma + 1)..].Trim(), name[..comma].Trim());
        var lastSpace = name.LastIndexOf(' ');
        return lastSpace < 0 ? (name, "") : (name[..lastSpace], name[(lastSpace + 1)..]);
    }
}
