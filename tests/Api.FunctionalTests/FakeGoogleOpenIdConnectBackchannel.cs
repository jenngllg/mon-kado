using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class FakeGoogleOpenIdConnectBackchannel : HttpMessageHandler
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly TimeProvider _timeProvider;

    public FakeGoogleOpenIdConnectBackchannel(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        SigningKey = new RsaSecurityKey(_rsa)
        {
            KeyId = "functional-google-key"
        };
    }

    public RsaSecurityKey SigningKey
    {
        get;
    }

    public string? Nonce
    {
        get; set;
    }

    public bool EmailVerified { get; set; } = true;

    public string? DisplayName { get; set; } = "Functional Member";

    public string Subject { get; set; } = "functional-google-subject";

    public string Email { get; set; } = "member@gmail.com";

    public string AuthorizedParty { get; set; } = GoogleAuthenticationApiFactory.ClientId;

    public string IdentityTokenIssuer { get; set; } = GoogleAuthenticationApiFactory.Issuer;

    public string IdentityTokenAudience { get; set; } = GoogleAuthenticationApiFactory.ClientId;

    public TimeSpan IdentityTokenAge
    {
        get; set;
    }

    public TimeSpan IdentityTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    public bool IncludeIssuedAt { get; set; } = true;

    public string? IdentityTokenOverride
    {
        get; set;
    }

    public string? ExpectedCodeChallenge
    {
        get; set;
    }

    public bool UseUnsignedIdentityToken
    {
        get; set;
    }

    public bool UseInvalidSigningKey
    {
        get; set;
    }

    public bool UseHmacSigningKey
    {
        get; set;
    }

    public bool IsTokenEndpointUnavailable
    {
        get; set;
    }

    public HttpStatusCode? TokenEndpointStatusCode
    {
        get; set;
    }

    public int TokenRequestCount
    {
        get; private set;
    }

    public bool WasPkceValidated
    {
        get; private set;
    }

    public string? LastTokenRequestBody
    {
        get; private set;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        TokenRequestCount++;
        LastTokenRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        if (IsTokenEndpointUnavailable)
            throw new HttpRequestException("Simulated Google token endpoint outage.");

        if (TokenEndpointStatusCode.HasValue)
            return new HttpResponseMessage(TokenEndpointStatusCode.Value)
            {
                Content = new StringContent(
                    "{\"error\":\"temporarily_unavailable\",\"error_description\":\"provider-response-canary\"}",
                    Encoding.UTF8,
                    "application/json")
            };

        if (ExpectedCodeChallenge is not null && !ValidatePkce(LastTokenRequestBody))
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"error\":\"invalid_grant\"}",
                    Encoding.UTF8,
                    "application/json")
            };

        var response = new
        {
            access_token = "google-access-token-not-persisted",
            expires_in = 300,
            id_token = IdentityTokenOverride ?? CreateIdentityToken(),
            token_type = "Bearer"
        };
        var json = JsonSerializer.Serialize(response);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

    private bool ValidatePkce(string? requestBody)
    {
        var parameters = QueryHelpers.ParseQuery(string.Concat(
            "?",
            requestBody));
        var verifier = parameters["code_verifier"];
        var verifierValue = verifier.Count == 1
            ? verifier[0]
            : null;

        if (string.IsNullOrWhiteSpace(verifierValue))
            return false;

        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(
            Encoding.ASCII.GetBytes(verifierValue)));
        WasPkceValidated = string.Equals(
            ExpectedCodeChallenge,
            challenge,
            StringComparison.Ordinal);

        return WasPkceValidated;
    }

    protected override void Dispose(bool disposing)
    {

        if (disposing)
            _rsa.Dispose();

        base.Dispose(disposing);
    }

    private string CreateIdentityToken()
    {

        if (UseUnsignedIdentityToken)
            return CreateIdentityToken(signingCredentials: null);

        if (UseInvalidSigningKey)
        {
            using var rsa = RSA.Create(2048);
            var signingKey = new RsaSecurityKey(rsa)
            {
                KeyId = SigningKey.KeyId
            };

            return CreateIdentityToken(new SigningCredentials(
                signingKey,
                SecurityAlgorithms.RsaSha256));
        }

        return UseHmacSigningKey
            ? CreateIdentityToken(new SigningCredentials(
                new SymmetricSecurityKey(new byte[32]),
                SecurityAlgorithms.HmacSha256))
            : CreateIdentityToken(new SigningCredentials(
            SigningKey,
            SecurityAlgorithms.RsaSha256));
    }

    private string CreateIdentityToken(SigningCredentials? signingCredentials)
    {
        var now = _timeProvider.GetUtcNow();
        var issuedAt = now.Subtract(IdentityTokenAge);
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = Subject,
            ["email"] = Email,
            ["email_verified"] = EmailVerified,
            ["azp"] = AuthorizedParty,
            ["nonce"] = Nonce ?? string.Empty
        };

        if (DisplayName is not null)
            claims["name"] = DisplayName;

        var descriptor = new SecurityTokenDescriptor
        {
            Audience = IdentityTokenAudience,
            Claims = claims,
            Expires = issuedAt.Add(IdentityTokenLifetime).UtcDateTime,
            IssuedAt = IncludeIssuedAt
                ? issuedAt.UtcDateTime
                : null,
            Issuer = IdentityTokenIssuer,
            NotBefore = now.AddSeconds(-5).UtcDateTime,
            SigningCredentials = signingCredentials
        };
        var handler = new JwtSecurityTokenHandler
        {
            SetDefaultTimesOnTokenCreation = false
        };

        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
