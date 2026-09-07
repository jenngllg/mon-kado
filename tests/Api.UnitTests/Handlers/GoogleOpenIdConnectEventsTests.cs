using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Handlers;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using MediatR;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

using Moq;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Handlers;

public class GoogleOpenIdConnectEventsTests
{
    private const string ClientId = "client.apps.googleusercontent.com";
    private static readonly DateTimeOffset _now = new(
        2030,
        1,
        1,
        10,
        0,
        0,
        TimeSpan.Zero);
    private readonly Mock<ISender> _senderMock;
    private readonly GoogleOpenIdConnectEvents _events;

    public GoogleOpenIdConnectEventsTests()
    {
        _senderMock = new Mock<ISender>(MockBehavior.Strict);
        var returnPathService = new GoogleReturnPathService(
            Microsoft.Extensions.Options.Options.Create(new GoogleAuthenticationOptions
            {
                Enabled = true,
                ClientId = ClientId,
                FrontendOrigin = "https://app.example.test",
                DefaultReturnPath = "/login/google-return",
                AllowedReturnPaths =
                ["/login/google-return"]
            }),
            new GoogleReturnPathValidator());
        var externalAuthenticationService = new GoogleExternalAuthenticationService(
            new FixedTimeProvider(_now),
            returnPathService);
        _events = new GoogleOpenIdConnectEvents(
            NullLogger<GoogleOpenIdConnectEvents>.Instance,
            returnPathService,
            externalAuthenticationService,
            Microsoft.Extensions.Options.Options.Create(new GoogleAuthenticationOptions
            {
                Enabled = true,
                ClientId = ClientId
            }),
            new FixedTimeProvider(_now),
            _senderMock.Object);
    }

