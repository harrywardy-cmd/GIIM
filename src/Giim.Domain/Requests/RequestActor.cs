namespace Giim.Domain.Requests;

/// <summary>
/// The signed-in person acting on a request: their login, their GIIM roles, and their record in the staff
/// directory if they have one (matched on login or email), which is how a manager is recognised as an approver.
/// </summary>
public sealed record RequestActor(
    string Login,
    string DisplayName,
    string? Email,
    Guid? PersonId,
    bool IsAdministrator,
    bool IsTechnician,
    bool IsManager)
{
    /// <summary>Managers, technicians and administrators can raise and comment on requests.</summary>
    public bool CanRaise => IsAdministrator || IsTechnician || IsManager;

    /// <summary>IT: orders, receives and hands over devices.</summary>
    public bool CanProcess => IsAdministrator || IsTechnician;
}
