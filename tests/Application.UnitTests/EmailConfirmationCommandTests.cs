using FluentValidation;
using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests;

public sealed class EmailConfirmationCommandTests
{
    [Fact]
    public async Task ValidConfirmationPreservesTheTokenExactly()
    {
        RecordingEmailConfirmationService service = new() { ConfirmationResult = true };
        ConfirmEmailCommandHandler handler = new(service);
        string userId = Guid.CreateVersion7().ToString("D");
        const string Token = "AbCd_-0123";

        await handler.Handle(
            new ConfirmEmailCommand(userId, Token),
            TestContext.Current.CancellationToken);

        Assert.Equal(userId, service.UserId);
        Assert.Equal(Token, service.Token);
    }

    [Fact]
    public async Task FailedConfirmationThrowsTheGenericException()
    {
        RecordingEmailConfirmationService service = new();
        ConfirmEmailCommandHandler handler = new(service);

        await Assert.ThrowsAsync<EmailConfirmationInvalidException>(() => handler.Handle(
            new ConfirmEmailCommand(Guid.CreateVersion7().ToString("D"), "dG9rZW4"),
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null, "dG9rZW4")]
    [InlineData("", "dG9rZW4")]
    [InlineData("not-a-guid", "dG9rZW4")]
    [InlineData("00000000-0000-0000-0000-000000000000", "dG9rZW4")]
    [InlineData("019c0fd9-7c7f-7de0-b02a-d9a02abc2ab4", null)]
    [InlineData("019c0fd9-7c7f-7de0-b02a-d9a02abc2ab4", "invalid token")]
    public async Task InvalidConfirmationInputFailsValidation(string? userId, string? token)
    {
        ConfirmEmailCommandValidator validator = new();

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(
            new ConfirmEmailCommand(userId, token),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task GenericValidationFailureHidesFieldDetails()
    {
        ConfirmEmailCommand command = new("invalid", "invalid token");
        ValidationBehavior<ConfirmEmailCommand, Unit> behavior = new(
            [new ConfirmEmailCommandValidator()]);

        await Assert.ThrowsAsync<EmailConfirmationInvalidException>(() => behavior.Handle(
            command,
            _ => Task.FromResult(Unit.Value),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResendHandlerTrimsTheEmailAddress()
    {
        RecordingEmailConfirmationService service = new();
        RequestEmailConfirmationCommandHandler handler = new(service);

        await handler.Handle(
            new RequestEmailConfirmationCommand(" Lea@example.fr "),
            TestContext.Current.CancellationToken);

        Assert.Equal("Lea@example.fr", service.Email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("person@example.fr extra")]
    public async Task InvalidResendEmailFailsValidation(string? email)
    {
        RequestEmailConfirmationCommandValidator validator = new();

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(
            new RequestEmailConfirmationCommand(email),
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == "Email");
    }

    private sealed class RecordingEmailConfirmationService : IEmailConfirmationService
    {
        public bool ConfirmationResult { get; init; }

        public string? UserId { get; private set; }

        public string? Token { get; private set; }

        public string? Email { get; private set; }

        public Task<bool> ConfirmAsync(
            string userId,
            string token,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UserId = userId;
            Token = token;
            return Task.FromResult(ConfirmationResult);
        }

        public Task RequestAsync(string email, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Email = email;
            return Task.CompletedTask;
        }
    }
}
