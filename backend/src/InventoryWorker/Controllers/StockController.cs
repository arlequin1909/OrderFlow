using InventoryWorker.Data;
using InventoryWorker.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryWorker.Controllers;

[ApiController]
[Route("api/stock")]
public class StockController : ControllerBase
{
    private readonly InventoryDbContext _dbContext;

    public StockController(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Product>>> GetAll(CancellationToken cancellationToken)
    {
        var products = await _dbContext.Products.AsNoTracking().OrderBy(p => p.Sku).ToListAsync(cancellationToken);
        return Ok(products);
    }

    [HttpGet("{sku}")]
    public async Task<ActionResult<Product>> GetBySku(string sku, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }
}
