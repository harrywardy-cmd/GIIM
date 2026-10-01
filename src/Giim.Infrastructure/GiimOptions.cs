namespace Giim.Infrastructure;

/// <summary>The address staff use to open GIIM (Giim:PublicBaseUrl), for links in emails and ticket notes.</summary>
public sealed class GiimOptions
{
    public string? PublicBaseUrl { get; set; }

    /// <summary>The address without a trailing slash, or null if not set (links are then left out).</summary>
    public string? BaseUrl => string.IsNullOrWhiteSpace(PublicBaseUrl) ? null : PublicBaseUrl.TrimEnd('/');
}
