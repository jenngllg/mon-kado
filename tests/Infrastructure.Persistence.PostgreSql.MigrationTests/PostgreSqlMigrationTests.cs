using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.MigrationTests;

[Collection(PostgreSqlMigrationTestSuite.Name)]
public class PostgreSqlMigrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task MigrateAsync_WhenMigrations_AreIdempotentAndMatchTheCurrentModel()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        await context.Database.MigrateAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);

        // Act
        var migrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        // Assert
        Assert.Collection(
            migrations,
            migration => Assert.EndsWith(
                "_InitialPersistenceBaseline",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddIdentityAndAccountRegistration",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddEmailConfirmationRequestThrottling",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddAuthenticationEmailDeliveryTracking",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddAuthenticationSessions",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddAuditableUtcDates",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_ReplaceAuthenticationTicketsWithRefreshSessions",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddMemberRole",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_UseMemberXminVersion",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddMemberEmailChanges",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddMemberPasswordChanges",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddMemberPasswordResets",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddGoogleExternalLogins",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddAuthenticationEmailRetentionCleanupIndex",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddWishlists",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddWishes",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddWishCollectionOrdering",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddWishlistShareLinks",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddWishlistParticipants",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddGiftReservations",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddWishlistReports",
                migration,
                StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());

        var tables = await GetPublicTablesAsync(
            context,
            cancellationToken);
        Assert.Equal(
            [
                "__EFMigrationsHistory",
                "authentication_email_outbox",
                "authentication_sessions",
                "gift_reservations",
                "guest_sessions",
                "member_email_change_requests",
                "role_claims",
                "roles",
                "user_claims",
                "user_logins",
                "user_roles",
                "user_tokens",
                "users",
                "wish_position_sequences",
                "wishes",
                "wishlist_participants",
                "wishlist_reports",
                "wishlist_share_links",
                "wishlists"
            ],
            tables);

        var constraints = await GetPublicConstraintsAsync(
            context,
            cancellationToken);
        Assert.Contains(
            "ck_users_display_name_valid",
            constraints);
        Assert.Contains(
            "ck_users_timestamps_consistent",
            constraints);
        Assert.DoesNotContain(
            "ck_users_version_positive",
            constraints);
        Assert.Contains(
            "ck_authentication_email_outbox_kind_valid",
            constraints);
        Assert.Contains(
            "fk_authentication_email_outbox_users_user_id",
            constraints);
        Assert.Contains(
            "ck_authentication_sessions_refresh_token_hash_length",
            constraints);
        Assert.Contains(
            "ck_authentication_sessions_timestamps_consistent",
            constraints);
        Assert.Contains(
            "fk_authentication_sessions_users_user_id",
            constraints);
        Assert.Contains(
            "ck_authentication_email_outbox_email_change_fields_consistent",
            constraints);
        Assert.Contains(
            "ck_member_email_change_requests_emails_different",
            constraints);
        Assert.Contains(
            "ck_member_email_change_requests_timestamps_consistent",
            constraints);
        Assert.Contains(
            "fk_authentication_email_outbox_member_email_change_request_id",
            constraints);
        Assert.Contains(
            "fk_member_email_change_requests_users_user_id",
            constraints);
        Assert.Contains(
            "ck_wishlists_name_valid",
            constraints);
        Assert.Contains(
            "ck_wishlists_occasion_valid",
            constraints);
        Assert.Contains(
            "ck_wishlists_timestamps_consistent",
            constraints);
        Assert.Contains(
            "fk_wishlists_users_owner_id",
            constraints);
        Assert.Contains(
            "ck_wish_position_sequences_next_position_valid",
            constraints);
        Assert.Contains(
            "ck_wish_position_sequences_current_count_valid",
            constraints);
        Assert.Contains(
            "fk_wish_position_sequences_wishlists_wishlist_id",
            constraints);
        Assert.Contains(
            "ck_wishes_name_valid",
            constraints);
        Assert.Contains(
            "ck_wishes_position_valid",
            constraints);
        Assert.Contains(
            "ck_wishes_price_valid",
            constraints);
        Assert.Contains(
            "ck_wishes_quantity_valid",
            constraints);
        Assert.Contains(
            "ck_wishes_timestamps_consistent",
            constraints);
        Assert.Contains(
            "ck_wishes_url_valid",
            constraints);
        Assert.Contains(
            "fk_wishes_wishlists_wishlist_id",
            constraints);
        Assert.Contains(
            "ck_wishlist_share_links_secret_hash_length",
            constraints);
        Assert.Contains(
            "ck_wishlist_share_links_timestamps_consistent",
            constraints);
        Assert.Contains(
            "fk_wishlist_share_links_wishlists_wishlist_id",
            constraints);
        Assert.Contains(
            "ck_guest_sessions_secret_hash_length",
            constraints);
        Assert.Contains(
            "ck_wishlist_participants_identity",
            constraints);
        Assert.Contains(
            "fk_wishlist_participants_guest_sessions_guest_session_id",
            constraints);
        Assert.Contains(
            "fk_wishlist_participants_users_member_id",
            constraints);
        Assert.Contains(
            "fk_wishlist_participants_wishlists_wishlist_id",
            constraints);
        Assert.Contains(
            "ck_gift_reservations_quantity_valid",
            constraints);
        Assert.Contains(
            "ck_gift_reservations_timestamps_consistent",
            constraints);
        Assert.Contains(
            "fk_gift_reservations_participants_wishlist_id_participant_id",
            constraints);
        Assert.Contains(
            "fk_gift_reservations_wishes_wishlist_id_wish_id",
            constraints);
        Assert.Contains(
            "ck_wishlist_reports_reason_valid",
            constraints);
        Assert.Contains(
            "ck_wishlist_reports_timestamps_consistent",
            constraints);
        Assert.Contains(
            "fk_wishlist_reports_wishlists_wishlist_id",
            constraints);

        var indexes = await GetPublicIndexesAsync(
            context,
            cancellationToken);
        Assert.Contains(
            "ux_users_normalized_email",
            indexes);
        Assert.Contains(
            "ix_users_unconfirmed_account_expiry",
            indexes);
        Assert.Contains(
            "ux_authentication_email_outbox_pending_user_kind",
            indexes);
        Assert.Contains(
            "ix_authentication_email_outbox_pending_delivery",
            indexes);
        Assert.Contains(
            "ix_authentication_email_outbox_user_kind_created_at",
            indexes);
        Assert.Contains(
            "ix_authentication_email_outbox_processed_cleanup",
            indexes);
        Assert.Contains(
            "ix_authentication_sessions_expires_at",
            indexes);
        Assert.Contains(
            "ix_authentication_sessions_user_id",
            indexes);
        Assert.Contains(
            "ix_authentication_email_outbox_member_email_change_request_id",
            indexes);
        Assert.Contains(
            "ix_member_email_change_requests_expires_at",
            indexes);
        Assert.Contains(
            "ux_member_email_change_requests_active_user",
            indexes);
        Assert.Contains(
            "ux_user_logins_user_id_login_provider",
            indexes);
        Assert.Contains(
            "ux_gift_reservations_participant_wish",
            indexes);
        Assert.Contains(
            "ux_wishlists_owner_normalized_name",
            indexes);
        Assert.Contains(
            "ux_wishes_wishlist_position",
            indexes);
        Assert.Contains(
            "ux_wishlist_share_links_secret_hash",
            indexes);
        Assert.Contains(
            "ux_wishlist_share_links_wishlist_id",
            indexes);
        Assert.Contains(
            "ix_guest_sessions_expires_at",
            indexes);
        Assert.Contains(
            "ux_wishlist_participants_wishlist_guest_session",
            indexes);
        Assert.Contains(
            "ux_wishlist_participants_wishlist_member",
            indexes);
        Assert.Contains(
            "ix_wishlist_reports_wishlist_id",
            indexes);

        var columns = await GetAuthenticationEmailOutboxColumnsAsync(
            context,
            cancellationToken);
        Assert.Contains(
            "provider_message_id",
            columns);
        Assert.Contains(
            "member_email_change_request_id",
            columns);
        Assert.Contains(
            "recipient_email",
            columns);
        Assert.Contains(
            "security_stamp_snapshot",
            columns);
        Assert.Equal(
            [
                "confirmed_at",
                "created_at",
                "current_email",
                "expires_at",
                "id",
                "new_email",
                "normalized_new_email",
                "revoked_at",
                "user_id"
            ],
            await GetMemberEmailChangeRequestColumnsAsync(
                context,
                cancellationToken));
        Assert.Equal(
            [
                "created_at",
                "expires_at",
                "id",
                "is_persistent",
                "refresh_token_hash",
                "renewed_at",
                "revoked_at",
                "user_id"
            ],
            await GetAuthenticationSessionColumnsAsync(
                context,
                cancellationToken));
        Assert.True(await IsUserUpdatedAtNullableAsync(
            context,
            cancellationToken));
        Assert.False(await HasUserVersionColumnAsync(
            context,
            cancellationToken));
    }

    [Fact]
    public async Task MigrateAsync_WhenWishlistMigrationIsRolledBack_RemovesAndRecreatesWishlistSchema()
    {
        // Arrange
        const string PreviousMigration = "20260824160547_AddAuthenticationEmailRetentionCleanupIndex";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        Assert.Contains(
            "wishlists",
            await GetPublicTablesAsync(
                context,
                cancellationToken));

        // Act
        await context.Database.MigrateAsync(
            PreviousMigration,
            cancellationToken);
        var tablesAfterDown = await GetPublicTablesAsync(
            context,
            cancellationToken);
        var indexesAfterDown = await GetPublicIndexesAsync(
            context,
            cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
        var tablesAfterUp = await GetPublicTablesAsync(
            context,
            cancellationToken);
        var indexesAfterUp = await GetPublicIndexesAsync(
            context,
            cancellationToken);

        // Assert
        Assert.DoesNotContain(
            "wishlists",
            tablesAfterDown);
        Assert.DoesNotContain(
            "ux_wishlists_owner_normalized_name",
            indexesAfterDown);
        Assert.Contains(
            "wishlists",
            tablesAfterUp);
        Assert.Contains(
            "ux_wishlists_owner_normalized_name",
            indexesAfterUp);
    }

    [Fact]
    public async Task MigrateAsync_WhenWishMigrationIsRolledBack_RemovesAndRecreatesWishSchema()
    {
        // Arrange
        const string PreviousMigration = "20260824222848_AddWishlists";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(cancellationToken);

        // Act
        await context.Database.MigrateAsync(
            PreviousMigration,
            cancellationToken);
        var tablesAfterDown = await GetPublicTablesAsync(
            context,
            cancellationToken);
        var indexesAfterDown = await GetPublicIndexesAsync(
            context,
            cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
        var tablesAfterUp = await GetPublicTablesAsync(
            context,
            cancellationToken);
        var indexesAfterUp = await GetPublicIndexesAsync(
            context,
            cancellationToken);

        // Assert
        Assert.DoesNotContain(
            "wishes",
            tablesAfterDown);
        Assert.DoesNotContain(
            "wish_position_sequences",
            tablesAfterDown);
        Assert.DoesNotContain(
            "ux_wishes_wishlist_position",
            indexesAfterDown);
        Assert.Contains(
            "wishes",
            tablesAfterUp);
        Assert.Contains(
            "wish_position_sequences",
            tablesAfterUp);
        Assert.Contains(
            "ux_wishes_wishlist_position",
            indexesAfterUp);
    }

    [Fact]
    public async Task MigrateAsync_WhenExistingWishIsMigrated_BackfillsQuantityWithoutKeepingDatabaseDefault()
    {
        // Arrange
        const string PreviousMigration = "20260826210949_AddWishlistParticipants";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(
            PreviousMigration,
            cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        context.Users.Add(new MonKadoUser
        {
            Id = ownerId,
            DisplayName = "Gift reservation migration",
            Email = "gift-reservation-migration@example.test",
            NormalizedEmail = "GIFT-RESERVATION-MIGRATION@EXAMPLE.TEST",
            UserName = "gift-reservation-migration@example.test",
            NormalizedUserName = "GIFT-RESERVATION-MIGRATION@EXAMPLE.TEST",
            EmailConfirmed = true
        });
        context.Wishlists.Add(new Wishlist(
            wishlistId,
            ownerId,
            "Migration wishlist",
            "MIGRATION WISHLIST",
            WishlistOccasion.Other,
            null,
            null));
        await context.SaveChangesAsync(cancellationToken);
        var createdAt = new DateTime(
            2026,
            8,
            30,
            8,
            0,
            0,
            DateTimeKind.Utc);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.wishes (
                id,
                wishlist_id,
                name,
                position,
                created_at)
            VALUES (
                {wishId},
                {wishlistId},
                {"Existing gift"},
                {1L},
                {createdAt});
            """,
            cancellationToken);
        bool quantityColumnExistsAfterDown;
        int quantityAfterFirstUp;
        int quantityAfterSecondUp;
        bool quantityHasDefault;

        try
        {
            // Act
            await context.Database.MigrateAsync(cancellationToken);
            context.ChangeTracker.Clear();
            quantityAfterFirstUp = await context.Wishes
                .AsNoTracking()
                .Where(wish => wish.Id == wishId)
                .Select(wish => wish.Quantity)
                .SingleAsync(cancellationToken);
            quantityHasDefault = await HasWishQuantityDefaultAsync(
                context,
                cancellationToken);
            await context.Database.MigrateAsync(
                PreviousMigration,
                cancellationToken);
            quantityColumnExistsAfterDown = await HasWishQuantityColumnAsync(
                context,
                cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
            context.ChangeTracker.Clear();
            quantityAfterSecondUp = await context.Wishes
                .AsNoTracking()
                .Where(wish => wish.Id == wishId)
                .Select(wish => wish.Quantity)
                .SingleAsync(cancellationToken);
        }
        finally
        {
            await context.Database.MigrateAsync(cancellationToken);
            await context.Users
                .Where(user => user.Id == ownerId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        // Assert
        Assert.Equal(
            1,
            quantityAfterFirstUp);
        Assert.False(quantityHasDefault);
        Assert.False(quantityColumnExistsAfterDown);
        Assert.Equal(
            1,
            quantityAfterSecondUp);
    }

    [Fact]
    public async Task MigrateAsync_WhenEmailChangeRowsExist_RollsBackWithoutViolatingOutboxConstraint()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        var suffix = Guid.CreateVersion7().ToString("N");
        var email = $"rollback-{suffix}@example.test";
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            DisplayName = "Email rollback test",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true
        };
        context.Users.Add(member);
        await context.SaveChangesAsync(cancellationToken);
        var now = new DateTime(
            2030,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        var request = MemberEmailChangeRequest.Create(
            member.Id,
            email,
            $"new-{email}",
            $"NEW-{email.ToUpperInvariant()}",
            now,
            now.AddHours(24));
        context.MemberEmailChangeRequests.Add(request);
        context.AuthenticationEmailOutboxMessages.AddRange(
            AuthenticationEmailOutboxMessage.CreateEmailConfirmation(
                member.Id,
                now),
            AuthenticationEmailOutboxMessage.CreateEmailChangeConfirmation(
                request.Id,
                member.Id,
                request.NewEmail,
                "security-stamp",
                now),
            AuthenticationEmailOutboxMessage.CreateEmailChangeSecurityNotification(
                request.Id,
                member.Id,
                request.CurrentEmail,
                now),
            AuthenticationEmailOutboxMessage.CreatePasswordChangedSecurityNotification(
                member.Id,
                email,
                now),
            AuthenticationEmailOutboxMessage.CreatePasswordReset(
                member.Id,
                email,
                "security-stamp",
                now));
        await context.SaveChangesAsync(cancellationToken);
        IReadOnlyList<string> remainingKinds;

        try
        {
            // Act
            await context.Database.MigrateAsync(
                "20260822180349_UseMemberXminVersion",
                cancellationToken);
            remainingKinds = await GetAuthenticationEmailOutboxKindsAsync(
                context,
                cancellationToken);
        }
        finally
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        // Assert
        Assert.Equal(
            ["EMAIL_CONFIRMATION"],
            remainingKinds);
    }

    [Fact]
    public async Task MigrateAsync_WhenPendingEmailChangePredatesGoogleMigration_BackfillsAndRemovesSecurityStampAcrossUpAndDown()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(
            "20260823172356_AddMemberPasswordResets",
            cancellationToken);
        var now = new DateTime(
            2030,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        var memberId = Guid.CreateVersion7(now);
        var securityStamp = "migration-security-stamp";
        var member = new MonKadoUser
        {
            Id = memberId,
            DisplayName = "Migration member",
            Email = "migration-email-change@example.test",
            NormalizedEmail = "MIGRATION-EMAIL-CHANGE@EXAMPLE.TEST",
            UserName = "migration-email-change@example.test",
            NormalizedUserName = "MIGRATION-EMAIL-CHANGE@EXAMPLE.TEST",
            EmailConfirmed = true,
            SecurityStamp = securityStamp
        };
        var request = MemberEmailChangeRequest.Create(
            memberId,
            "migration-email-change@example.test",
            "new-migration-email-change@example.test",
            "NEW-MIGRATION-EMAIL-CHANGE@EXAMPLE.TEST",
            now,
            now.AddHours(24));
        context.Users.Add(member);
        context.MemberEmailChangeRequests.Add(request);
        await context.SaveChangesAsync(cancellationToken);
        var messageId = Guid.CreateVersion7(now.AddMilliseconds(1));
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.authentication_email_outbox (
                id,
                user_id,
                member_email_change_request_id,
                recipient_email,
                kind,
                created_at,
                available_at,
                attempt_count)
            VALUES (
                {messageId},
                {memberId},
                {request.Id},
                {request.NewEmail},
                'EMAIL_CHANGE_CONFIRMATION',
                {now},
                {now},
                {0});
            """,
            cancellationToken);
        string? snapshotAfterUp;
        string? snapshotAfterDown;

        try
        {
            // Act
            await context.Database.MigrateAsync(cancellationToken);
            snapshotAfterUp = await context.AuthenticationEmailOutboxMessages
                .AsNoTracking()
                .Where(message => message.Id == messageId)
                .Select(message => message.SecurityStampSnapshot)
                .SingleAsync(cancellationToken);
            await context.Database.MigrateAsync(
                "20260823172356_AddMemberPasswordResets",
                cancellationToken);
            snapshotAfterDown = await context.AuthenticationEmailOutboxMessages
                .AsNoTracking()
                .Where(message => message.Id == messageId)
                .Select(message => message.SecurityStampSnapshot)
                .SingleAsync(cancellationToken);
        }
        finally
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        // Assert
        Assert.Equal(
            securityStamp,
            snapshotAfterUp);
        Assert.Null(snapshotAfterDown);
    }

    [Fact]
    public async Task MigrateAsync_WhenOpaqueSessionExists_DeletesSessionDuringRefreshMigration()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(
            "20260821115421_AddAuditableUtcDates",
            cancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var displayName = "Migration test";
        var email = $"migration-{suffix}@example.test";
        var normalizedEmail = $"MIGRATION-{suffix}@EXAMPLE.TEST";
        var protectedTicket = Convert.FromHexString("01");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.users (
                id,
                display_name,
                created_at,
                version,
                user_name,
                normalized_user_name,
                email,
                normalized_email,
                email_confirmed,
                phone_number_confirmed,
                two_factor_enabled,
                lockout_enabled,
                access_failed_count)
            VALUES (
                {userId},
                {displayName},
                {now},
                {1},
                {email},
                {normalizedEmail},
                {email},
                {normalizedEmail},
                {true},
                {false},
                {false},
                {true},
                {0});
            """,
            cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.authentication_sessions (
                id,
                user_id,
                protected_ticket,
                created_at,
                renewed_at,
                expires_at)
            VALUES (
                {sessionId},
                {userId},
                {protectedTicket},
                {now},
                {now},
                {now.AddHours(8)});
            """,
            cancellationToken);

        // Act
        await context.Database.MigrateAsync(cancellationToken);

        // Assert
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task MigrateAsync_WhenAuditableMemberHasNullUpdatedAt_BackfillsValueBeforeRollback()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(
            "20260821115421_AddAuditableUtcDates",
            cancellationToken);
        var userId = Guid.CreateVersion7();
        var createdAt = new DateTime(
            2026,
            8,
            21,
            12,
            0,
            0,
            DateTimeKind.Utc);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.users (
                id,
                display_name,
                created_at,
                version,
                user_name,
                normalized_user_name,
                email,
                normalized_email,
                email_confirmed,
                phone_number_confirmed,
                two_factor_enabled,
                lockout_enabled,
                access_failed_count)
            VALUES (
                {userId},
                {"Migration test"},
                {createdAt},
                {1},
                {"rollback@example.test"},
                {"ROLLBACK@EXAMPLE.TEST"},
                {"rollback@example.test"},
                {"ROLLBACK@EXAMPLE.TEST"},
                {true},
                {false},
                {false},
                {true},
                {0});
            """,
            cancellationToken);

        // Act
        DateTime updatedAt;

        try
        {
            await context.Database.MigrateAsync(
                "20260813171453_AddAuthenticationSessions",
                cancellationToken);
            updatedAt = await GetUserUpdatedAtAsync(
                context,
                userId,
                cancellationToken);
        }
        finally
        {
            await context.Database.MigrateAsync(cancellationToken);
        }
        var hasNoDefault = await HasNoUserUpdatedAtDefaultAsync(
            context,
            cancellationToken);

        // Assert
        Assert.Equal(
            createdAt,
            updatedAt);
        Assert.True(hasNoDefault);
    }

    [Fact]
    public async Task MigrateAsync_WhenExistingMember_BackfillsAndRemovesMemberRole()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(
            "20260821191432_ReplaceAuthenticationTicketsWithRefreshSessions",
            cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM public.users;",
            cancellationToken);
        var memberId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.users (
                id,
                display_name,
                created_at,
                version,
                user_name,
                normalized_user_name,
                email,
                normalized_email,
                email_confirmed,
                phone_number_confirmed,
                two_factor_enabled,
                lockout_enabled,
                access_failed_count)
            VALUES (
                {memberId},
                {"Existing member"},
                {now},
                {1},
                {"existing@example.test"},
                {"EXISTING@EXAMPLE.TEST"},
                {"existing@example.test"},
                {"EXISTING@EXAMPLE.TEST"},
                {true},
                {false},
                {false},
                {true},
                {0});
            """,
            cancellationToken);

        // Act
        await context.Database.MigrateAsync(cancellationToken);

        // Assert
        var role = await context.Roles
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == RoleIds.Member,
                cancellationToken);
        Assert.Equal(
            RoleNames.Member,
            role.Name);
        Assert.Equal(
            "MEMBER",
            role.NormalizedName);
        var assignment = await context.UserRoles
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(
            memberId,
            assignment.UserId);
        Assert.Equal(
            RoleIds.Member,
            assignment.RoleId);
        var member = await context.Users
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == memberId,
                cancellationToken);
        Assert.NotEqual(
            0u,
            member.Version);

        await context.Database.MigrateAsync(
            "20260821191432_ReplaceAuthenticationTicketsWithRefreshSessions",
            cancellationToken);
        Assert.Empty(await context.Roles
            .AsNoTracking()
            .Where(value => value.Id == RoleIds.Member)
            .ToArrayAsync(cancellationToken));
        Assert.Empty(await context.UserRoles
            .AsNoTracking()
            .ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task MigrateAsync_WhenGoogleLoginDataExists_PreservesDataAcrossUpAndDown()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(
            "20260823172356_AddMemberPasswordResets",
            cancellationToken);
        var memberId = Guid.CreateVersion7();
        var email = $"google-migration-{memberId:N}@example.test";
        var normalizedEmail = email.ToUpperInvariant();
        var createdAt = new DateTime(
            2026,
            8,
            23,
            12,
            0,
            0,
            DateTimeKind.Utc);
        var subject = new string(
            'S',
            128);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.users (
                id,
                display_name,
                created_at,
                user_name,
                normalized_user_name,
                email,
                normalized_email,
                email_confirmed,
                phone_number_confirmed,
                two_factor_enabled,
                lockout_enabled,
                access_failed_count)
            VALUES (
                {memberId},
                {"Google migration"},
                {createdAt},
                {email},
                {normalizedEmail},
                {email},
                {normalizedEmail},
                {true},
                {false},
                {false},
                {true},
                {0});
            """,
            cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.user_logins (
                login_provider,
                provider_key,
                provider_display_name,
                user_id)
            VALUES (
                {"Google"},
                {subject},
                {"Google"},
                {memberId});
            """,
            cancellationToken);

        try
        {
            // Act
            await context.Database.MigrateAsync(cancellationToken);
            var subjectAfterUp = await GetGoogleSubjectAsync(
                context,
                memberId,
                cancellationToken);
            await context.Database.MigrateAsync(
                "20260823172356_AddMemberPasswordResets",
                cancellationToken);
            var subjectAfterDown = await GetGoogleSubjectAsync(
                context,
                memberId,
                cancellationToken);

            // Assert
            Assert.Equal(
                subject,
                subjectAfterUp);
            Assert.Equal(
                subject,
                subjectAfterDown);
        }
        finally
        {
            await context.Database.MigrateAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task MigrateAsync_WhenProcessedAuthenticationEmailExists_PreservesDataAcrossIndexUpAndDown()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        const string previousMigration = "20260823220140_AddGoogleExternalLogins";
        await context.Database.MigrateAsync(
            previousMigration,
            cancellationToken);
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            DisplayName = "Retention migration test",
            Email = "retention-migration@example.test",
            NormalizedEmail = "RETENTION-MIGRATION@EXAMPLE.TEST",
            UserName = "retention-migration@example.test",
            NormalizedUserName = "RETENTION-MIGRATION@EXAMPLE.TEST",
            EmailConfirmed = true
        };
        var createdAt = new DateTime(
            2026,
            7,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        var message = AuthenticationEmailOutboxMessage.CreatePasswordChangedSecurityNotification(
            member.Id,
            member.Email,
            createdAt);
        context.Users.Add(member);
        context.AuthenticationEmailOutboxMessages.Add(message);
        await context.SaveChangesAsync(cancellationToken);
        await context.AuthenticationEmailOutboxMessages
            .Where(value => value.Id == message.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    value => value.ProcessedAt,
                    createdAt.AddMinutes(1)),
                cancellationToken);
        bool rowExistsAfterUp;
        bool rowExistsAfterDown;
        bool indexExistsAfterUp;
        bool indexExistsAfterDown;

        try
        {
            // Act
            await context.Database.MigrateAsync(cancellationToken);
            rowExistsAfterUp = await context.AuthenticationEmailOutboxMessages
                .AsNoTracking()
                .AnyAsync(
                    value => value.Id == message.Id,
                    cancellationToken);
            var indexesAfterUp = await GetPublicIndexesAsync(
                context,
                cancellationToken);
            indexExistsAfterUp = indexesAfterUp.Contains(
                "ix_authentication_email_outbox_processed_cleanup",
                StringComparer.Ordinal);
            await context.Database.MigrateAsync(
                previousMigration,
                cancellationToken);
            rowExistsAfterDown = await context.AuthenticationEmailOutboxMessages
                .AsNoTracking()
                .AnyAsync(
                    value => value.Id == message.Id,
                    cancellationToken);
            var indexesAfterDown = await GetPublicIndexesAsync(
                context,
                cancellationToken);
            indexExistsAfterDown = indexesAfterDown.Contains(
                "ix_authentication_email_outbox_processed_cleanup",
                StringComparer.Ordinal);
        }
        finally
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        // Assert
        Assert.True(rowExistsAfterUp);
        Assert.True(rowExistsAfterDown);
        Assert.True(indexExistsAfterUp);
        Assert.False(indexExistsAfterDown);
    }

    [Fact]
    public async Task MigrateAsync_WhenGoogleSubjectUsesMaximumLength_AcceptsSubjectAndRejectsSecondProviderLogin()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            DisplayName = "Google constraint test",
            Email = "google-constraint@example.test",
            NormalizedEmail = "GOOGLE-CONSTRAINT@EXAMPLE.TEST",
            UserName = "google-constraint@example.test",
            NormalizedUserName = "GOOGLE-CONSTRAINT@EXAMPLE.TEST",
            EmailConfirmed = true
        };
        context.Users.Add(member);
        await context.SaveChangesAsync(cancellationToken);
        context.UserLogins.Add(new Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>
        {
            LoginProvider = "Google",
            ProviderKey = new string(
                'S',
                255),
            ProviderDisplayName = "Google",
            UserId = member.Id
        });
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        context.UserLogins.Add(new Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>
        {
            LoginProvider = "Google",
            ProviderKey = "different-subject",
            ProviderDisplayName = "Google",
            UserId = member.Id
        });

        // Act
        Task action() => context.SaveChangesAsync(cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(action);
        context.ChangeTracker.Clear();
        Assert.Equal(
            255,
            await context.UserLogins
                .AsNoTracking()
                .Select(login => login.ProviderKey.Length)
                .SingleAsync(
                    loginLength => loginLength == 255,
                    cancellationToken));
        Task rollbackAction() => context.Database.MigrateAsync(
            "20260823172356_AddMemberPasswordResets",
            cancellationToken);
        var rollbackException = await Assert.ThrowsAsync<PostgresException>(rollbackAction);
        Assert.Contains(
            "provider_key contains values longer than 128 characters",
            rollbackException.MessageText,
            StringComparison.Ordinal);
        Assert.Contains(
            await context.Database.GetAppliedMigrationsAsync(cancellationToken),
            migration => migration.EndsWith(
                "_AddGoogleExternalLogins",
                StringComparison.Ordinal));
        Assert.Equal(
            255,
            await context.UserLogins
                .AsNoTracking()
                .Where(login => login.UserId == member.Id)
                .Select(login => login.ProviderKey.Length)
                .SingleAsync(cancellationToken));
        await context.Users
            .Where(user => user.Id == member.Id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> GetAuthenticationEmailOutboxColumnsAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'authentication_email_outbox'
            ORDER BY column_name;
            """;
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<string> GetGoogleSubjectAsync(
        MonKadoDbContext context,
        Guid memberId,
        CancellationToken cancellationToken)
    {

        return await context.UserLogins
            .AsNoTracking()
            .Where(login => login.UserId == memberId && login.LoginProvider == "Google")
            .Select(login => login.ProviderKey)
            .SingleAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> GetAuthenticationEmailOutboxKindsAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT kind
            FROM public.authentication_email_outbox
            ORDER BY kind;
            """;
        var kinds = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            kinds.Add(reader.GetString(0));
        }

        return kinds;
    }

    private static async Task<IReadOnlyList<string>> GetAuthenticationSessionColumnsAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'authentication_sessions'
            ORDER BY column_name;
            """;
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<IReadOnlyList<string>> GetMemberEmailChangeRequestColumnsAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'member_email_change_requests'
            ORDER BY column_name;
            """;
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private ServiceProvider CreateServiceProvider()
    {
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:PostgreSql"] = fixture.Container.GetConnectionString();

        var services = new ServiceCollection();
        services.ConfigureInfrastructureInjection(configuration);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<IReadOnlyList<string>> GetPublicTablesAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<IReadOnlyList<string>> GetPublicConstraintsAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT constraint_name
            FROM information_schema.table_constraints
            WHERE constraint_schema = 'public'
            ORDER BY constraint_name;
            """;

        var constraints = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            constraints.Add(reader.GetString(0));
        }

        return constraints;
    }

    private static async Task<IReadOnlyList<string>> GetPublicIndexesAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
            ORDER BY indexname;
            """;

        var indexes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            indexes.Add(reader.GetString(0));
        }

        return indexes;
    }

    private static async Task<bool> IsUserUpdatedAtNullableAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT is_nullable = 'YES'
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'users'
              AND column_name = 'updated_at';
            """;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The users.updated_at column is missing."));
    }

    private static async Task<DateTime> GetUserUpdatedAtAsync(
        MonKadoDbContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT updated_at
            FROM public.users
            WHERE id = @user_id;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "user_id";
        parameter.Value = userId;
        command.Parameters.Add(parameter);

        return (DateTime)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The migration test user is missing."));
    }

    private static async Task<bool> HasNoUserUpdatedAtDefaultAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_default IS NULL
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'users'
              AND column_name = 'updated_at';
            """;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The users.updated_at column is missing."));
    }

    private static async Task<bool> HasUserVersionColumnAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'users'
                  AND column_name = 'version');
            """;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The users table could not be inspected."));
    }

    private static async Task<bool> HasWishQuantityColumnAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'wishes'
                  AND column_name = 'quantity');
            """;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The wishes table could not be inspected."));
    }

    private static async Task<bool> HasWishQuantityDefaultAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_default IS NOT NULL
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'wishes'
              AND column_name = 'quantity';
            """;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The wishes.quantity column is missing."));
    }
}
