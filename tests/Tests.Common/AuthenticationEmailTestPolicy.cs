using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Tests.Common;

/// <summary>
/// Creates deterministic authentication e-mail delivery policies for tests.
/// </summary>
public static class AuthenticationEmailTestPolicy
{
    /// <summary>
    /// Creates the default delivery policy.
    /// </summary>
    /// <returns>The delivery policy.</returns>
    public static AuthenticationEmailDeliveryPolicy CreateDefault()
    {

        return Create(
            20,
            TimeSpan.FromMinutes(2),
            10);
    }

    /// <summary>
    /// Creates a policy that claims one message per cycle.
    /// </summary>
    /// <returns>The delivery policy.</returns>
    public static AuthenticationEmailDeliveryPolicy CreateSingleMessage()
    {

        return Create(
            1,
            TimeSpan.FromMinutes(2),
            10);
    }

    /// <summary>
    /// Creates the policy used by the Google concurrency scenario.
    /// </summary>
    /// <returns>The delivery policy.</returns>
    public static AuthenticationEmailDeliveryPolicy CreateGoogleConcurrency()
    {

        return Create(
            10,
            TimeSpan.FromMinutes(1),
            10);
    }

    /// <summary>
    /// Creates a policy that terminates delivery after the first failure.
    /// </summary>
    /// <returns>The delivery policy.</returns>
    public static AuthenticationEmailDeliveryPolicy CreateTerminalFailure()
    {

        return Create(
            20,
            TimeSpan.FromMinutes(2),
            1);
    }

    private static AuthenticationEmailDeliveryPolicy Create(
        int batchSize,
        TimeSpan leaseDuration,
        int maximumAttempts)
    {

        return new AuthenticationEmailDeliveryPolicy(
            batchSize,
            leaseDuration,
            maximumAttempts,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(6),
            TimeSpan.FromHours(6),
            TimeSpan.FromHours(24));
    }
}
