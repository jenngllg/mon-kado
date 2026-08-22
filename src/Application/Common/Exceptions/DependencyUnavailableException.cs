namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
/// <summary>
/// Represents dependency unavailable exception.
/// </summary>
/// <param name="dependencyName">The dependency name.</param>
/// <param name="innerException">The inner exception.</param>

public class DependencyUnavailableException(
    string dependencyName,
    Exception? innerException)
    : Exception(
        $"The {dependencyName} dependency is unavailable.",
        innerException)
{
}
