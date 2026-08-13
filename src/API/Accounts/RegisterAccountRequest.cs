namespace JennGllg.Fr.MonKado.Back.Api.Accounts;

public sealed record RegisterAccountRequest(
    string? Email,
    string? Password,
    string? DisplayName);
