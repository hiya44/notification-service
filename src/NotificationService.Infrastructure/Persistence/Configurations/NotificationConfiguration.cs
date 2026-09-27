using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Notifications;

namespace NotificationService.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public const string IdempotencyKeyIndexName = "ix_notifications_idempotency_key";

    /// <summary>
    /// Shadow property: until when a dispatcher instance has claimed the notification.
    /// A persistence concern only, so it is not part of the domain model.
    /// </summary>
    public const string LockedUntil = nameof(LockedUntil);

    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new NotificationId(value))
            .ValueGeneratedNever();

        builder.Property(n => n.CustomerId)
            .HasColumnName("customer_id")
            .HasMaxLength(CustomerId.MaxLength)
            .HasConversion(id => id.Value, value => CustomerId.Create(value));

        builder.ComplexProperty(n => n.Recipient, recipient =>
        {
            recipient.Property(r => r.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(20);
            recipient.Property(r => r.Address).HasColumnName("recipient_address").HasMaxLength(EmailAddress.MaxLength);
        });

        builder.ComplexProperty(n => n.Content, content =>
        {
            content.Property(c => c.Subject).HasColumnName("subject").HasMaxLength(NotificationContent.MaxSubjectLength);
            content.Property(c => c.Body).HasColumnName("body").HasMaxLength(NotificationContent.MaxBodyLength);
        });

        builder.Property(n => n.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(IdempotencyKey.MaxLength)
            .HasConversion(key => key!.Value, value => IdempotencyKey.Create(value));

        // Guarantees one notification per key, even for concurrent requests. NULLs are not considered equal.
        builder.HasIndex(n => n.IdempotencyKey).IsUnique().HasDatabaseName(IdempotencyKeyIndexName);

        builder.Property(n => n.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.CreatedAt).HasColumnName("created_at");
        builder.Property(n => n.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(n => n.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(n => n.FailedDispatchCount).HasColumnName("failed_dispatch_count");
        builder.Property(n => n.FailureReason).HasColumnName("failure_reason").HasMaxLength(2000);
        builder.Ignore(n => n.Channel);

        builder.Property<DateTimeOffset?>(LockedUntil).HasColumnName("locked_until");

        // Maps to PostgreSQL's xmin system column: detects concurrent updates without an extra column.
        builder.Property<uint>("Version").IsRowVersion();

        // Partial index covering exactly what the dispatch worker polls for.
        builder.HasIndex(n => n.NextAttemptAt)
            .HasDatabaseName("ix_notifications_due")
            .HasFilter("status = 'Pending'");

        builder.OwnsMany(n => n.DeliveryAttempts, attempts =>
        {
            attempts.ToTable("delivery_attempts");
            attempts.WithOwner().HasForeignKey("NotificationId");
            attempts.Property("NotificationId").HasColumnName("notification_id");
            attempts.Property<long>("Id").HasColumnName("id");
            attempts.HasKey("Id");

            attempts.Property(a => a.ProviderName).HasColumnName("provider_name").HasMaxLength(100);
            attempts.Property(a => a.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(30);
            attempts.Property(a => a.FailureReason).HasColumnName("failure_reason").HasMaxLength(2000);
            attempts.Property(a => a.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(200);
            attempts.Property(a => a.AttemptedAt).HasColumnName("attempted_at");
        });

        builder.Navigation(n => n.DeliveryAttempts)
            .HasField("_deliveryAttempts")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
