using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers.v1;

/// <summary>
/// Provides API endpoints for managing wishlists.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/wishlists")]
public class WishListController(IMediator mediator,
    ILogger<WishListController> logger,
    IMapper mapper)
    : ApiControllerBase(mediator, logger, mapper)
{

}
