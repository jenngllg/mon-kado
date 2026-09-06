using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates public member search terms and pagination.</summary>
public class SearchUsersQueryValidator : AbstractValidator<SearchUsersQuery>
{
    /// <summary>Configures the shared display-name rules and search-specific limits.</summary>
    public SearchUsersQueryValidator()
    {
        RuleFor(query => query.DisplayName)
            .ApplyDisplayNameRules()
            .Must(value => value!
                .Trim()
                .Normalize(NormalizationForm.FormC)
                .EnumerateRunes()
                .Count() >= 2)
            .WithMessage("The display name search must contain at least 2 characters.");
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(SearchUsersQuery.DefaultPage)
            .When(query => query.Page.HasValue)
            .WithMessage(ValidationMessages.InvalidPage);
        RuleFor(query => query.PageSize)
            .InclusiveBetween(
            SearchUsersQuery.DefaultPage,
            SearchUsersQuery.MaximumPageSize)
            .When(query => query.PageSize.HasValue)
            .WithMessage(ValidationMessages.InvalidPageSize);
    }
}
