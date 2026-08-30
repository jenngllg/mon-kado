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

public class MonKadoDbContext(DbContextOptions<MonKadoDbContext> options)
    : IdentityDbContext<MonKadoUser, IdentityRole<Guid>, Guid>(options), IUnitOfWork
{
    /// <summary>
    /// Gets authentication sessions.
    /// </summary>

    public DbSet<AuthenticationSession> AuthenticationSessions =>
        Set<AuthenticationSession>();
    /// <summary>
    /// Gets authentication email outbox messages.
    /// </summary>

    public DbSet<AuthenticationEmailOutboxMessage> AuthenticationEmailOutboxMessages =>
        Set<AuthenticationEmailOutboxMessage>();
    /// <summary>
    /// Gets member email change requests.
    /// </summary>

    public DbSet<MemberEmailChangeRequest> MemberEmailChangeRequests =>
        Set<MemberEmailChangeRequest>();

    /// <summary>
    /// Gets private wishlists.
    /// </summary>
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();

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

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("public");

        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        builder.ApplyConfigurationsFromAssembly(typeof(MonKadoDbContext).Assembly);

        IdentityModelConfiguration.Configure(builder);
    }
}
