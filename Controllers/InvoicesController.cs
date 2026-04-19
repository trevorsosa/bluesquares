using BlueSquares.Data;
using BlueSquares.Filters;
using BlueSquares.Models;
using BlueSquares.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueSquares.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvoicesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IEmailService _emailService;
    private readonly IPdfService _pdfService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IInvoiceService _invoiceService;
    private readonly ILogger<InvoicesController> _logger;

    public InvoicesController(
        ApplicationDbContext context,
        IWhatsAppService whatsAppService,
        IEmailService emailService,
        IPdfService pdfService,
        ISubscriptionService subscriptionService,
        IInvoiceService invoiceService,
        ILogger<InvoicesController> logger)
    {
        _context = context;
        _whatsAppService = whatsAppService;
        _emailService = emailService;
        _pdfService = pdfService;
        _subscriptionService = subscriptionService;
        _invoiceService = invoiceService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetInvoices([FromQuery] string? status = null, [FromQuery] int? limit = null)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var query = _context.Invoices
            .Include(i => i.Client)
            .Where(i => i.MerchantId == merchantId);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(i => i.Status == status);

        IQueryable<Invoice> orderedQuery = query.OrderByDescending(i => i.CreatedAt);
        if (limit.HasValue && limit.Value > 0)
            orderedQuery = orderedQuery.Take(limit.Value);

        var invoices = await orderedQuery
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.InvoiceDate,
                i.DueDate,
                i.TotalAmount,
                i.Currency,
                i.Status,
                i.PaidDate,
                Client = new { i.Client.Id, i.Client.Name, i.Client.WhatsAppNumber }
            })
            .ToListAsync();

        return Ok(invoices);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetInvoice(Guid id)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var invoice = await _context.Invoices
            .Include(i => i.Client)
            .Include(i => i.LineItems)
            .Include(i => i.Queries)
            .FirstOrDefaultAsync(i => i.Id == id && i.MerchantId == merchantId);

        if (invoice == null)
            return NotFound();

        return Ok(invoice);
    }

    [HttpPost]
    [RequireActiveSubscription]
    public async Task<IActionResult> CreateInvoice([FromBody] InvoiceCreateDto invoiceData)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        try
        {
            var result = await _invoiceService.CreateInvoiceAsync(merchantId, new InvoiceCreationRequest
            {
                ClientId = invoiceData.ClientId,
                InvoiceNumber = invoiceData.InvoiceNumber,
                DueDate = invoiceData.DueDate,
                TotalAmount = invoiceData.TotalAmount,
                Notes = invoiceData.Notes,
                LineItems = invoiceData.LineItems?.Select(li => new InvoiceLineItemRequest
                {
                    Description = li.Description,
                    Quantity = li.Quantity,
                    UnitPrice = li.UnitPrice
                }).ToList() ?? new List<InvoiceLineItemRequest>()
            });

            var invoice = await _context.Invoices
                .Include(i => i.LineItems)
                .FirstAsync(i => i.Id == result.InvoiceId);

            return Ok(invoice);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateInvoice(Guid id, [FromBody] InvoiceCreateDto invoiceData)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var invoice = await _context.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == id && i.MerchantId == merchantId);

        if (invoice == null)
            return NotFound();

        if (invoice.Status == "Paid")
            return BadRequest(new { message = "Cannot edit paid invoice" });

        invoice.DueDate = invoiceData.DueDate ?? invoice.DueDate;
        invoice.Notes = invoiceData.Notes;
        invoice.UpdatedAt = DateTime.UtcNow;

        // Update line items
        if (invoiceData.LineItems != null)
        {
            // Remove old items
            _context.InvoiceLineItems.RemoveRange(invoice.LineItems);
            
            // Add new items
            foreach (var item in invoiceData.LineItems)
            {
                var lineItem = new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice
                };
                _context.InvoiceLineItems.Add(lineItem);
            }
            
            invoice.TotalAmount = invoiceData.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        }
        else if (invoiceData.TotalAmount > 0)
        {
            invoice.TotalAmount = invoiceData.TotalAmount;
        }

        await _context.SaveChangesAsync();

        return Ok(invoice);
    }

    [HttpPost("{id}/send")]
    [RequireActiveSubscription]
    public async Task<IActionResult> SendInvoice(Guid id, [FromBody] SendInvoiceDto sendData)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var invoice = await _context.Invoices
            .FirstOrDefaultAsync(i => i.Id == id && i.MerchantId == merchantId);

        if (invoice == null)
            return NotFound();

        var whatsappSent = await _invoiceService.SendInvoiceAsync(id, sendData.SendEmail, true);
        var emailSent = sendData.SendEmail;

        return Ok(new
        {
            message = "Invoice sent successfully",
            whatsappSent,
            emailSent
        });
    }

    [HttpPost("{id}/nudge")]
    public async Task<IActionResult> NudgeClient(Guid id)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var invoice = await _context.Invoices
            .FirstOrDefaultAsync(i => i.Id == id && i.MerchantId == merchantId);

        if (invoice == null)
            return NotFound();

        if (invoice.Status == "Paid")
            return BadRequest(new { message = "Invoice is already paid" });

        var sent = await _whatsAppService.SendReminderMessage(id);

        return Ok(new { message = "Reminder sent successfully", sent });
    }

    [HttpPost("{id}/mark-paid")]
    public async Task<IActionResult> MarkAsPaid(Guid id, [FromBody] MarkPaidDto paidData)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var invoice = await _context.Invoices
            .FirstOrDefaultAsync(i => i.Id == id && i.MerchantId == merchantId);

        if (invoice == null)
            return NotFound();

        invoice.Status = "Paid";
        invoice.PaidDate = paidData.PaidDate ?? DateTime.UtcNow;
        invoice.PaymentMethod = paidData.PaymentMethod ?? "Manual";
        invoice.PaymentReference = paidData.PaymentReference;
        invoice.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        if (paidData.SendReceipt)
        {
            await _whatsAppService.SendReceiptMessage(id);
            await _emailService.SendReceiptEmail(id);
        }

        return Ok(new { message = "Invoice marked as paid" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteInvoice(Guid id)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var invoice = await _context.Invoices
            .FirstOrDefaultAsync(i => i.Id == id && i.MerchantId == merchantId);

        if (invoice == null)
            return NotFound();

        if (invoice.Status == "Paid")
            return BadRequest(new { message = "Cannot delete paid invoice" });

        _context.Invoices.Remove(invoice);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Invoice deleted successfully" });
    }

    [HttpGet("{id}/queries")]
    public async Task<IActionResult> GetInvoiceQueries(Guid id)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var invoice = await _context.Invoices
            .FirstOrDefaultAsync(i => i.Id == id && i.MerchantId == merchantId);

        if (invoice == null)
            return NotFound();

        var queries = await _context.InvoiceQueries
            .Where(q => q.InvoiceId == id)
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync();

        return Ok(queries);
    }

    [HttpPost("queries/{queryId}/respond")]
    public async Task<IActionResult> RespondToQuery(Guid queryId, [FromBody] RespondToQueryDto response)
    {
        var query = await _context.InvoiceQueries
            .Include(q => q.Invoice)
            .FirstOrDefaultAsync(q => q.Id == queryId);

        if (query == null)
            return NotFound();

        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty || query.Invoice.MerchantId != merchantId)
            return Unauthorized();

        query.MerchantResponse = response.Response;
        query.IsResolved = response.MarkAsResolved;
        query.ResolvedAt = response.MarkAsResolved ? DateTime.UtcNow : null;

        await _context.SaveChangesAsync();

        // Send response via WhatsApp
        await _whatsAppService.SendMerchantReply(queryId, response.Response);

        return Ok(new { message = "Response sent successfully" });
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> DownloadInvoicePdf(Guid id)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var exists = await _context.Invoices
            .AnyAsync(i => i.Id == id && i.MerchantId == merchantId);

        if (!exists)
            return NotFound();

        try
        {
            var pdf = await _pdfService.GenerateInvoicePdf(id);
            return File(pdf, "application/pdf", $"invoice-{id}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating invoice PDF for {InvoiceId}", id);
            return StatusCode(500, new { message = "Failed to generate PDF" });
        }
    }

    [HttpGet("{id}/receipt/pdf")]
    public async Task<IActionResult> DownloadReceiptPdf(Guid id)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var invoice = await _context.Invoices
            .FirstOrDefaultAsync(i => i.Id == id && i.MerchantId == merchantId);

        if (invoice == null)
            return NotFound();

        if (invoice.Status != "Paid")
            return BadRequest(new { message = "Invoice is not yet paid" });

        try
        {
            var pdf = await _pdfService.GenerateReceiptPdf(id);
            return File(pdf, "application/pdf", $"receipt-{id}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating receipt PDF for {InvoiceId}", id);
            return StatusCode(500, new { message = "Failed to generate PDF" });
        }
    }

    private Guid GetMerchantId()
    {
        var merchantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "merchant_id")?.Value;
        if (Guid.TryParse(merchantIdClaim, out var merchantId))
            return merchantId;
        
        return Guid.Empty;
    }
}

public class InvoiceCreateDto
{
    public Guid ClientId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public List<LineItemDto>? LineItems { get; set; }
}

public class LineItemDto
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}

public class SendInvoiceDto
{
    public bool SendEmail { get; set; } = false;
}

public class MarkPaidDto
{
    public DateTime? PaidDate { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }
    public bool SendReceipt { get; set; } = true;
}

public class RespondToQueryDto
{
    public string Response { get; set; } = string.Empty;
    public bool MarkAsResolved { get; set; } = true;
}
