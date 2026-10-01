using Giim.Domain.Assets;
using Giim.Domain.Common;

namespace Giim.Domain.Assignments;

/// <summary>
/// Records that a person holds an asset or an app. Offboarding is generated from these,
/// because they reflect what someone actually has, not what their department template says.
/// </summary>
public sealed class Assignment : Entity
{
    public Guid PersonId { get; set; }
    public Guid? AssetId { get; set; }
    public Guid? ApplicationId { get; set; }

    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }

    public Guid? CaseId { get; set; }
    public string? ServiceDeskRequestId { get; set; }

    /// <summary>Technician who issued the asset.</summary>
    public string? AssignedBy { get; set; }
    public string? Notes { get; set; }

    /// <summary>What came with the asset (dock, charger, bag...), checked off when it is returned.</summary>
    public List<AccessoryLine> Accessories { get; init; } = [];

    // Filled in on return.
    public string? ReceivedBy { get; set; }
    public string? ReturnedBy { get; set; }
    public AssetCondition? ReturnCondition { get; set; }
    public string? ReturnTicketNumber { get; set; }

    public bool IsActive => EndedAt is null;

    public static Assignment ForAsset(Guid personId, Guid assetId, string? requestId = null) =>
        new() { PersonId = personId, AssetId = assetId, ServiceDeskRequestId = requestId };

    public static Assignment ForApplication(Guid personId, Guid applicationId, string? requestId = null) =>
        new() { PersonId = personId, ApplicationId = applicationId, ServiceDeskRequestId = requestId };

    /// <summary>A new issue of an asset, recorded against the technician and ticket.</summary>
    public static Assignment Start(Guid personId, Guid assetId, ActionContext context, string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Assignment
        {
            PersonId = personId,
            AssetId = assetId,
            AssignedAt = context.OccurredAt ?? DateTimeOffset.UtcNow,
            AssignedBy = context.Actor,
            ServiceDeskRequestId = string.IsNullOrWhiteSpace(context.TicketNumber) ? null : context.TicketNumber.Trim().ToUpperInvariant(),
            Notes = notes ?? (string.IsNullOrWhiteSpace(context.Note) ? null : context.Note.Trim()),
        };
    }

    /// <summary>
    /// Ends the assignment and checks off accessories. Every line not in <paramref name="returnedLineIds"/> is marked missing.
    /// </summary>
    /// <returns>The accessories that did not come back.</returns>
    public IReadOnlyList<AccessoryLine> Complete(ActionContext context, AssetCondition condition, string? returnedBy,
        IReadOnlySet<Guid> returnedLineIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(returnedLineIds);
        if (!IsActive)
            throw new DomainException("This assignment has already ended.");

        var unknown = returnedLineIds.Where(id => Accessories.All(a => a.Id != id)).ToList();
        if (unknown.Count > 0)
            throw new DomainException("Some returned accessories don't belong to this assignment.");

        foreach (var line in Accessories)
            line.Status = returnedLineIds.Contains(line.Id) ? AccessoryStatus.Returned : AccessoryStatus.Missing;

        EndedAt = context.OccurredAt ?? DateTimeOffset.UtcNow;
        ReceivedBy = context.Actor;
        ReturnedBy = string.IsNullOrWhiteSpace(returnedBy) ? null : returnedBy.Trim();
        ReturnCondition = condition;
        ReturnTicketNumber = string.IsNullOrWhiteSpace(context.TicketNumber) ? null : context.TicketNumber.Trim().ToUpperInvariant();
        UpdatedAt = EndedAt;

        return Accessories.Where(a => a.Status == AccessoryStatus.Missing).ToList();
    }

    /// <summary>
    /// Ends the assignment because the asset was reported lost or stolen: nothing came back, so there is no condition
    /// or receiver. Accessories keep their status; tracked ones (a dock) have their own assignment and stay with the person.
    /// </summary>
    public void EndAsMissing(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsActive)
            throw new DomainException("This assignment has already ended.");

        EndedAt = context.OccurredAt ?? DateTimeOffset.UtcNow;
        ReturnTicketNumber = string.IsNullOrWhiteSpace(context.TicketNumber) ? null : context.TicketNumber.Trim().ToUpperInvariant();
        UpdatedAt = EndedAt;
    }
}

public enum AccessoryStatus { Issued, Returned, Missing }

/// <summary>
/// One accessory issued with an asset. Either another tracked asset (a dock with its own serial), a stock item
/// (a charger), or free text.
/// </summary>
public sealed class AccessoryLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Description { get; init; }
    public int Quantity { get; init; } = 1;
    public Guid? AccessoryAssetId { get; init; }
    public Guid? StockItemId { get; init; }
    public AccessoryStatus Status { get; set; } = AccessoryStatus.Issued;

    public string Label => Quantity > 1 ? $"{Description} x{Quantity}" : Description;
}
