using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

internal sealed class AuthenticationEmailOutboxMessageConfiguration
    : IEntityTypeConfiguration<AuthenticationEmailOutboxMessage>
{
    /// <summary>
    /// Executes the configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<AuthenticationEmailOutboxMessage> builder)
    {
        builder.ToTable(
            "authentication_email_outbox",
            table =>
        {
            table.HasCheckConstraint(
                "ck_authentication_email_outbox_attempt_count_non_negative",
                "attempt_count >= 0");
            table.HasCheckConstraint(
                "ck_authentication_email_outbox_kind_valid",
                "kind IN ('EMAIL_CONFIRMATION', 'EMAIL_CHANGE_CONFIRMATION', " +
                "'EMAIL_CHANGE_SECURITY_NOTIFICATION', 'PASSWORD_CHANGED_SECURITY_NOTIFICATION')");
            table.HasCheckConstraint(
                "ck_authentication_email_outbox_email_change_fields_consistent",
                "(kind = 'EMAIL_CONFIRMATION' AND member_email_change_request_id IS NULL " +
                "AND recipient_email IS NULL) OR " +
                "(kind IN ('EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION') " +
                "AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL) OR " +
                "(kind = 'PASSWORD_CHANGED_SECURITY_NOTIFICATION' " +
                "AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_authentication_email_outbox_timestamps_consistent",
                "available_at >= created_at AND " +
                "(processed_at IS NULL OR processed_at >= created_at)");
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Kind)
            .HasConversion(
                kind => ConvertKindToDatabase(kind),
                value => ConvertKindFromDatabase(value))
            .HasMaxLength(50);
        builder.Property(message => message.LastError)
            .HasMaxLength(1000);
        builder.Property(message => message.ProviderMessageId)
            .HasMaxLength(255);
        builder.Property(message => message.RecipientEmail)
            .HasMaxLength(254);

        builder.HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(message => message.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_authentication_email_outbox_users_user_id");

        builder.HasOne<MemberEmailChangeRequest>()
            .WithMany()
            .HasForeignKey(message => message.MemberEmailChangeRequestId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_authentication_email_outbox_member_email_change_request_id");

        builder.HasIndex(message => new
        {
            message.UserId,
            message.Kind
        })
            .HasDatabaseName("ux_authentication_email_outbox_pending_user_kind")
            .IsUnique()
            .HasFilter(
                "processed_at IS NULL AND kind <> 'PASSWORD_CHANGED_SECURITY_NOTIFICATION'");

        builder.HasIndex(message => new
        {
            message.AvailableAt,
            message.CreatedAt
        })
            .HasDatabaseName("ix_authentication_email_outbox_pending_delivery")
            .HasFilter("processed_at IS NULL");

        builder.HasIndex(message => new
        {
            message.UserId,
            message.Kind,
            message.CreatedAt
        })
            .HasDatabaseName("ix_authentication_email_outbox_user_kind_created_at")
            .IsDescending(
                false,
                false,
                true);
    }

    private static string ConvertKindToDatabase(AuthenticationEmailKind kind)
    {

        return kind switch
        {
            AuthenticationEmailKind.EmailConfirmation => "EMAIL_CONFIRMATION",
            AuthenticationEmailKind.EmailChangeConfirmation => "EMAIL_CHANGE_CONFIRMATION",
            AuthenticationEmailKind.EmailChangeSecurityNotification =>
                "EMAIL_CHANGE_SECURITY_NOTIFICATION",
            AuthenticationEmailKind.PasswordChangedSecurityNotification =>
                "PASSWORD_CHANGED_SECURITY_NOTIFICATION",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown authentication email kind.")
        };
    }

    private static AuthenticationEmailKind ConvertKindFromDatabase(string value)
    {

        return value switch
        {
            "EMAIL_CONFIRMATION" => AuthenticationEmailKind.EmailConfirmation,
            "EMAIL_CHANGE_CONFIRMATION" => AuthenticationEmailKind.EmailChangeConfirmation,
            "EMAIL_CHANGE_SECURITY_NOTIFICATION" =>
                AuthenticationEmailKind.EmailChangeSecurityNotification,
            "PASSWORD_CHANGED_SECURITY_NOTIFICATION" =>
                AuthenticationEmailKind.PasswordChangedSecurityNotification,
            _ => throw new InvalidOperationException($"Unknown authentication email kind '{value}'.")
        };
    }
}
