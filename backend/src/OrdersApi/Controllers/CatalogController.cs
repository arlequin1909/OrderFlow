using Microsoft.AspNetCore.Mvc;
using OrdersApi.Models;
using OrdersApi.Services;

namespace OrdersApi.Controllers;

/// <summary>
/// Read-only product catalog for the frontend's SKU picker. Lives here — not on
/// InventoryWorker — because InventoryWorker is a pure background service with no HTTP
/// surface at all (see README "Architecture decisions").
/// </summary>
[ApiController]
[Route("api/catalog")]
public class CatalogController : ControllerBase
{
    private readonly ICatalogService _catalogService;

    public CatalogController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CatalogProduct>>> GetAll(CancellationToken cancellationToken)
    {
        var products = await _catalogService.GetAllAsync(cancellationToken);
        return Ok(products);
    }
}
