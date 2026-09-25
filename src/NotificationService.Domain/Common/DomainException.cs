namespace NotificationService.Domain.Common;

/// <summary>
/// Raised when an operation would violate a domain rule or invariant.
/// Messages must not contain personal data (addresses, phone numbers), as they can surface in logs and API responses.
/// </summary>
public class DomainException(string message) : Exception(message);
