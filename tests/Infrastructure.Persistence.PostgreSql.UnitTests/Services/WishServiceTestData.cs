namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class WishServiceTestData(
    Guid id,
    Guid ownerId,
    Guid wishlistId,
    string name,
    string? note,
    string? url,
    decimal? price,
    CancellationToken cancellationToken)
{
    public Guid Id { get; } = id;

    public Guid OwnerId { get; } = ownerId;

    public Guid WishlistId { get; } = wishlistId;

    public string Name { get; } = name;

    public string? Note { get; } = note;

    public string? Url { get; } = url;

    public decimal? Price { get; } = price;

    public CancellationToken CancellationToken { get; } = cancellationToken;
}
