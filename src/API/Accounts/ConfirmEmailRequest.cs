namespace JennGllg.Fr.MonKado.Back.Api.Accounts;

public sealed record ConfirmEmailRequest(string? UserId, string? Token);
