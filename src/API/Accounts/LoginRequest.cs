namespace JennGllg.Fr.MonKado.Back.Api.Accounts;

public sealed record LoginRequest(string? Email, string? Password, bool RememberMe = false);
