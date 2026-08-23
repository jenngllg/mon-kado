using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class MemberEmailChangeTokenPurposeTests
{
    [Fact]
    public void Create_WhenCalled_BindsRequestAndNormalizedEmail()
    {
        // Arrange
        var requestId = Guid.Parse("0198d027-51c0-7000-8000-000000000004");

        // Act
        var purpose = MemberEmailChangeTokenPurpose.Create(
            requestId,
            "NEW@EXAMPLE.FR");

        // Assert
        Assert.Equal(
            "MonKado.EmailChange:0198d027-51c0-7000-8000-000000000004:NEW@EXAMPLE.FR",
            purpose);
    }
}
