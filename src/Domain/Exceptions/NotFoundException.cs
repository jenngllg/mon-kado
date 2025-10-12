using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Domain.Exceptions;

/// <summary>
/// Represents an exception that is thrown when a requested resource is not found.
/// </summary>
[ExcludeFromCodeCoverage]
public class NotFoundException
    : Exception
{
    public NotFoundException() { }

    public NotFoundException(string message)
        : base(message) { }

    public NotFoundException(string message,
        Exception innerException)
        : base(message, innerException) { }
}