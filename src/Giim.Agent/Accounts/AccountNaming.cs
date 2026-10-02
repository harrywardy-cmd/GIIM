using System.Globalization;
using System.Text;

namespace Giim.Agent.Accounts;

/// <summary>
/// Makes a sign-in name from a person's name by the configured format: accents removed ("Zoë" → "zoe"), only letters,
/// digits, dots and hyphens kept, at most 20 characters (the sAMAccountName limit), and a number added if it's taken.
/// </summary>
internal static class AccountNaming
{
    public const int MaxSamLength = 20;

    public static string Base(string format, string givenName, string surname)
    {
        var first = Clean(givenName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "");
        var last = Clean(surname);
        if (first.Length == 0 && last.Length == 0) throw new DirectoryException("The person has no usable name for an account.", retryable: false);

        var name = format
            .Replace("{first}", first, StringComparison.OrdinalIgnoreCase)
            .Replace("{last}", last, StringComparison.OrdinalIgnoreCase)
            .Replace("{f}", first.Length > 0 ? first[..1] : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{l}", last.Length > 0 ? last[..1] : "", StringComparison.OrdinalIgnoreCase);
        name = Clean(name).Trim('.', '-');
        if (name.Length == 0) name = first.Length > 0 ? first : last;
        return name.Length <= MaxSamLength ? name : name[..MaxSamLength].TrimEnd('.', '-');
    }

    /// <summary>The first free name: priya.patel, then priya.patel2, priya.patel3… (shortened to fit if needed).</summary>
    public static async Task<string> FreeAsync(string baseName, Func<string, Task<bool>> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        if (!await taken(baseName)) return baseName;
        for (var n = 2; n < 100; n++)
        {
            var suffix = n.ToString(CultureInfo.InvariantCulture);
            var candidate = (baseName.Length + suffix.Length <= MaxSamLength ? baseName : baseName[..(MaxSamLength - suffix.Length)]) + suffix;
            if (!await taken(candidate)) return candidate;
        }
        throw new DirectoryException($"No free sign-in name based on {baseName}.", retryable: false);
    }

    private static string Clean(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var kept = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var lower = char.ToLowerInvariant(c);
            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-') kept.Append(lower);
        }
        return kept.ToString();
    }
}
