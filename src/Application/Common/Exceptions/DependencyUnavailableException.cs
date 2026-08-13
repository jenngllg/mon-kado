namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

public sealed class DependencyUnavailableException(string dependencyName, Exception innerException)
    : Exception($"The {dependencyName} dependency is unavailable.", innerException)
{
}
