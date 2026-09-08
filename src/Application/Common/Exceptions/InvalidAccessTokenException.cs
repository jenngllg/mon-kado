namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Rejects Bearer credentials without invalidating a separate refresh cookie.</summary>
public class InvalidAccessTokenException()
    : Exception("The access token is invalid or revoked.")
{
}
