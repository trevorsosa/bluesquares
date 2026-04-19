using BlueSquares.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueSquares.Controllers;

[ApiController]
[Route("api/queries")]
public class QueriesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public QueriesController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetQueries([FromQuery] bool unresolvedOnly = false)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var query = _context.InvoiceQueries
            .Include(q => q.Invoice)
                .ThenInclude(i => i.Client)
            .Where(q => q.Invoice.MerchantId == merchantId);

        if (unresolvedOnly)
            query = query.Where(q => !q.IsResolved);

        var results = await query
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => new
            {
                q.Id,
                q.QueryText,
                q.MerchantResponse,
                q.IsResolved,
                q.CreatedAt,
                q.ResolvedAt,
                invoice = new
                {
                    q.Invoice.Id,
                    q.Invoice.InvoiceNumber,
                    q.Invoice.Status,
                    client = new
                    {
                        q.Invoice.Client.Name,
                        q.Invoice.Client.WhatsAppNumber
                    }
                }
            })
            .ToListAsync();

        return Ok(results);
    }

    private Guid GetMerchantId()
    {
        var claim = User.Claims.FirstOrDefault(c => c.Type == "merchant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}
