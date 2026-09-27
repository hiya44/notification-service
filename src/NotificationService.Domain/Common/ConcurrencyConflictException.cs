namespace NotificationService.Domain.Common;

/// <summary>
/// An aggregate could not be saved because another process changed it after it was loaded.
/// </summary>
public class ConcurrencyConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
