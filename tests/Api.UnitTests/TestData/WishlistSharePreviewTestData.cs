using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.TestData;

/// <summary>Creates public-preview metadata with explicit image-count and escaping scenarios.</summary>
public static class WishlistSharePreviewTestData
{
    /// <summary>Creates a preview whose title must be HTML encoded.</summary>
    /// <param name="imageCount">The number of public image references.</param>
    /// <returns>The complete preview model.</returns>
    public static WishlistSharePreview CreateWithHtmlTitle(int imageCount)
    {
        var fixture = TestFixture.Create();
        var images = fixture.CreateMany<WishlistSharePreviewImage>(imageCount)
            .ToArray();

        return fixture.Build<WishlistSharePreview>()
            .With(
                preview => preview.Name,
                "Noël <script> & \"cadeaux\"")
            .With(
                preview => preview.Images,
                images)
            .Create();
    }
}
