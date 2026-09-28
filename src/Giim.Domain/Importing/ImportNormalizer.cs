using System.Globalization;
using Giim.Domain.Assets;

namespace Giim.Domain.Importing;

/// <summary>Cleans up values from legacy registers so they match across Excel, SDP and Intune.</summary>
public static class ImportNormalizer
{
    private static readonly CultureInfo Australian = CultureInfo.GetCultureInfo("en-AU");

    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "d/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy", "d/MM/yy", "d/M/yy", "dd/MM/yy", "d-MMM-yyyy", "d MMM yyyy"];

    private static readonly Dictionary<string, string> ManufacturerAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hp"] = "HP",
        ["hp inc"] = "HP",
        ["hp inc."] = "HP",
        ["hewlett-packard"] = "HP",
        ["hewlett packard"] = "HP",
        ["dell"] = "Dell",
        ["dell inc"] = "Dell",
        ["dell inc."] = "Dell",
        ["lenovo"] = "Lenovo",
        ["apple"] = "Apple",
        ["apple inc."] = "Apple",
        ["samsung"] = "Samsung",
        ["samsung electronics"] = "Samsung",
        ["microsoft"] = "Microsoft",
        ["microsoft corporation"] = "Microsoft",
    };

    /// <summary>Serials are compared without spaces or dashes and in upper case.</summary>
    public static string? Serial(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var cleaned = new string(raw.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return cleaned.Length == 0 ? null : cleaned;
    }

    public static string? Manufacturer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var trimmed = raw.Trim();
        return ManufacturerAliases.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    /// <summary>
    /// Turns common synonyms into the default category names ("Notebook" becomes "Laptop").
    /// Anything else is returned trimmed so it can match a category IT has added.
    /// </summary>
    public static string? CategoryName(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "laptop" or "notebook" => "Laptop",
            "desktop" or "pc" or "workstation" or "mini pc" => "Desktop",
            "monitor" or "screen" or "display" => "Monitor",
            "dock" or "docking station" => "Dock",
            "phone" or "mobile" or "mobile phone" or "smartphone" => "Phone",
            "tablet" or "ipad" => "Tablet",
            "peripheral" or "keyboard" or "mouse" or "headset" => "Peripheral",
            "monitor arm" or "monitor mount" or "mount" or "vesa mount" => "Monitor mount",
            _ => raw.Trim(),
        };

    /// <summary>Maps free-text statuses from old registers onto the lifecycle.</summary>
    public static AssetStatus? Status(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "in use" or "assigned" or "deployed" or "issued" => AssetStatus.Assigned,
            "spare" or "in stock" or "in store" or "available" or "stock" => AssetStatus.InStock,
            "returned" => AssetStatus.Returned,
            "repair" or "in repair" or "rma" or "warranty" => AssetStatus.InRepair,
            "lost" or "stolen" or "missing" => AssetStatus.Lost,
            "disposed" or "retired" or "written off" or "e-waste" => AssetStatus.Disposed,
            _ => null,
        };

    /// <summary>Parses ISO dates (from real Excel date cells) and Australian day-first text dates.</summary>
    public static bool TryDate(string? raw, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (DateOnly.TryParseExact(raw.Trim(), DateFormats, Australian, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
            return true;
        }

        return false;
    }

    public static bool TryCost(string? raw, out decimal? cost)
    {
        cost = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (decimal.TryParse(raw.Trim().TrimStart('$'), NumberStyles.Currency, Australian, out var parsed))
        {
            cost = parsed;
            return true;
        }

        return false;
    }

    public static string? Text(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
}
