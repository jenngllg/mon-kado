using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Tests.Common;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests;

public static class WishImportTestData
{
    public static CreateWishImportPreviewCommand CreateValidCommand()
    {
        var fixture = TestFixture.Create();

        return new CreateWishImportPreviewCommand(
            fixture.Create<Guid>(),
            fixture.Create<Guid>(),
            "https://example.com");
    }
}
