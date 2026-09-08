using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

/// <summary>
/// Represents mon kado db context.
/// </summary>
/// <param name="options">The options.</param>
public class MonKadoDbContext(DbContextOptions<MonKadoDbContext> options) : IdentityDbContext<MonKadoUser, IdentityRole<Guid>, Guid>(options), IUnitOfWork
{
    /// <summary>Gets durable personal-data export requests and cleanup identities.</summary>
    public DbSet<MemberDataExport> MemberDataExports => Set<MemberDataExport>();

    /// <summary>Gets retained administrative export accountability events.</summary>
    public DbSet<AdministrativeDataExportEvent> AdministrativeDataExportEvents => Set<AdministrativeDataExportEvent>();
    /// <summary>Gets the minimal administrative erasure audit.</summary>
    public DbSet<AdministrativeAccountErasureEvent> AdministrativeAccountErasureEvents => Set<AdministrativeAccountErasureEvent>();
    /// <summary>Gets the short-lived erasure notification outbox.</summary>
    public DbSet<AccountErasureEmail> AccountErasureEmails => Set<AccountErasureEmail>();
    /// <summary>
    /// Gets authentication sessions.
    /// </summary>
    public DbSet<AuthenticationSession> AuthenticationSessions => Set<AuthenticationSession>();
    /// <summary>Gets the identifiers of access tokens issued by committed sessions.</summary>
    public DbSet<AuthenticationAccessToken> AuthenticationAccessTokens => Set<AuthenticationAccessToken>();
    /// <summary>Gets retained administrative session-revocation events.</summary>
    public DbSet<AdministrativeSessionRevocationEvent> AdministrativeSessionRevocationEvents => Set<AdministrativeSessionRevocationEvent>();
    /// <summary>
    /// Gets authentication email outbox messages.
    /// </summary>
    public DbSet<AuthenticationEmailOutboxMessage> AuthenticationEmailOutboxMessages => Set<AuthenticationEmailOutboxMessage>();
    /// <summary>
    /// Gets obsolete gift-image deletion messages.
    /// </summary>
    public DbSet<GiftImageDeletionOutboxMessage> GiftImageDeletionOutboxMessages => Set<GiftImageDeletionOutboxMessage>();
    /// <summary>
    /// Gets member email change requests.
    /// </summary>
    public DbSet<MemberEmailChangeRequest> MemberEmailChangeRequests => Set<MemberEmailChangeRequest>();
    /// <summary>Gets pending account deletion confirmation requests.</summary>
    public DbSet<MemberAccountDeletionRequest> MemberAccountDeletionRequests => Set<MemberAccountDeletionRequest>();
    /// <summary>
    /// Gets private wishlists.
    /// </summary>
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    /// <summary>Gets private administrator moderation decisions.</summary>
    public DbSet<WishlistModerationEvent> WishlistModerationEvents => Set<WishlistModerationEvent>();
    /// <summary>Gets durable moderation notification deliveries.</summary>
    public DbSet<WishlistModerationEmail> WishlistModerationEmails => Set<WishlistModerationEmail>();
    /// <summary>
    /// Gets gift wishes.
    /// </summary>
    public DbSet<Wish> Wishes => Set<Wish>();
    /// <summary>
    /// Gets gift wish collection position sequences.
    /// </summary>
    public DbSet<WishPositionSequence> WishPositionSequences => Set<WishPositionSequence>();
    /// <summary>
    /// Gets active wishlist share links.
    /// </summary>
    public DbSet<WishlistShareLink> WishlistShareLinks => Set<WishlistShareLink>();
    /// <summary>
    /// Gets anonymous browser guest sessions.
    /// </summary>
    public DbSet<GuestSession> GuestSessions => Set<GuestSession>();
    /// <summary>
    /// Gets wishlist participants.
    /// </summary>
    public DbSet<WishlistParticipant> WishlistParticipants => Set<WishlistParticipant>();
    /// <summary>
    /// Gets gift reservations.
    /// </summary>
    public DbSet<GiftReservation> GiftReservations => Set<GiftReservation>();
    /// <summary>
    /// Gets durable member gift reservation histories.
    /// </summary>
    public DbSet<GiftReservationHistory> GiftReservationHistories => Set<GiftReservationHistory>();
    /// <summary>
    /// Gets anonymous wishlist reports.
    /// </summary>
    public DbSet<WishlistReport> WishlistReports => Set<WishlistReport>();
    /// <summary>Gets immutable private report review events.</summary>
    public DbSet<WishlistReportReviewEvent> WishlistReportReviewEvents => Set<WishlistReportReviewEvent>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("public");
        builder.HasPostgresExtension("unaccent");
        builder
            .Entity<IdentityRole<Guid>>()
            .ToTable("roles");
        builder
            .Entity<IdentityUserRole<Guid>>()
            .ToTable("user_roles");
        builder
            .Entity<IdentityUserClaim<Guid>>()
            .ToTable("user_claims");
        builder
            .Entity<IdentityUserLogin<Guid>>()
            .ToTable("user_logins");
        builder
            .Entity<IdentityUserToken<Guid>>()
            .ToTable("user_tokens");
        builder
            .Entity<IdentityRoleClaim<Guid>>()
            .ToTable("role_claims");
        builder.ApplyConfigurationsFromAssembly(typeof(MonKadoDbContext).Assembly);
        IdentityModelConfiguration.Configure(builder);
    }
}
