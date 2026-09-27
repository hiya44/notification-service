using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Notifications;

public sealed class GetNotificationHandler(INotificationRepository repository)
{
    /// <returns>The notification's details, or null if it does not exist.</returns>
    public async Task<NotificationDetails?> HandleAsync(NotificationId id, CancellationToken cancellationToken)
    {
        var notification = await repository.GetAsync(id, cancellationToken);
        return notification is null ? null : NotificationDetails.From(notification);
    }
}
