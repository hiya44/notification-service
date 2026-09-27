using MailKit.Net.Smtp;
using NotificationService.Domain.Notifications;
using NotificationService.Infrastructure.Providers.Smtp;

namespace NotificationService.Infrastructure.Tests.Providers;

public class SmtpEmailProviderTests
{
    [Theory]
    [InlineData(SmtpStatusCode.MailboxUnavailable)]
    [InlineData(SmtpStatusCode.UserNotLocalTryAlternatePath)]
    [InlineData(SmtpStatusCode.MailboxNameNotAllowed)]
    public void Classify_WhenRecipientRejectedPermanently_IsPermanentFailure(SmtpStatusCode statusCode)
    {
        var outcome = SmtpEmailProvider.Classify(
            new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, statusCode, "Recipient rejected"));

        outcome.Kind.ShouldBe(DeliveryOutcomeKind.PermanentFailure);
    }

    [Fact]
    public void Classify_WhenRecipientRejectedTemporarily_IsTransientFailure()
    {
        var outcome = SmtpEmailProvider.Classify(
            new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxBusy, "Try again later"));

        outcome.Kind.ShouldBe(DeliveryOutcomeKind.TransientFailure);
    }

    [Theory]
    [InlineData(SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxUnavailable)]
    [InlineData(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed)]
    [InlineData(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.ServiceNotAvailable)]
    public void Classify_WhenRejectedForAnotherReason_IsTransientFailure(SmtpErrorCode errorCode, SmtpStatusCode statusCode)
    {
        var outcome = SmtpEmailProvider.Classify(new SmtpCommandException(errorCode, statusCode, "Rejected"));

        outcome.Kind.ShouldBe(DeliveryOutcomeKind.TransientFailure);
    }

    [Fact]
    public void Classify_DoesNotIncludeServerResponseInReason()
    {
        var outcome = SmtpEmailProvider.Classify(new SmtpCommandException(
            SmtpErrorCode.RecipientNotAccepted,
            SmtpStatusCode.MailboxUnavailable,
            "<jane@example.com>: Recipient address rejected"));

        outcome.FailureReason.ShouldNotBeNull().ShouldNotContain("jane@example.com");
    }
}
