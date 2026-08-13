using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed record ConfirmEmailCommand(string? UserId, string? Token)
    : IRequest, IGenericValidationFailure
{
    Exception IGenericValidationFailure.CreateValidationException() =>
        new EmailConfirmationInvalidException();
}
