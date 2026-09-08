using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class ExecuteAdministrativeAccountErasureCommandValidatorTests
{
    private readonly ExecuteAdministrativeAccountErasureCommandValidator _validator = new();

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("SUPPORT-808\n", false)]
    [InlineData("\tSUPPORT-808", false)]
    [InlineData("SUPPORT\u0000808", false)]
    [InlineData(" SUPPORT-808 ", true)]
    public async Task ValidateAsync_WhenReferenceVaries_EnforcesItsContract(
        string? reference,
        bool valid)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var command = new ExecuteAdministrativeAccountErasureCommand(
            Guid.CreateVersion7(),
            memberId,
            memberId,
            reference);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            valid,
            result.IsValid);
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task ValidateAsync_WhenReferenceHasSurroundingSpaces_LimitsNormalizedLength(
        int length,
        bool valid)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var reference = "  " + new string(
            'a',
            length) + "  ";
        var command = new ExecuteAdministrativeAccountErasureCommand(
            Guid.CreateVersion7(),
            memberId,
            memberId,
            reference);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            valid,
            result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("01900000-0000-7000-8000-000000000001")]
    public async Task ValidateAsync_WhenConfirmationDoesNotMatch_RejectsTarget(string? confirmed)
    {
        // Arrange
        var command = new ExecuteAdministrativeAccountErasureCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            confirmed is null ? null : Guid.Parse(confirmed),
            "SUPPORT-808");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.ConfirmedMemberId));
    }

    [Fact]
    public async Task ValidateAsync_WhenIdentifiersAndReferenceAreMissing_AggregatesErrors()
    {
        // Arrange
        var command = new ExecuteAdministrativeAccountErasureCommand(
            Guid.Empty,
            null,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                "AdministratorId",
                "ConfirmedMemberId",
                "MemberId",
                "RequestReference"
            ],
            result.Errors
                .Select(error => error.PropertyName)
                .Distinct()
                .Order());
    }
}
