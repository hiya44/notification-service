using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Notifications;

/// <summary>
/// Accepts a notification for delivery. Delivery itself happens asynchronously (see the dispatch worker),
/// so this only validates the request and stores the notification as Pending.
/// </summary>
public sealed class SendNotificationHandler(INotificationRepository repository, TimeProvider timeProvider)
{
    /// <exception cref="IdempotencyKeyConflictException">The idempotency key was used for a different request.</exception>
    public async Task<SendNotificationResult> HandleAsync(SendNotificationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var customerId = CustomerId.Create(command.CustomerId);
        var recipient = Recipient.Create(command.Channel, command.Recipient);
        var content = NotificationContent.Create(command.Subject, command.Body);
        var idempotencyKey = command.IdempotencyKey is null ? null : IdempotencyKey.Create(command.IdempotencyKey);

        if (idempotencyKey is not null)
        {
            var existing = await repository.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return Duplicate(existing, customerId, recipient, content);
            }
        }

        var notification = Notification.Create(customerId, recipient, content, timeProvider.GetUtcNow(), idempotencyKey);

        if (await repository.TryAddAsync(notification, cancellationToken))
        {
            return new SendNotificationResult(notification.Id, IsDuplicate: false);
        }

        // A concurrent request with the same key was stored between our lookup and insert.
        var winner = await repository.FindByIdempotencyKeyAsync(idempotencyKey!, cancellationToken)
            ?? throw new InvalidOperationException("Notification was reported as a duplicate but could not be found.");

        return Duplicate(winner, customerId, recipient, content);
    }

    private static SendNotificationResult Duplicate(
        Notification existing,
        CustomerId customerId,
        Recipient recipient,
        NotificationContent content)
    {
        if (!existing.IsSameRequestAs(customerId, recipient, content))
        {
            throw new IdempotencyKeyConflictException(existing.IdempotencyKey!);
        }

        return new SendNotificationResult(existing.Id, IsDuplicate: true);
    }
}
