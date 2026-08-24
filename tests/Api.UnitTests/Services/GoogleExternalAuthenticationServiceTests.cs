using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class GoogleExternalAuthenticationServiceTests
{
    private static readonly DateTimeOffset _now = new(
        2026,
        8,
        24,
        10,
        0,
        0,
        TimeSpan.Zero);

    private readonly Mock<IAuthenticationService> _authenticationServiceMock;
    private readonly GoogleExternalAuthenticationService _service;

    public GoogleExternalAuthenticationServiceTests()
    {
        _authenticationServiceMock = new Mock<IAuthenticationService>(MockBehavior.Strict);
        _service = new GoogleExternalAuthenticationService(
            new FixedTimeProvider(_now),
            new GoogleReturnPathService(
                Microsoft.Extensions.Options.Options.Create(new GoogleAuthenticationOptions
                {
                    Enabled = true,
                    FrontendOrigin = "https://app.example.test",
                    DefaultReturnPath = "/my-lists",
                    AllowedReturnPaths =
                    [
                        "/my-lists"
                    ]
                }),
                new GoogleReturnPathValidator()));
    }

    [Fact]
    public void CreateChallengeProperties_WhenCurrentSessionExists_ProtectsOnlyTechnicalState()
    {
        // Arrange
        var currentSessionId = Guid.CreateVersion7();

        // Act
        var properties = _service.CreateChallengeProperties(
            "/my-lists",
            rememberMe: true,
            currentSessionId);

        // Assert
        Assert.False(properties.IsPersistent);
        Assert.False(properties.AllowRefresh);
        Assert.Equal(
            GoogleAuthenticationConstants.CompletionPath,
            properties.RedirectUri);
        Assert.Equal(
            _now.Add(GoogleAuthenticationConstants.TransientLifetime),
            properties.ExpiresUtc);
        Assert.Equal(
            "/my-lists",
            properties.Items[GoogleAuthenticationConstants.ReturnPathProperty]);
        Assert.Equal(
            "1",
            properties.Items[GoogleAuthenticationConstants.RememberMeProperty]);
        Assert.Equal(
            currentSessionId.ToString("D"),
            properties.Items[GoogleAuthenticationConstants.CurrentSessionIdProperty]);
        var flowId = Guid.ParseExact(
            properties.Items[GoogleAuthenticationConstants.FlowIdProperty] ?? string.Empty,
            "D");
        Assert.Equal(
            7,
            flowId.Version);
        Assert.DoesNotContain(
            properties.Items.Keys,
            key => key.Contains(
                "token",
                StringComparison.OrdinalIgnoreCase));
        Assert.False(properties.Items.ContainsKey(
            GoogleAuthenticationConstants.ExpectedMemberIdProperty));
        Assert.False(properties.Items.ContainsKey(
            GoogleAuthenticationConstants.FlowBindingProperty));
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void CreateChallengeProperties_WhenCurrentSessionIsMissing_ProtectsFixedLengthSentinel()
    {
        // Arrange

        // Act
        var properties = _service.CreateChallengeProperties(
            "/my-lists",
            rememberMe: false,
            currentSessionId: null);

        // Assert
        Assert.Equal(
            GoogleAuthenticationConstants.NoCurrentSessionValue,
            properties.Items[GoogleAuthenticationConstants.CurrentSessionIdProperty]);
        Assert.Equal(
            "0",
            properties.Items[GoogleAuthenticationConstants.RememberMeProperty]);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void MatchesFlowBinding_WhenBindingsMatch_ReturnsTrue()
    {
        // Arrange
        var flowBinding = _service.CreateFlowBinding();

        // Act
        var matches = _service.MatchesFlowBinding(
            flowBinding,
            flowBinding);

        // Assert
        Assert.True(matches);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void MatchesFlowBinding_WhenValidBindingsDiffer_ReturnsFalse()
    {
        // Arrange
        var firstBinding = _service.CreateFlowBinding();
        var secondBinding = _service.CreateFlowBinding();

        // Act
        var matches = _service.MatchesFlowBinding(
            firstBinding,
            secondBinding);

        // Assert
        Assert.False(matches);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void MatchesFlowBinding_WhenBrowserBindingIsInvalid_ReturnsFalse(
        string? browserFlowBinding)
    {
        // Arrange
        var protectedFlowBinding = _service.CreateFlowBinding();

        // Act
        var matches = _service.MatchesFlowBinding(
            protectedFlowBinding,
            browserFlowBinding);

        // Assert
        Assert.False(matches);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void MatchesFlowBinding_WhenProtectedBindingIsInvalid_ReturnsFalse()
    {
        // Arrange

        // Act
        var matches = _service.MatchesFlowBinding(
            "invalid",
            "invalid");

        // Assert
        Assert.False(matches);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void TryGetFlowBinding_WhenBindingIsMissing_ReturnsFalse()
    {
        // Arrange
        var properties = new AuthenticationProperties();

        // Act
        var isValid = _service.TryGetFlowBinding(
            properties,
            out var flowBinding);

        // Assert
        Assert.False(isValid);
        Assert.Empty(flowBinding);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void TryGetFlowBinding_WhenBindingValueIsNull_ReturnsFalse()
    {
        // Arrange
        var properties = new AuthenticationProperties();
        properties.Items[GoogleAuthenticationConstants.FlowBindingProperty] = null;

        // Act
        var isValid = _service.TryGetFlowBinding(
            properties,
            out var flowBinding);

        // Assert
        Assert.False(isValid);
        Assert.Empty(flowBinding);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void BuildBoundPath_WhenPathAlreadyHasQuery_AppendsBinding()
    {
        // Arrange
        const string FlowBinding = "opaque-flow-binding";

        // Act
        var path = _service.BuildBoundPath(
            "/#/login?error=google_auth_failed",
            FlowBinding);

        // Assert
        Assert.Equal(
            "/#/login?error=google_auth_failed&flow=opaque-flow-binding",
            path);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenProtectedTicketIsValid_ReturnsMinimalContext()
    {
        // Arrange
        var currentSessionId = Guid.CreateVersion7();
        var expectedMemberId = Guid.CreateVersion7();
        var properties = CreateCompletedProperties(
            "/my-lists",
            rememberMe: true,
            currentSessionId);
        properties.Items[GoogleAuthenticationConstants.ExpectedMemberIdProperty] =
            expectedMemberId.ToString("D");
        var ticket = new AuthenticationTicket(
            CreatePrincipal(),
            properties,
            GoogleAuthenticationSchemes.ExternalCookie);
        var context = CreateHttpContext();
        var cancellationToken = TestContext.Current.CancellationToken;
        _authenticationServiceMock
            .Setup(service => service.AuthenticateAsync(
                context,
                GoogleAuthenticationSchemes.ExternalCookie))
            .ReturnsAsync(AuthenticateResult.Success(ticket));

        // Act
        var result = await _service.AuthenticateAsync(
            context,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        var authentication = result.Context;
        Assert.Equal(
            "google-subject",
            authentication.Identity.Subject);
        Assert.Equal(
            "member@example.test",
            authentication.Identity.Email);
        Assert.True(authentication.Identity.EmailVerified);
        Assert.Equal(
            "example.test",
            authentication.Identity.HostedDomain);
        Assert.Equal(
            "Member Name",
            authentication.Identity.DisplayName);
        Assert.True(authentication.IsPersistent);
        Assert.Equal(
            "/my-lists",
            authentication.ReturnPath);
        Assert.Equal(
            expectedMemberId,
            authentication.ExpectedMemberId);
        Assert.Equal(
            currentSessionId,
            authentication.CurrentSessionId);
        Assert.Equal(
            7,
            authentication.FlowId.Version);
        Assert.Equal(
            properties.Items[GoogleAuthenticationConstants.FlowBindingProperty],
            result.FlowBinding);
        _authenticationServiceMock.Verify(service => service.AuthenticateAsync(
            context,
            GoogleAuthenticationSchemes.ExternalCookie),
            Times.Once);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenExpectedMemberSentinelIsProtected_ReturnsNewAccountContext()
    {
        // Arrange
        var properties = CreateCompletedProperties(
            "/my-lists",
            rememberMe: false,
            currentSessionId: null);
        properties.Items[GoogleAuthenticationConstants.ExpectedMemberIdProperty] =
            GoogleAuthenticationConstants.NoExpectedMemberValue;
        var ticket = new AuthenticationTicket(
            CreatePrincipal(),
            properties,
            GoogleAuthenticationSchemes.ExternalCookie);
        var context = CreateHttpContext();
        _authenticationServiceMock
            .Setup(service => service.AuthenticateAsync(
                context,
                GoogleAuthenticationSchemes.ExternalCookie))
            .ReturnsAsync(AuthenticateResult.Success(ticket));

        // Act
        var result = await _service.AuthenticateAsync(
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.Context.ExpectedMemberId);
        Assert.Null(result.Context.CurrentSessionId);
        Assert.False(result.Context.IsPersistent);
        _authenticationServiceMock.Verify(service => service.AuthenticateAsync(
            context,
            GoogleAuthenticationSchemes.ExternalCookie),
            Times.Once);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task AuthenticateAsync_WhenExpectedMemberSnapshotIsInvalid_ReturnsNull(
        string? expectedMemberValue)
    {
        // Arrange
        var properties = CreateCompletedProperties(
            "/my-lists",
            rememberMe: false,
            currentSessionId: null);

        if (expectedMemberValue is not null)
            properties.Items[GoogleAuthenticationConstants.ExpectedMemberIdProperty] =
                expectedMemberValue;

        var ticket = new AuthenticationTicket(
            CreatePrincipal(),
            properties,
            GoogleAuthenticationSchemes.ExternalCookie);
        var context = CreateHttpContext();
        _authenticationServiceMock
            .Setup(service => service.AuthenticateAsync(
                context,
                GoogleAuthenticationSchemes.ExternalCookie))
            .ReturnsAsync(AuthenticateResult.Success(ticket));

        // Act
        var result = await _service.AuthenticateAsync(
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
        _authenticationServiceMock.Verify(service => service.AuthenticateAsync(
            context,
            GoogleAuthenticationSchemes.ExternalCookie),
            Times.Once);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("missingReturnPath")]
    [InlineData("missingRememberMe")]
    [InlineData("invalidRememberMe")]
    [InlineData("missingFlowId")]
    [InlineData("invalidFlowId")]
    [InlineData("emptyFlowId")]
    [InlineData("missingFlowBinding")]
    [InlineData("invalidFlowBinding")]
    [InlineData("invalidReturnPath")]
    [InlineData("missingCurrentSession")]
    [InlineData("invalidCurrentSession")]
    public async Task AuthenticateAsync_WhenProtectedTechnicalPropertyIsInvalid_ReturnsNull(
        string scenario)
    {
        // Arrange
        var properties = CreateCompletedProperties(
            "/my-lists",
            rememberMe: false,
            currentSessionId: null);
        properties.Items[GoogleAuthenticationConstants.ExpectedMemberIdProperty] =
            GoogleAuthenticationConstants.NoExpectedMemberValue;
        ApplyInvalidPropertyScenario(
            properties,
            scenario);
        var ticket = new AuthenticationTicket(
            CreatePrincipal(),
            properties,
            GoogleAuthenticationSchemes.ExternalCookie);
        var context = CreateHttpContext();
        _authenticationServiceMock
            .Setup(service => service.AuthenticateAsync(
                context,
                GoogleAuthenticationSchemes.ExternalCookie))
            .ReturnsAsync(AuthenticateResult.Success(ticket));

        // Act
        var result = await _service.AuthenticateAsync(
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
        _authenticationServiceMock.Verify(service => service.AuthenticateAsync(
            context,
            GoogleAuthenticationSchemes.ExternalCookie),
            Times.Once);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("missingSubject")]
    [InlineData("duplicateSubject")]
    [InlineData("missingEmail")]
    [InlineData("duplicateEmail")]
    [InlineData("missingEmailVerified")]
    [InlineData("duplicateEmailVerified")]
    [InlineData("invalidEmailVerified")]
    [InlineData("duplicateHostedDomain")]
    [InlineData("duplicateDisplayName")]
    public async Task AuthenticateAsync_WhenProtectedClaimSetIsInvalid_ReturnsNull(string scenario)
    {
        // Arrange
        var properties = CreateCompletedProperties(
            "/my-lists",
            rememberMe: false,
            currentSessionId: null);
        properties.Items[GoogleAuthenticationConstants.ExpectedMemberIdProperty] =
            GoogleAuthenticationConstants.NoExpectedMemberValue;
        var principal = CreatePrincipal();
        ApplyInvalidClaimScenario(
            principal,
            scenario);
        var ticket = new AuthenticationTicket(
            principal,
            properties,
            GoogleAuthenticationSchemes.ExternalCookie);
        var context = CreateHttpContext();
        _authenticationServiceMock
            .Setup(service => service.AuthenticateAsync(
                context,
                GoogleAuthenticationSchemes.ExternalCookie))
            .ReturnsAsync(AuthenticateResult.Success(ticket));

        // Act
        var result = await _service.AuthenticateAsync(
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
        _authenticationServiceMock.Verify(service => service.AuthenticateAsync(
            context,
            GoogleAuthenticationSchemes.ExternalCookie),
            Times.Once);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenOptionalClaimsAreMissing_ReturnsContextWithoutOptionalValues()
    {
        // Arrange
        var properties = CreateCompletedProperties(
            "/my-lists",
            rememberMe: false,
            currentSessionId: null);
        properties.Items[GoogleAuthenticationConstants.ExpectedMemberIdProperty] =
            GoogleAuthenticationConstants.NoExpectedMemberValue;
        var principal = CreatePrincipal();
        var identity = Assert.IsType<ClaimsIdentity>(principal.Identity);
        RemoveClaim(
            identity,
            "hd");
        RemoveClaim(
            identity,
            "name");
        var ticket = new AuthenticationTicket(
            principal,
            properties,
            GoogleAuthenticationSchemes.ExternalCookie);
        var context = CreateHttpContext();
        _authenticationServiceMock
            .Setup(service => service.AuthenticateAsync(
                context,
                GoogleAuthenticationSchemes.ExternalCookie))
            .ReturnsAsync(AuthenticateResult.Success(ticket));

        // Act
        var result = await _service.AuthenticateAsync(
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.Context.Identity.HostedDomain);
        Assert.Null(result.Context.Identity.DisplayName);
        _authenticationServiceMock.Verify(service => service.AuthenticateAsync(
            context,
            GoogleAuthenticationSchemes.ExternalCookie),
            Times.Once);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenCancellationIsRequestedBeforeAuthentication_ThrowsOperationCanceled()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var context = CreateHttpContext();

        // Act
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.AuthenticateAsync(
                context,
                cancellationTokenSource.Token));

        // Assert
        Assert.Equal(
            cancellationTokenSource.Token,
            exception.CancellationToken);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenCancellationIsRequestedDuringAuthentication_ThrowsOperationCanceled()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var context = CreateHttpContext();
        _authenticationServiceMock
            .Setup(service => service.AuthenticateAsync(
                context,
                GoogleAuthenticationSchemes.ExternalCookie))
            .Returns(() =>
            {
                cancellationTokenSource.Cancel();

                return Task.FromResult(AuthenticateResult.NoResult());
            });

        // Act
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.AuthenticateAsync(
                context,
                cancellationTokenSource.Token));

        // Assert
        Assert.Equal(
            cancellationTokenSource.Token,
            exception.CancellationToken);
        _authenticationServiceMock.Verify(service => service.AuthenticateAsync(
            context,
            GoogleAuthenticationSchemes.ExternalCookie),
            Times.Once);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAsync_WhenCalled_SignsOutOnlyExternalScheme()
    {
        // Arrange
        var context = CreateHttpContext();
        _authenticationServiceMock
            .Setup(service => service.SignOutAsync(
                context,
                GoogleAuthenticationSchemes.ExternalCookie,
                null))
            .Returns(Task.CompletedTask);

        // Act
        await _service.DeleteAsync(
            context,
            TestContext.Current.CancellationToken);

        // Assert
        _authenticationServiceMock.Verify(service => service.SignOutAsync(
            context,
            GoogleAuthenticationSchemes.ExternalCookie,
            null),
            Times.Once);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAsync_WhenCancellationIsRequested_ThrowsOperationCanceled()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var context = CreateHttpContext();

        // Act
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.DeleteAsync(
                context,
                cancellationTokenSource.Token));

        // Assert
        Assert.Equal(
            cancellationTokenSource.Token,
            exception.CancellationToken);
        _authenticationServiceMock.VerifyNoOtherCalls();
    }

    private AuthenticationProperties CreateCompletedProperties(
        string returnPath,
        bool rememberMe,
        Guid? currentSessionId)
    {
        var properties = _service.CreateChallengeProperties(
            returnPath,
            rememberMe,
            currentSessionId);
        properties.Items[GoogleAuthenticationConstants.FlowBindingProperty] =
            _service.CreateFlowBinding();

        return properties;
    }

    private DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_authenticationServiceMock.Object);

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
    }

    private static ClaimsPrincipal CreatePrincipal()
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
                "hd",
                "example.test"),
            new Claim(
                "name",
                "Member Name")
        ],
        GoogleAuthenticationSchemes.ExternalCookie));
    }

    private static void ApplyInvalidPropertyScenario(
        AuthenticationProperties properties,
        string scenario)
    {
        switch (scenario)
        {
            case "missingReturnPath":
                properties.Items.Remove(GoogleAuthenticationConstants.ReturnPathProperty);
                break;
            case "missingRememberMe":
                properties.Items.Remove(GoogleAuthenticationConstants.RememberMeProperty);
                break;
            case "invalidRememberMe":
                properties.Items[GoogleAuthenticationConstants.RememberMeProperty] = "True";
                break;
            case "missingFlowId":
                properties.Items.Remove(GoogleAuthenticationConstants.FlowIdProperty);
                break;
            case "invalidFlowId":
                properties.Items[GoogleAuthenticationConstants.FlowIdProperty] = "not-a-guid";
                break;
            case "emptyFlowId":
                properties.Items[GoogleAuthenticationConstants.FlowIdProperty] = Guid.Empty.ToString("D");
                break;
            case "missingFlowBinding":
                properties.Items.Remove(GoogleAuthenticationConstants.FlowBindingProperty);
                break;
            case "invalidFlowBinding":
                properties.Items[GoogleAuthenticationConstants.FlowBindingProperty] =
                    "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!";
                break;
            case "invalidReturnPath":
                properties.Items[GoogleAuthenticationConstants.ReturnPathProperty] = "//evil.example";
                break;
            case "missingCurrentSession":
                properties.Items.Remove(GoogleAuthenticationConstants.CurrentSessionIdProperty);
                break;
            case "invalidCurrentSession":
                properties.Items[GoogleAuthenticationConstants.CurrentSessionIdProperty] = "not-a-guid";
                break;
        }
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
        }
    }

    private static void RemoveClaim(
        ClaimsIdentity identity,
        string claimType)
    {
        var claim = Assert.Single(identity.FindAll(claimType));
        identity.RemoveClaim(claim);
    }
}
