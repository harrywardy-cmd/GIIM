namespace Giim.Domain.Assets;

/// <summary>What a technician enters when a new device arrives.</summary>
public sealed record NewAsset(
    string SerialNumber,
    string Manufacturer,
    string Model,
    Guid CategoryId,
    string? AssetTag = null,
    DateOnly? PurchaseDate = null,
    DateOnly? WarrantyExpiry = null,
    string? Supplier = null,
    decimal? Cost = null,
    string? PurchaseOrder = null,
    string? Notes = null);
