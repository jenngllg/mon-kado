using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Serves as the base class for API controllers.
/// </summary>
/// <remarks>This class is intended to be inherited by specific API controllers in an ASP.NET Core application. It
/// provides access to an <see cref="IMediator"/> instance for handling requests and an <see
/// cref="ILogger{TCategoryName}"/> instance for logging purposes.</remarks>
[ApiController]
public class ApiControllerBase(IMediator mediator,
    ILogger<ApiControllerBase> logger,
    IMapper mapper)
    : ControllerBase
{
    /// <summary>
    /// Provides access to the mediator instance used for sending requests and publishing notifications.
    /// </summary>
    protected readonly IMediator Mediator = mediator;

    /// <summary>
    /// Provides logging functionality for the API controller.
    /// </summary>
    protected readonly ILogger<ApiControllerBase> Logger = logger;

    /// <summary>
    /// Provides access to the <see cref="IMapper"/> instance used for object mapping operations.
    /// </summary>
    protected readonly IMapper Mapper = mapper;
}
