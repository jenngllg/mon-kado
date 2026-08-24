using JennGllg.Fr.MonKado.Back.Domain.Configurations;

using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Configurations;

public class DomainInjectionConfigurationTests
{
    [Fact]
    public void ConfigureDomainInjection_WhenCalled_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.ConfigureDomainInjection();

        // Assert
        Assert.Same(
            services,
            result);
    }
}
