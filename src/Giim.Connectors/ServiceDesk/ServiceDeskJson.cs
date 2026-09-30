using System.Globalization;
using System.Text.Json;

namespace Giim.Connectors.ServiceDesk;

/// <summary>Reads the ServiceDesk Plus v3 request format, shared by the API client and the file stand-in.</summary>
public static class ServiceDeskJson
{
    public const string MediaType = "application/vnd.manageengine.sdp.v3+json";

    public static ServiceDeskRequest ParseRequest(JsonElement request)
    {
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in request.EnumerateObject())
        {
            if (property.Name == "udf_fields" && property.Value.ValueKind == JsonValueKind.Object)
                foreach (var udf in property.Value.EnumerateObject())
                    fields[udf.Name] = Value(udf.Value);
            else
                fields[property.Name] = Value(property.Value);
        }

        return new ServiceDeskRequest(
            Key: Text(request, "id") ?? throw new FormatException("The ticket has no id."),
            DisplayId: Text(request, "display_id") ?? Text(request, "id")!,
            Subject: Text(request, "subject") ?? "",
            Template: Name(request, "template"),
            Status: Name(request, "status"),
            RequesterEmail: request.TryGetProperty("requester", out var requester) && requester.ValueKind == JsonValueKind.Object
                ? Text(requester, "email_id") : null,
            Fields: fields);
    }

    /// <summary>
    /// A field as text: plain values as they are; lookups ({"name": …}) by name; dates ({"value": ms, "display_value": …})
    /// by their millisecond value, so they can be converted in the right time zone.
    /// </summary>
    private static string? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Object when value.TryGetProperty("value", out var v) => v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText(),
        JsonValueKind.Object when value.TryGetProperty("name", out var n) => n.GetString(),
        JsonValueKind.Object when value.TryGetProperty("email_id", out var e) => e.GetString(),
        _ => null,
    };

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    private static string? Name(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("name", out var name)
            ? name.GetString()
            : null;

    /// <summary>A date from a ticket field: SDP's milliseconds value (in the given time zone), or yyyy-mm-dd, or dd/mm/yyyy.</summary>
    public static DateOnly? ParseDate(string? value, TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) && value.Length >= 10)
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), zone).DateTime);
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)) return iso;
        if (DateOnly.TryParseExact(value, ["dd/MM/yyyy", "d/M/yyyy", "dd MMM yyyy", "d MMM yyyy"], CultureInfo.GetCultureInfo("en-AU"),
                DateTimeStyles.None, out var au)) return au;
        return null;
    }
}
