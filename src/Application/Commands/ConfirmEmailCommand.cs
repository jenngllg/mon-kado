using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;
/// <summary>
/// Represents confirm email command.
/// </summary>
/// <param name="userId">The user id.</param>
/// <param name="token">The token.</param>

public class ConfirmEmailCommand(
    string? userId,
    string? token)
    : IRequest, IGenericValidationFailure
{
    /// <summary>
    /// Gets user id.
    /// </summary>
    public string? UserId { get; } = userId;
    /// <summary>
    /// Gets token.
    /// </summary>

    public string? Token { get; } = token;

    Exception IGenericValidationFailure.CreateValidationException()
    {

        return new EmailConfirmationInvalidException();
    }
}
