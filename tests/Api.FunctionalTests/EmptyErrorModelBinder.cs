using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class EmptyErrorModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        bindingContext.ModelState.AddModelError(
            bindingContext.ModelName,
            string.Empty);
        bindingContext.Result = ModelBindingResult.Failed();

        return Task.CompletedTask;
    }
}
