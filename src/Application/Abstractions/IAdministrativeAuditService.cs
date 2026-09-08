using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Reads retained administrative events in one consistent database snapshot.</summary>
public interface IAdministrativeAuditService
{
    /// <summary>Returns the filtered page without accessing private target content.</summary>
    /// <param name="filter">The normalized validated filters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The filtered page and matching total.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL cannot serve a consistent snapshot.</exception>
    /// <exception cref="OperationCanceledException">The request was canceled.</exception>
    Task<AdministrativeAuditPage> GetPageAsync(
        AdministrativeAuditFilter filter,
        CancellationToken cancellationToken);
}
