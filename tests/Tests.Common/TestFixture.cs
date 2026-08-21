using AutoFixture;

namespace JennGllg.Fr.MonKado.Back.Tests.Common;

public static class TestFixture
{
    public static Fixture Create()
    {
        var fixture = new Fixture();
        fixture.Register(Guid.CreateVersion7);

        return fixture;
    }
}
