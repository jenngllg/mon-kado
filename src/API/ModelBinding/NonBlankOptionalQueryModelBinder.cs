using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace JennGllg.Fr.MonKado.Back.Api.ModelBinding;

/// <summary>Preserves optional query values while rejecting supplied blanks instead of silently removing a filter.</summary>
/// <typeparam name="T">The nullable scalar query type.</typeparam>
/// <param name="loggerFactory">The framework model-binding logger factory.</param>
public class NonBlankOptionalQueryModelBinder<T>(ILoggerFactory loggerFactory) : SimpleTypeModelBinder(
    typeof(T),
    loggerFactory)
{
    /// <summary>Rejects a supplied blank value; absent parameters are left unbound by the base binder.</summary>
    /// <param name="bindingContext">The query binding context.</param>
    /// <param name="valueProviderResult">The supplied raw query value.</param>
    /// <param name="model">The framework-converted scalar.</param>
    protected override void CheckModel(
        ModelBindingContext bindingContext,
        ValueProviderResult valueProviderResult,
        object? model)
    {

        if (model is null)
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                ValidationMessages.BlankQueryParameter);

            return;
        }

        base.CheckModel(
            bindingContext,
            valueProviderResult,
            model);
    }
}
