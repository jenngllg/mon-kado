using JennGllg.Fr.MonKado.Back.Domain.Enums;
using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>
/// Represents a wish.
/// </summary>
[ExcludeFromCodeCoverage]
public class Wish
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the comment.
    /// </summary>
    public string Comment { get; set; }

    /// <summary>
    /// Gets or sets the size.
    /// </summary>
    public string Size { get; set; }

    /// <summary>
    /// Gets or sets the price.
    /// </summary>
    public float Price { get; set; }

    /// <summary>
    /// Gets or sets the color.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the model.
    /// </summary>
    public string Model { get; set; }

    /// <summary>
    /// Gets or sets the URL of the picture.
    /// </summary>
    public string PictureUrl { get; set; }

    /// <summary>
    /// Gets or sets the URL used for the example resource.
    /// </summary>
    public string ExampleUrl { get; set; }

    /// <summary>
    /// Gets or sets the current status.
    /// </summary>
    public WishStatus Status { get; set; }
}
