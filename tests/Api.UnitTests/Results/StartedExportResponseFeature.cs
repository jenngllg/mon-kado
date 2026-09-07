using Microsoft.AspNetCore.Http.Features;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Results;

internal class StartedExportResponseFeature(bool started) : HttpResponseFeature
{
    public override bool HasStarted => started;
}
