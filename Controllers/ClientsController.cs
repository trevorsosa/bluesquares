using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlueSquares.Data;
using BlueSquares.Models;

namespace BlueSquares.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClientsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ClientsController> _logger;

    public ClientsController(ApplicationDbContext context, ILogger<ClientsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetClients()
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var clients = await _context.Clients
            .Where(c => c.MerchantId == merchantId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return Ok(clients);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetClient(Guid id)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var client = await _context.Clients
            .Include(c => c.Invoices)
            .FirstOrDefaultAsync(c => c.Id == id && c.MerchantId == merchantId);

        if (client == null)
            return NotFound();

        var balance = await _context.Invoices
            .Where(i => i.ClientId == id && i.Status != "Paid")
            .SumAsync(i => i.TotalAmount);

        return Ok(new { client, outstandingBalance = balance });
    }

    [HttpPost]
    public async Task<IActionResult> CreateClient([FromBody] Client clientData)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var client = new Client
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            Name = clientData.Name,
            WhatsAppNumber = clientData.WhatsAppNumber,
            Email = clientData.Email,
            CompanyName = clientData.CompanyName,
            CreatedAt = DateTime.UtcNow
        };

        _context.Clients.Add(client);
        await _context.SaveChangesAsync();

        return Ok(client);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateClient(Guid id, [FromBody] Client clientData)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == id && c.MerchantId == merchantId);

        if (client == null)
            return NotFound();

        client.Name = clientData.Name;
        client.WhatsAppNumber = clientData.WhatsAppNumber;
        client.Email = clientData.Email;
        client.CompanyName = clientData.CompanyName;
        client.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(client);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteClient(Guid id)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var client = await _context.Clients
            .Include(c => c.Invoices)
            .FirstOrDefaultAsync(c => c.Id == id && c.MerchantId == merchantId);

        if (client == null)
            return NotFound();

        if (client.Invoices.Any())
            return BadRequest(new { message = "Cannot delete client with existing invoices" });

        _context.Clients.Remove(client);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Client deleted successfully" });
    }

    [HttpGet("{id}/balance")]
    public async Task<IActionResult> GetClientBalance(Guid id)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == id && c.MerchantId == merchantId);

        if (client == null)
            return NotFound();

        var invoices = await _context.Invoices
            .Where(i => i.ClientId == id)
            .OrderByDescending(i => i.InvoiceDate)
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.InvoiceDate,
                i.DueDate,
                i.TotalAmount,
                i.Status,
                i.PaidDate
            })
            .ToListAsync();

        var totalOutstanding = invoices.Where(i => i.Status != "Paid").Sum(i => i.TotalAmount);
        var totalPaid = invoices.Where(i => i.Status == "Paid").Sum(i => i.TotalAmount);

        return Ok(new
        {
            client = new { client.Id, client.Name, client.Email },
            totalOutstanding,
            totalPaid,
            invoices
        });
    }

    private Guid GetMerchantId()
    {
        var merchantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "merchant_id")?.Value;
        if (Guid.TryParse(merchantIdClaim, out var merchantId))
            return merchantId;
        
        return Guid.Empty;
    }
}
