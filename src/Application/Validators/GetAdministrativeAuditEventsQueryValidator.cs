using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates audit filters before normalization or database access.</summary>
public class GetAdministrativeAuditEventsQueryValidator : AbstractValidator<GetAdministrativeAuditEventsQuery>
{
    /// <summary>Configures all independent filter and pagination rules.</summary>
    public GetAdministrativeAuditEventsQueryValidator()
    {
        RuleFor(query => query.CallerId)
            .NotEmpty();
        RuleFor(query => query.Action)
            .IsInEnum();
        RuleFor(query => query.AdministratorId)
            .NotEqual(Guid.Empty);
        RuleFor(query => query.MemberId)
            .NotEqual(Guid.Empty);
        RuleFor(query => query.WishlistId)
            .NotEqual(Guid.Empty);
        RuleFor(query => query.ExportId)
            .NotEqual(Guid.Empty);
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage(ValidationMessages.InvalidPage);
        RuleFor(query => query.PageSize)
            .InclusiveBetween(
                1,
                100)
            .WithMessage(ValidationMessages.InvalidPageSize);
        RuleFor(query => query.RequestReference)
            .Must(reference => reference is null || (!string.IsNullOrWhiteSpace(reference) && reference.Trim().Length <= AdministrativeDataExportConstraints.MaximumReferenceLength && !reference.Any(char.IsControl)))
            .WithMessage(ValidationMessages.InvalidAdministrativeExportReference);
        RuleFor(query => query.From)
            .Must(value => value is null || AdministrativeAuditDate.IsValid(value))
            .WithMessage(ValidationMessages.InvalidAuditDate);
        RuleFor(query => query.To)
            .Must(value => value is null || AdministrativeAuditDate.IsValid(value))
            .WithMessage(ValidationMessages.InvalidAuditDate);
        RuleFor(query => query.To)
            .Must((
                query,
                to) => AdministrativeAuditDate.Parse(query.From!) < AdministrativeAuditDate.Parse(to!))
            .When(query => query.From is not null && query.To is not null && AdministrativeAuditDate.IsValid(query.From) && AdministrativeAuditDate.IsValid(query.To))
            .WithMessage(ValidationMessages.InvalidAuditDateRange);
    }
}
