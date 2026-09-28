namespace Giim.Domain.Common;

/// <summary>Raised when an operation would break a business rule.</summary>
public sealed class DomainException(string message) : Exception(message);