    [Theory]
    [InlineData("missingProperties", "failed")]
    [InlineData("missingDeadline", "failed")]
    [InlineData("expired", "failed")]
    [InlineData("valid", "cancelled")]
    public async Task AccessDenied_WhenProtectedDeadlineIsChecked_ReturnsSafeError(
        string scenario,
        string expectedError)
    {
        // Arrange
        var context = new AccessDeniedContext(
            new DefaultHttpContext(),
            CreateScheme(),
            new OpenIdConnectOptions())
        {
            Properties = scenario == "missingProperties" ? null : new AuthenticationProperties
            {
                ExpiresUtc = scenario switch
                {
                    "missingDeadline" => null,
                    "expired" => _now,
                    _ => _now.AddSeconds(1)
                }
            }
        };

        // Act
        await _events.AccessDenied(context);

        // Assert
        Assert.Equal(
            $"https://app.example.test/login/google-return#error={expectedError}",
            context.Response.Headers.Location);
        Assert.Equal(
            "no-store",
            context.Response.Headers.CacheControl);
        Assert.Equal(
            "no-referrer",
            context.Response.Headers["Referrer-Policy"]);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
        _senderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task TicketReceived_WhenDeadlineIsMissingOrExpired_DoesNotResolveMember(int? remainingSeconds)
    {
        // Arrange
        var context = CreateTicketReceivedContext(CreateValidPrincipal());
        Assert.NotNull(context.Properties);
        context.Properties.ExpiresUtc = remainingSeconds.HasValue ? _now.AddSeconds(remainingSeconds.Value) : null;

        // Act
        await _events.TicketReceived(context);

        // Assert
        Assert.Equal(
            "https://app.example.test/login/google-return#error=failed",
            context.Response.Headers.Location);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TicketReceived_WhenDeadlineExpiresDuringMemberResolution_DoesNotIssueProof()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var context = CreateTicketReceivedContext(CreateValidPrincipal());
        context.HttpContext.RequestAborted = cancellationToken;
        var clockMock = new Mock<TimeProvider>(MockBehavior.Strict);
        clockMock
            .SetupSequence(clock => clock.GetUtcNow())
            .Returns(_now)
            .Returns(_now.AddMinutes(5));
        var returnPathMock = new Mock<JennGllg.Fr.MonKado.Back.Api.Abstractions.IGoogleReturnPathService>(MockBehavior.Strict);
        returnPathMock
            .Setup(service => service.BuildAbsoluteUri(GoogleAuthenticationConstants.AuthenticationFailurePath))
            .Returns("https://app.example.test/login/google-return#error=failed");
        var externalMock = new Mock<JennGllg.Fr.MonKado.Back.Api.Abstractions.IGoogleExternalAuthenticationService>(MockBehavior.Strict);
        var events = new GoogleOpenIdConnectEvents(
            NullLogger<GoogleOpenIdConnectEvents>.Instance,
            returnPathMock.Object,
            externalMock.Object,
            Microsoft.Extensions.Options.Options.Create(new GoogleAuthenticationOptions()),
            clockMock.Object,
            _senderMock.Object);
        _senderMock
            .Setup(sender => sender.Send(
                It.IsAny<ResolveGoogleExpectedMemberCommand>(),
                cancellationToken))
            .ReturnsAsync((Guid?)null);

        // Act
        await events.TicketReceived(context);

        // Assert
        Assert.Equal(
            "https://app.example.test/login/google-return#error=failed",
            context.Response.Headers.Location);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
        _senderMock.Verify(
            sender => sender.Send(
                It.IsAny<ResolveGoogleExpectedMemberCommand>(),
                cancellationToken),
            Times.Once);
        clockMock.Verify(
            clock => clock.GetUtcNow(),
            Times.Exactly(2));
        returnPathMock.Verify(
            service => service.BuildAbsoluteUri(GoogleAuthenticationConstants.AuthenticationFailurePath),
            Times.Once);
        _senderMock.VerifyNoOtherCalls();
        clockMock.VerifyNoOtherCalls();
        returnPathMock.VerifyNoOtherCalls();
        externalMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthorizationCodeReceived_WhenProtectedPropertiesAreMissing_RejectsFlow()
    {
        // Arrange
        var context = new AuthorizationCodeReceivedContext(
            new DefaultHttpContext(),
            CreateScheme(),
            new OpenIdConnectOptions(),
            new AuthenticationProperties());
        context.Properties = null!;

        // Act
        await _events.AuthorizationCodeReceived(context);

        // Assert
        Assert.Equal(
            "https://app.example.test/login/google-return#error=failed",
            context.Response.Headers.Location);
        Assert.Equal(
            "no-store",
            context.Response.Headers.CacheControl);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RedirectToIdentityProvider_WhenChallengeStarts_ForcesAccountSelection()
    {
        // Arrange
        var context = new RedirectContext(
            new DefaultHttpContext(),
            CreateScheme(),
            new OpenIdConnectOptions(),
            new AuthenticationProperties());
        context.ProtocolMessage = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectMessage();

        // Act
        await _events.RedirectToIdentityProvider(context);

        // Assert
        Assert.Equal(
            "select_account",
            context.ProtocolMessage.Prompt);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TokenValidated_WhenSignedClaimsAreValid_ReducesPrincipalWithoutResolvingMember()
    {
        // Arrange
        var context = CreateTokenValidatedContext(CreateValidPrincipal());

        // Act
        await _events.TokenValidated(context);

        // Assert
        Assert.Null(context.Result?.Failure);
        var properties = Assert.IsType<AuthenticationProperties>(context.Properties);
        var principal = Assert.IsType<ClaimsPrincipal>(context.Principal);
        Assert.False(properties.Items.ContainsKey(
            GoogleAuthenticationConstants.ExpectedMemberIdProperty));
        Assert.Equal(
            "no-store",
            context.Response.Headers.CacheControl);
        Assert.Equal(
            [
                "email",
                "email_verified",
                "hd",
                "name",
                JwtRegisteredClaimNames.Sub
            ],
            principal.Claims
                .Select(claim => claim.Type)
                .OrderBy(type => type)
                .ToArray());
        Assert.DoesNotContain(
            principal.Claims,
            claim => claim.Type is "aud" or "azp" or "nonce");
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TicketReceived_WhenExpectedMemberExists_ProtectsResolvedSnapshot()
    {
        // Arrange
        var expectedMemberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _senderMock
            .Setup(sender => sender.Send(
                It.Is<ResolveGoogleExpectedMemberCommand>(command =>
                    command.Identity != null &&
                    command.Identity.Subject == "google-subject" &&
                    command.Identity.Email == "member@example.test"),
                cancellationToken))
            .ReturnsAsync(expectedMemberId);
        var context = CreateTicketReceivedContext(CreateValidPrincipal());
        context.HttpContext.RequestAborted = cancellationToken;

        // Act
        await _events.TicketReceived(context);

        // Assert
        var properties = Assert.IsType<AuthenticationProperties>(context.Properties);
        Assert.Equal(
            expectedMemberId.ToString("D"),
            properties.Items[GoogleAuthenticationConstants.ExpectedMemberIdProperty]);
        var flowBinding = Assert.IsType<string>(
            properties.Items[GoogleAuthenticationConstants.FlowBindingProperty]);
        Assert.Equal(
            43,
            flowBinding.Length);
        Assert.Equal(
            $"https://app.example.test{GoogleAuthenticationConstants.FrontendReturnPath}#flow={flowBinding}",
            properties.RedirectUri);
        Assert.Equal(
            properties.RedirectUri,
            context.ReturnUri);
        Assert.Equal(
            "no-store",
            context.Response.Headers.CacheControl);
        _senderMock.Verify(sender => sender.Send(
            It.IsAny<ResolveGoogleExpectedMemberCommand>(),
            cancellationToken),
            Times.Once);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TicketReceived_WhenNoMemberExists_ProtectsExplicitNoneSnapshot()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _senderMock
            .Setup(sender => sender.Send(
                It.IsAny<ResolveGoogleExpectedMemberCommand>(),
                cancellationToken))
            .ReturnsAsync((Guid?)null);
        var context = CreateTicketReceivedContext(CreateValidPrincipal());
        context.HttpContext.RequestAborted = cancellationToken;

        // Act
        await _events.TicketReceived(context);

        // Assert
        var properties = Assert.IsType<AuthenticationProperties>(context.Properties);
        Assert.Equal(
            GoogleAuthenticationConstants.NoExpectedMemberValue,
            properties.Items[GoogleAuthenticationConstants.ExpectedMemberIdProperty]);
        _senderMock.Verify(sender => sender.Send(
            It.IsAny<ResolveGoogleExpectedMemberCommand>(),
            cancellationToken),
            Times.Once);
        _senderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("emptyPrincipal")]
    [InlineData("missingSubject")]
    [InlineData("duplicateSubject")]
    [InlineData("missingEmail")]
    [InlineData("duplicateEmail")]
    [InlineData("missingEmailVerified")]
    [InlineData("duplicateEmailVerified")]
    [InlineData("invalidEmailVerified")]
    [InlineData("duplicateHostedDomain")]
    [InlineData("duplicateDisplayName")]
    public async Task TicketReceived_WhenReducedIdentityIsInvalid_PreservesConcurrentCookieAndRedirectsSafely(
        string scenario)
    {
        // Arrange
        var authenticationServiceMock = new Mock<IAuthenticationService>(MockBehavior.Strict);
        var principal = scenario == "emptyPrincipal"
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : CreateValidPrincipal();

        if (scenario != "emptyPrincipal")
            ApplyInvalidClaimScenario(
                principal,
                scenario);

        var context = CreateTicketReceivedContext(principal);
        ConfigureAuthenticationService(
            context.HttpContext,
            authenticationServiceMock);

        // Act
        await _events.TicketReceived(context);

        // Assert
        Assert.Equal(
            "https://app.example.test/login/google-return#error=failed",
            context.Response.Headers.Location);
        Assert.Equal(
            "no-store",
            context.Response.Headers.CacheControl);
        authenticationServiceMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TicketReceived_WhenExpectedMemberResolutionFails_PreservesConcurrentCookieAndRedirectsSafely()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var authenticationServiceMock = new Mock<IAuthenticationService>(MockBehavior.Strict);
        _senderMock
            .Setup(sender => sender.Send(
                It.IsAny<ResolveGoogleExpectedMemberCommand>(),
                cancellationToken))
            .ThrowsAsync(new GoogleAuthenticationFailedException());
        var context = CreateTicketReceivedContext(CreateValidPrincipal());
        context.HttpContext.RequestAborted = cancellationToken;
        ConfigureAuthenticationService(
            context.HttpContext,
            authenticationServiceMock);

        // Act
        await _events.TicketReceived(context);

        // Assert
        Assert.Equal(
            "https://app.example.test/login/google-return#error=failed",
            context.Response.Headers.Location);
        authenticationServiceMock.VerifyNoOtherCalls();
        _senderMock.Verify(sender => sender.Send(
            It.IsAny<ResolveGoogleExpectedMemberCommand>(),
            cancellationToken),
            Times.Once);
        _senderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("email_verified", "false")]
    [InlineData("azp", "another-client")]
    [InlineData("aud", "another-client")]
    [InlineData("sub", "invalid subject")]
    [InlineData("sub", "é")]
    [InlineData("sub", "")]
    [InlineData("email", "   ")]
    [InlineData("email_verified", "not-a-boolean")]
    [InlineData(JwtRegisteredClaimNames.Iat, "not-a-number")]
    [InlineData(JwtRegisteredClaimNames.Iat, "9223372036854775807")]
    public async Task TokenValidated_WhenRequiredClaimIsInvalid_FailsBeforePersistence(
        string claimType,
        string claimValue)
    {
        // Arrange
        var claims = CreateValidPrincipal().Claims
            .Where(claim => claim.Type != claimType)
            .Append(new Claim(
                claimType,
                claimValue));
        var context = CreateTokenValidatedContext(new ClaimsPrincipal(new ClaimsIdentity(claims)));

        // Act
        await _events.TokenValidated(context);

        // Assert
        Assert.NotNull(context.Result?.Failure);
        _senderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("missingSubject")]
    [InlineData("duplicateSubject")]
    [InlineData("missingEmail")]
    [InlineData("duplicateEmail")]
    [InlineData("missingEmailVerified")]
    [InlineData("duplicateEmailVerified")]
    [InlineData("missingIssuedAt")]
    [InlineData("duplicateIssuedAt")]
    [InlineData("duplicateHostedDomain")]
    [InlineData("duplicateDisplayName")]
    [InlineData("duplicateAuthorizedParty")]
    [InlineData("missingAudience")]
    [InlineData("longSubject")]
    public async Task TokenValidated_WhenClaimCardinalityIsInvalid_FailsBeforePersistence(
        string scenario)
    {
        // Arrange
        var principal = CreateValidPrincipal();
        ApplyInvalidClaimScenario(
            principal,
            scenario);
        var context = CreateTokenValidatedContext(principal);

        // Act
        await _events.TokenValidated(context);

        // Assert
        Assert.NotNull(context.Result?.Failure);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TokenValidated_WhenSecurityTokenIsMissing_FailsBeforePersistence()
    {
        // Arrange
        var context = CreateTokenValidatedContext(CreateValidPrincipal());
        context.SecurityToken = null!;

        // Act
        await _events.TokenValidated(context);

        // Assert
        Assert.NotNull(context.Result?.Failure);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TokenValidated_WhenPrincipalIsMissing_FailsBeforePersistence()
    {
        // Arrange
        var context = CreateTokenValidatedContext(CreateValidPrincipal());
        context.Principal = null!;

        // Act
        await _events.TokenValidated(context);

        // Assert
        Assert.NotNull(context.Result?.Failure);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TokenValidated_WhenOptionalClaimsAreMissing_AcceptsMinimalClaims()
    {
        // Arrange
        var principal = CreateValidPrincipal();
        var identity = Assert.IsType<ClaimsIdentity>(principal.Identity);
        RemoveClaim(
            identity,
            "hd");
        RemoveClaim(
            identity,
            "name");
        RemoveClaim(
            identity,
            "azp");
        var context = CreateTokenValidatedContext(principal);

        // Act
        await _events.TokenValidated(context);

        // Assert
        Assert.Null(context.Result?.Failure);
        Assert.DoesNotContain(
            context.Principal?.Claims ?? [],
            claim => claim.Type is "hd" or "name" or "azp");
        _senderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(SecurityAlgorithms.None)]
    [InlineData(SecurityAlgorithms.HmacSha256)]
    public async Task TokenValidated_WhenTokenIsNotValidatedRs256_FailsBeforePersistence(
        string algorithm)
    {
        // Arrange
        var context = CreateTokenValidatedContext(
            CreateValidPrincipal(),
            CreateSecurityToken(algorithm));

        // Act
        await _events.TokenValidated(context);

        // Assert
        Assert.NotNull(context.Result?.Failure);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TokenValidated_WhenRs256SignatureIsMissing_FailsBeforePersistence()
    {
        // Arrange
        var context = CreateTokenValidatedContext(
            CreateValidPrincipal(),
            CreateSecurityToken(
                SecurityAlgorithms.RsaSha256,
                string.Empty));

        // Act
        await _events.TokenValidated(context);

        // Assert
        Assert.NotNull(context.Result?.Failure);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TokenValidated_WhenMultipleAudiencesHaveNoAuthorizedParty_FailsBeforePersistence()
    {
        // Arrange
        var claims = CreateValidPrincipal().Claims
            .Where(claim => claim.Type != "azp")
            .Append(new Claim(
                JwtRegisteredClaimNames.Aud,
                "another-client"));
        var context = CreateTokenValidatedContext(new ClaimsPrincipal(new ClaimsIdentity(claims)));

        // Act
        await _events.TokenValidated(context);

        // Assert
        Assert.NotNull(context.Result?.Failure);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TokenValidated_WhenMultipleAudiencesHaveMatchingAuthorizedParty_AcceptsClaims()
    {
        // Arrange
        var claims = CreateValidPrincipal().Claims
            .Append(new Claim(
                JwtRegisteredClaimNames.Aud,
                "another-client"));
        var context = CreateTokenValidatedContext(new ClaimsPrincipal(new ClaimsIdentity(claims)));

        // Act
        await _events.TokenValidated(context);

        // Assert
        Assert.Null(context.Result?.Failure);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TokenValidated_WhenDisplayNameIsBlank_NormalizesItAsMissing()
    {
        // Arrange
        var claims = CreateValidPrincipal().Claims
            .Where(claim => claim.Type != "name")
            .Append(new Claim(
                "name",
                "   "));
        var context = CreateTokenValidatedContext(new ClaimsPrincipal(new ClaimsIdentity(claims)));

        // Act
        await _events.TokenValidated(context);

        // Assert
        var principal = Assert.IsType<ClaimsPrincipal>(context.Principal);
        Assert.DoesNotContain(
            principal.Claims,
            claim => claim.Type == "name");
        Assert.Null(context.Result?.Failure);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TokenValidated_WhenDisplayNameContainsInvalidUtf16_NormalizesItAsMissing()
    {
        // Arrange
        var invalidDisplayName = new string(
            (char)0xD800,
            1);
        var claims = CreateValidPrincipal().Claims
            .Where(claim => claim.Type != "name")
            .Append(new Claim(
                "name",
                invalidDisplayName));
        var context = CreateTokenValidatedContext(new ClaimsPrincipal(new ClaimsIdentity(claims)));

        // Act
        await _events.TokenValidated(context);

        // Assert
        var principal = Assert.IsType<ClaimsPrincipal>(context.Principal);
        Assert.DoesNotContain(
            principal.Claims,
            claim => claim.Type == "name");
        Assert.Null(context.Result?.Failure);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TokenValidated_WhenAuthorizedPartyIsDuplicated_FailsBeforePersistence()
    {
        // Arrange
        var principal = CreateValidPrincipal();
        principal.Identities.Single().AddClaim(new Claim(
            "azp",
            ClientId));
        var context = CreateTokenValidatedContext(principal);

        // Act
        await _events.TokenValidated(context);

        // Assert
        Assert.NotNull(context.Result?.Failure);
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TicketReceived_WhenExpectedMemberResolutionIsUnavailable_FailsSafely()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var authenticationServiceMock = new Mock<IAuthenticationService>(MockBehavior.Strict);
        _senderMock
            .Setup(sender => sender.Send(
                It.IsAny<ResolveGoogleExpectedMemberCommand>(),
                cancellationToken))
            .ThrowsAsync(new DependencyUnavailableException(
                "PostgreSQL",
                null));
        var context = CreateTicketReceivedContext(CreateValidPrincipal());
        context.HttpContext.RequestAborted = cancellationToken;
        ConfigureAuthenticationService(
            context.HttpContext,
            authenticationServiceMock);

        // Act
        await _events.TicketReceived(context);

        // Assert
        var properties = Assert.IsType<AuthenticationProperties>(context.Properties);
        Assert.False(properties.Items.ContainsKey(
            GoogleAuthenticationConstants.ExpectedMemberIdProperty));
        Assert.Equal(
            "https://app.example.test/login/google-return#error=unavailable",
            context.Response.Headers.Location);
        Assert.Equal(
            "no-store",
            context.Response.Headers.CacheControl);
        _senderMock.Verify(sender => sender.Send(
            It.IsAny<ResolveGoogleExpectedMemberCommand>(),
            cancellationToken),
            Times.Once);
        authenticationServiceMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoteFailure_WhenProtocolFails_RedirectsWithoutSensitiveDetails()
    {
        // Arrange
        var authenticationServiceMock = new Mock<IAuthenticationService>(MockBehavior.Strict);
        var httpContext = CreateRemoteFailureHttpContext(authenticationServiceMock);
        var exception = new InvalidOperationException("sensitive-provider-description");
        var context = new RemoteFailureContext(
            httpContext,
            CreateScheme(),
            new OpenIdConnectOptions(),
            exception);

        // Act
        await _events.RemoteFailure(context);

        // Assert
        Assert.Equal(
            StatusCodes.Status302Found,
            httpContext.Response.StatusCode);
        Assert.Equal(
            "https://app.example.test/login/google-return#error=failed",
            httpContext.Response.Headers.Location);
        Assert.DoesNotContain(
            "sensitive",
            httpContext.Response.Headers.Location.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "no-store",
            httpContext.Response.Headers.CacheControl);
        authenticationServiceMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoteFailure_WhenFailureIsMissing_RedirectsToGenericFailure()
    {
        // Arrange
        var authenticationServiceMock = new Mock<IAuthenticationService>(MockBehavior.Strict);
        var httpContext = CreateRemoteFailureHttpContext(authenticationServiceMock);
        var context = new RemoteFailureContext(
            httpContext,
            CreateScheme(),
            new OpenIdConnectOptions(),
            null!);

        // Act
        await _events.RemoteFailure(context);

        // Assert
        Assert.Equal(
            "https://app.example.test/login/google-return#error=failed",
            httpContext.Response.Headers.Location);
        Assert.Equal(
            "no-store",
            httpContext.Response.Headers.CacheControl);
        authenticationServiceMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoteFailure_WhenSnapshotDependencyIsUnavailable_RedirectsToRetryableError()
    {
        // Arrange
        var authenticationServiceMock = new Mock<IAuthenticationService>(MockBehavior.Strict);
        var httpContext = CreateRemoteFailureHttpContext(authenticationServiceMock);
        var exception = new AuthenticationFailureException(
            "wrapped",
            new DependencyUnavailableException(
                "PostgreSQL",
                null));
        var context = new RemoteFailureContext(
            httpContext,
            CreateScheme(),
            new OpenIdConnectOptions(),
            exception);

        // Act
        await _events.RemoteFailure(context);

        // Assert
        Assert.Equal(
            "https://app.example.test/login/google-return#error=unavailable",
            httpContext.Response.Headers.Location);
        Assert.Equal(
            "no-store",
            httpContext.Response.Headers.CacheControl);
        authenticationServiceMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("http")]
    [InlineData("canceled")]
    [InlineData("timeout")]
    public async Task RemoteFailure_WhenProviderTransportFails_RedirectsToRetryableError(
        string scenario)
    {
        // Arrange
        var authenticationServiceMock = new Mock<IAuthenticationService>(MockBehavior.Strict);
        var httpContext = CreateRemoteFailureHttpContext(authenticationServiceMock);
        var transportException = CreateTransportException(scenario);
        var context = new RemoteFailureContext(
            httpContext,
            CreateScheme(),
            new OpenIdConnectOptions(),
            new AuthenticationFailureException(
                "wrapped",
                transportException));

        // Act
        await _events.RemoteFailure(context);

        // Assert
        Assert.Equal(
            "https://app.example.test/login/google-return#error=unavailable",
            httpContext.Response.Headers.Location);
        authenticationServiceMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoteFailure_WhenRequestWasAborted_DoesNotClassifyTransportAsProviderOutage()
    {
        // Arrange
        var authenticationServiceMock = new Mock<IAuthenticationService>(MockBehavior.Strict);
        var httpContext = CreateRemoteFailureHttpContext(authenticationServiceMock);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        httpContext.RequestAborted = cancellationTokenSource.Token;
        var context = new RemoteFailureContext(
            httpContext,
            CreateScheme(),
            new OpenIdConnectOptions(),
            new HttpRequestException("aborted request"));

        // Act
        await _events.RemoteFailure(context);

        // Assert
        Assert.Equal(
            "https://app.example.test/login/google-return#error=failed",
            httpContext.Response.Headers.Location);
        authenticationServiceMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }

    private static ClaimsPrincipal CreateValidPrincipal()
    {

        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(
                JwtRegisteredClaimNames.Sub,
                "google-subject"),
            new Claim(
                "email",
                "member@example.test"),
            new Claim(
                "email_verified",
                "true"),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                _now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(
                "hd",
                "example.test"),
            new Claim(
                "name",
                "Member Name"),
            new Claim(
                "azp",
                ClientId),
            new Claim(
                "aud",
                ClientId),
            new Claim(
                "nonce",
                "nonce")
        ]));
    }

    private static void ApplyInvalidClaimScenario(
        ClaimsPrincipal principal,
        string scenario)
    {
        var identity = Assert.IsType<ClaimsIdentity>(principal.Identity);

        switch (scenario)
        {
            case "missingSubject":
                RemoveClaim(
                    identity,
                    JwtRegisteredClaimNames.Sub);
                break;
            case "duplicateSubject":
                identity.AddClaim(new Claim(
                    JwtRegisteredClaimNames.Sub,
                    "second-subject"));
                break;
            case "longSubject":
                RemoveClaim(
                    identity,
                    JwtRegisteredClaimNames.Sub);
                identity.AddClaim(new Claim(
                    JwtRegisteredClaimNames.Sub,
                    new string(
                        'a',
                        256)));
                break;
            case "missingEmail":
                RemoveClaim(
                    identity,
                    "email");
                break;
            case "duplicateEmail":
                identity.AddClaim(new Claim(
                    "email",
                    "second@example.test"));
                break;
            case "missingEmailVerified":
                RemoveClaim(
                    identity,
                    "email_verified");
                break;
            case "duplicateEmailVerified":
                identity.AddClaim(new Claim(
                    "email_verified",
                    "true"));
                break;
            case "invalidEmailVerified":
                RemoveClaim(
                    identity,
                    "email_verified");
                identity.AddClaim(new Claim(
                    "email_verified",
                    "not-a-boolean"));
                break;
            case "missingIssuedAt":
                RemoveClaim(
                    identity,
                    JwtRegisteredClaimNames.Iat);
                break;
            case "duplicateIssuedAt":
                identity.AddClaim(new Claim(
                    JwtRegisteredClaimNames.Iat,
                    _now.ToUnixTimeSeconds().ToString(
                        System.Globalization.CultureInfo.InvariantCulture)));
                break;
            case "duplicateHostedDomain":
                identity.AddClaim(new Claim(
                    "hd",
                    "second.example.test"));
                break;
            case "duplicateDisplayName":
                identity.AddClaim(new Claim(
                    "name",
                    "Second Name"));
                break;
            case "duplicateAuthorizedParty":
                identity.AddClaim(new Claim(
                    "azp",
                    ClientId));
                break;
            case "missingAudience":
                RemoveClaim(
                    identity,
                    JwtRegisteredClaimNames.Aud);
                break;
        }
    }

    private static void RemoveClaim(
        ClaimsIdentity identity,
        string claimType)
    {
        var claim = Assert.Single(identity.FindAll(claimType));
        identity.RemoveClaim(claim);
    }

    private static TokenValidatedContext CreateTokenValidatedContext(
        ClaimsPrincipal principal,
        JwtSecurityToken? securityToken = null)
    {
        var context = new TokenValidatedContext(
            new DefaultHttpContext(),
            CreateScheme(),
            new OpenIdConnectOptions(),
            principal,
            new AuthenticationProperties());
        context.SecurityToken = securityToken ?? CreateSecurityToken(
            SecurityAlgorithms.RsaSha256);

        return context;
    }

    private static TicketReceivedContext CreateTicketReceivedContext(ClaimsPrincipal principal)
    {
        var properties = new AuthenticationProperties
        {
            ExpiresUtc = _now.AddMinutes(5)
        };
        var ticket = new AuthenticationTicket(
            principal,
            properties,
            GoogleAuthenticationSchemes.OpenIdConnect);

        return new TicketReceivedContext(
            new DefaultHttpContext(),
            CreateScheme(),
            new OpenIdConnectOptions(),
            ticket);
    }

    private static JwtSecurityToken CreateSecurityToken(
        string algorithm,
        string signature = "c2lnbmF0dXJl")
    {
        var header = Base64UrlEncoder.Encode($"{{\"alg\":\"{algorithm}\"}}");
        var payload = Base64UrlEncoder.Encode("{}");
        var token = new JwtSecurityToken($"{header}.{payload}.{signature}");
        token.SigningKey = new SymmetricSecurityKey(new byte[32]);

        return token;
    }

    private static AuthenticationScheme CreateScheme()
    {

        return new AuthenticationScheme(
            GoogleAuthenticationSchemes.OpenIdConnect,
            "Google",
            typeof(OpenIdConnectHandler));
    }

    private static Exception CreateTransportException(string scenario)
    {

        return scenario switch
        {
            "http" => new HttpRequestException("provider unavailable"),
            "canceled" => new TaskCanceledException("provider timed out"),
            _ => new TimeoutException("provider timed out")
        };
    }

    private static DefaultHttpContext CreateRemoteFailureHttpContext(
        Mock<IAuthenticationService> authenticationServiceMock)
    {
        var context = new DefaultHttpContext();
        ConfigureAuthenticationService(
            context,
            authenticationServiceMock);

        return context;
    }

    private static void ConfigureAuthenticationService(
        HttpContext context,
        Mock<IAuthenticationService> authenticationServiceMock)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authenticationServiceMock.Object);
        context.RequestServices = services.BuildServiceProvider();
    }
}
