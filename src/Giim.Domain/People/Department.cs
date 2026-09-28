using Giim.Domain.Common;

namespace Giim.Domain.People;

public sealed class Department : Entity
{
    public required string Name { get; set; }
    public required string Code { get; set; }
    public string? CostCentre { get; set; }
}
