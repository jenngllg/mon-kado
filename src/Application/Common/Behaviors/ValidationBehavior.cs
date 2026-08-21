using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
/// <summary>
/// Represents validation behavior.
/// </summary>
/// <param name="validators">The validators.</param>

public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Executes the handle operation.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="next">The next.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!validators.Any())
            return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(
                context,
                cancellationToken)));

        var validationErrors = results
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .Select(failure => new ValidationError(
                ToCamelCasePath(failure.PropertyName),
                failure.ErrorMessage))
            .DistinctBy(
                error => new
                {
                    error.PropertyName,
                    error.ErrorMessage
                })
            .ToArray();

        if (validationErrors.Length != 0)
        {

            if (request is IGenericValidationFailure genericFailure)
                throw genericFailure.CreateValidationException();

            throw new RequestValidationException(validationErrors);
        }

        return await next(cancellationToken);
    }

    private static string ToCamelCasePath(string value)
    {

        return string.IsNullOrEmpty(value)
            ? value
            : string.Join(
                '.',
                value
                .Split('.')
                .Select(ToCamelCaseSegment));
    }

    internal static string ToCamelCaseSegment(string value)
    {

        return string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];
    }
}
