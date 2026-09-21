namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;

/// <summary>Applies the operational log property's strict data allowlist.</summary>
public interface ILogPropertyFilter
{
    /// <summary>Copies only a safe typed technical value.</summary>
    /// <param name="name">The structured property name.</param>
    /// <param name="value">The proposed value, never formatted implicitly.</param>
    /// <returns>The safe primitive value, or null when rejected.</returns>
    object? Filter(
        string name,
        object? value);
}
