using FluentValidation;
using FluentValidation.Results;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Common.Behaviors;

public class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_WhenNoValidatorExists_InvokesNextHandler()
    {
        // Arrange
        var command = new ConfirmEmailCommand(
            "user-id",
            "token");
        var behavior = new ValidationBehavior<ConfirmEmailCommand, Unit>([]);
        var invoked = false;

        Task<Unit> next(CancellationToken cancellationToken)
        {
            Assert.Equal(
                TestContext.Current.CancellationToken,
                cancellationToken);
            invoked = true;

            return Task.FromResult(Unit.Value);
        }

        // Act
        var result = await behavior.Handle(
            command,
            next,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            Unit.Value,
            result);
        Assert.True(invoked);
    }

    [Fact]
    public async Task Handle_WhenValidationSucceeds_InvokesNextHandler()
    {
        // Arrange
        var command = new ConfirmEmailCommand(
            Guid.NewGuid().ToString(),
            "token");
        var behavior = new ValidationBehavior<ConfirmEmailCommand, Unit>(
            [new ConfirmEmailCommandValidator()]);

        // Act
        var result = await behavior.Handle(
            command,
            _ => Task.FromResult(Unit.Value),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            Unit.Value,
            result);
    }

    [Fact]
    public async Task Handle_WhenDetailedValidationFails_ReturnsDistinctCamelCaseErrors()
    {
        // Arrange
        var command = new RequestEmailConfirmationCommand("user@example.com");
        var validator = new InlineValidator<RequestEmailConfirmationCommand>();
        validator
            .RuleFor(value => value.Email)
            .Must(_ => false)
            .WithMessage("Invalid token")
            .OverridePropertyName("Parent.Child");
        validator
            .RuleFor(value => value.Email)
            .Must(_ => false)
            .WithMessage("Invalid token")
            .OverridePropertyName("Parent.Child");
        validator
            .RuleFor(value => value.Email)
            .Must(_ => false)
            .WithMessage("Root error")
            .OverridePropertyName(string.Empty);
        validator
            .RuleFor(value => value.Email)
            .Must(_ => false)
            .WithMessage("Lowercase parent")
            .OverridePropertyName("parent.Child");
        var behavior = new ValidationBehavior<RequestEmailConfirmationCommand, Unit>([validator]);

        // Act
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            behavior.Handle(
                command,
                _ => Task.FromResult(Unit.Value),
                TestContext.Current.CancellationToken));

        // Assert
        var errors = exception.ValidationErrors.ToArray();
        Assert.Equal(
            3,
            errors.Length);
        Assert.Contains(
            new ValidationError(
                "parent.child",
                "Invalid token"),
            errors,
            ValidationErrorComparer.Instance);
        Assert.Contains(
            new ValidationError(
                string.Empty,
                "Root error"),
            errors,
            ValidationErrorComparer.Instance);
        Assert.Contains(
            new ValidationError(
                "parent.child",
                "Lowercase parent"),
            errors,
            ValidationErrorComparer.Instance);
    }

    [Fact]
    public async Task Handle_WhenGenericValidationFails_HidesFieldDetails()
    {
        // Arrange
        // Act
        var command = new ConfirmEmailCommand(
            "invalid",
            "invalid token");
        var behavior = new ValidationBehavior<ConfirmEmailCommand, Unit>(
            [new ConfirmEmailCommandValidator()]);

        Task<Unit> action()
        {
            return behavior.Handle(
            command,
            _ => Task.FromResult(Unit.Value),
            TestContext.Current.CancellationToken);
        }

        // Assert
        await Assert.ThrowsAsync<EmailConfirmationInvalidException>((Func<Task<Unit>>)action);
    }

    [Fact]
    public async Task Handle_WhenRefreshTokenValidationFails_ThrowsInvalidSession()
    {
        // Arrange
        var command = new RefreshSessionCommand(null);
        var behavior = new ValidationBehavior<RefreshSessionCommand, Unit>(
            [new RefreshSessionCommandValidator()]);

        Task<Unit> action()
        {
            return behavior.Handle(
                command,
                _ => Task.FromResult(Unit.Value),
                TestContext.Current.CancellationToken);
        }

        // Act
        var exception = await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(
            (Func<Task<Unit>>)action);

        // Assert
        Assert.Equal(
            "The authentication session is invalid or expired.",
            exception.Message);
    }

    [Fact]
    public async Task Handle_WhenCurrentSessionMemberIdIsEmpty_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var query = new GetCurrentSessionQuery(Guid.Empty);
        var behavior = new ValidationBehavior<GetCurrentSessionQuery, CurrentSession>(
            [new GetCurrentSessionQueryValidator()]);

        Task<CurrentSession> action()
        {
            return behavior.Handle(
                query,
                _ => throw new InvalidOperationException("The handler must not be invoked."),
                TestContext.Current.CancellationToken);
        }

        // Act
        var exception = await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(
            (Func<Task<CurrentSession>>)action);

        // Assert
        Assert.Equal(
            "The authentication session is invalid or expired.",
            exception.Message);
    }

    [Fact]
    public async Task Handle_WhenProfileMemberIdIsEmpty_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var command = new UpdateMemberProfileCommand(
            Guid.Empty,
            "Jenn",
            42);
        var behavior = new ValidationBehavior<UpdateMemberProfileCommand, MemberProfile>(
            [new UpdateMemberProfileCommandValidator()]);

        Task<MemberProfile> action()
        {
            return behavior.Handle(
                command,
                _ => throw new InvalidOperationException("The handler must not be invoked."),
                TestContext.Current.CancellationToken);
        }

        // Act
        var exception = await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(
            (Func<Task<MemberProfile>>)action);

        // Assert
        Assert.Equal(
            "The authentication session is invalid or expired.",
            exception.Message);
    }
}
