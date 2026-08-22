using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class MemberProfileServiceTests
{
    private readonly Mock<IMemberRepository> _memberRepositoryMock;
    private readonly MemberProfileService _memberProfileService;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;

    public MemberProfileServiceTests()
    {
        _memberRepositoryMock = new Mock<IMemberRepository>(MockBehavior.Strict);
        _unitOfWorkMock = new Mock<IUnitOfWork>(MockBehavior.Strict);
        _memberProfileService = new MemberProfileService(
            _memberRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task UpdateAsync_WhenDisplayNameChanges_UpdatesAndSavesProfile()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = CreateMember(
            memberId,
            "Jenn");
        _memberRepositoryMock
            .Setup(repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(member);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);

        // Act
        var result = await _memberProfileService.UpdateAsync(
            memberId,
            "Jennifer",
            0,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            "Jennifer",
            result.DisplayName);
        Assert.Equal(
            0u,
            result.Version);
        Assert.Equal(
            "Jennifer",
            member.DisplayName);
        _memberRepositoryMock.Verify(
            repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenSaveDetectsDeletedMember_ReturnsNull()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = CreateMember(
            memberId,
            "Jenn");
        _memberRepositoryMock
            .SetupSequence(repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(member)
            .ReturnsAsync((MonKadoUser?)null);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        // Act
        var result = await _memberProfileService.UpdateAsync(
            memberId,
            "Jennifer",
            0,
            cancellationToken);

        // Assert
        Assert.Null(result);
        _memberRepositoryMock.Verify(
            repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken),
            Times.Exactly(2));
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenConcurrencyResolutionTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = CreateMember(
            memberId,
            "Jenn");
        _memberRepositoryMock
            .SetupSequence(repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(member)
            .ThrowsAsync(new TimeoutException());
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        // Act
        var action = () => _memberProfileService.UpdateAsync(
            memberId,
            "Jennifer",
            0,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        Assert.IsType<TimeoutException>(exception.InnerException);
        _memberRepositoryMock.Verify(
            repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken),
            Times.Exactly(2));
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenConcurrencyResolutionThrowsUnrelatedException_PropagatesException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = CreateMember(
            memberId,
            "Jenn");
        var expected = new InvalidOperationException();
        _memberRepositoryMock
            .SetupSequence(repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(member)
            .ThrowsAsync(expected);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        // Act
        var action = () => _memberProfileService.UpdateAsync(
            memberId,
            "Jennifer",
            0,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(
            expected,
            exception);
        _memberRepositoryMock.Verify(
            repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken),
            Times.Exactly(2));
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenDisplayNameIsUnchanged_ReturnsProfileWithoutSaving()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = CreateMember(
            memberId,
            "Jenn");
        _memberRepositoryMock
            .Setup(repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(member);

        // Act
        var result = await _memberProfileService.UpdateAsync(
            memberId,
            "Jenn",
            0,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            "Jenn",
            result.DisplayName);
        Assert.Equal(
            0u,
            result.Version);
        _memberRepositoryMock.Verify(
            repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenMemberDoesNotExist_ReturnsNullWithoutSaving()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _memberRepositoryMock
            .Setup(repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync((MonKadoUser?)null);

        // Act
        var result = await _memberProfileService.UpdateAsync(
            memberId,
            "Jenn",
            0,
            cancellationToken);

        // Assert
        Assert.Null(result);
        _memberRepositoryMock.Verify(
            repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenExpectedVersionIsStale_ThrowsMemberProfileVersionConflictException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = CreateMember(
            memberId,
            "Jenn");
        _memberRepositoryMock
            .Setup(repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(member);

        // Act
        var action = () => _memberProfileService.UpdateAsync(
            memberId,
            "Jennifer",
            1,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<MemberProfileVersionConflictException>(action);
        _memberRepositoryMock.Verify(
            repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenSaveDetectsConcurrencyConflict_ThrowsMemberProfileVersionConflictException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = CreateMember(
            memberId,
            "Jenn");
        _memberRepositoryMock
            .SetupSequence(repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(member)
            .ReturnsAsync(member);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        // Act
        var action = () => _memberProfileService.UpdateAsync(
            memberId,
            "Jennifer",
            0,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<MemberProfileVersionConflictException>(action);
        _memberRepositoryMock.Verify(
            repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken),
            Times.Exactly(2));
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _memberRepositoryMock
            .Setup(repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _memberProfileService.UpdateAsync(
            memberId,
            "Jenn",
            0,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        Assert.IsType<TimeoutException>(exception.InnerException);
        _memberRepositoryMock.Verify(
            repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenRepositoryThrowsUnrelatedException_PropagatesException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new InvalidOperationException();
        _memberRepositoryMock
            .Setup(repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken))
            .ThrowsAsync(expected);

        // Act
        var action = () => _memberProfileService.UpdateAsync(
            memberId,
            "Jenn",
            0,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(
            expected,
            exception);
        _memberRepositoryMock.Verify(
            repository => repository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private void VerifyNoOtherCalls()
    {
        _memberRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    private static MonKadoUser CreateMember(
        Guid memberId,
        string displayName)
    {

        return new MonKadoUser
        {
            Id = memberId,
            DisplayName = displayName
        };
    }
}
