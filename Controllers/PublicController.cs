using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlueSquares.Data;
using BlueSquares.Services;

namespace BlueSquares.Controllers;

[ApiController]
[Route("api/public")]
public class PublicController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IGeoLocationService _geoService;
    private readonly IEmailService _emailService;
    private readonly IPdfService _pdfService;
    private readonly ILogger<PublicController> _logger;

    public PublicController(
        ApplicationDbContext context,
        IGeoLocationService geoService,
        IEmailService emailService,
        IPdfService pdfService,
        ILogger<PublicController> logger)
    {
        _context = context;
        _geoService = geoService;
        _emailService = emailService;
        _pdfService = pdfService;
        _logger = logger;
    }

    [HttpGet("geo-detect")]
    public async Task<IActionResult> DetectCountry()
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        // For local development
        if (ipAddress == "::1" || ipAddress == "127.0.0.1" || ipAddress == "unknown")
        {
            ipAddress = ""; // Will default to external IP detection
        }

        var (countryCode, countryName) = await _geoService.GetCountryFromIp(ipAddress);
        var isSupported = _geoService.IsSupportedCountry(countryCode);

        return Ok(new
        {
            countryCode,
            countryName,
            isSupported,
            supportedCountries = new[] { "ZA", "GB", "IE" }
        });
    }

    [HttpPost("waitlist")]
    public async Task<IActionResult> JoinWaitlist([FromBody] WaitlistDto data)
    {
        var result = await _emailService.AddToWaitlist(data.Email, data.Country, data.CountryCode);

        if (result)
            return Ok(new { message = "Successfully added to waitlist" });
        else
            return StatusCode(500, new { message = "Failed to add to waitlist" });
    }

    [HttpGet("invoice/{id}")]
    public async Task<IActionResult> GetInvoiceDetails(Guid id)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Merchant)
            .Include(i => i.Client)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null)
            return NotFound();

        // Update status to Viewed if it was Sent
        if (invoice.Status == "Sent")
        {
            invoice.Status = "Viewed";
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            invoice = new
            {
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.InvoiceDate,
                invoice.DueDate,
                invoice.TotalAmount,
                invoice.Currency,
                invoice.Notes,
                invoice.Status,
                invoice.PaymentRefCode,
                invoice.PaidDate,
                invoice.PaymentMethod,
                invoice.PaymentReference
            },
            merchant = new
            {
                invoice.Merchant.BusinessName,
                invoice.Merchant.LogoUrl,
                invoice.Merchant.ContactNumber,
                invoice.Merchant.Address,
                invoice.Merchant.Country,
                invoice.Merchant.BankName,
                invoice.Merchant.AccountNumber,
                invoice.Merchant.BranchCode,
                invoice.Merchant.AccountHolderName,
                invoice.Merchant.BankIban,
                invoice.Merchant.BankBic,
                PayFastEnabled = invoice.Merchant.PayFastEnabled,
                PaystackEnabled = invoice.Merchant.PaystackEnabled,
                OzowEnabled = invoice.Merchant.OzowEnabled,
                StripeEnabled = invoice.Merchant.StripeEnabled,
                PayPalEnabled = invoice.Merchant.PayPalEnabled
            },
            client = new
            {
                invoice.Client.Name,
                invoice.Client.Email
            },
            lineItems = invoice.LineItems.Select(li => new
            {
                li.Description,
                li.Quantity,
                li.UnitPrice,
                li.Total
            })
        });
    }

    [HttpGet("statement/{clientId}")]
    public async Task<IActionResult> GetStatement(Guid clientId)
    {
        var client = await _context.Clients
            .Include(c => c.Merchant)
            .Include(c => c.Invoices)
                .ThenInclude(i => i.LineItems)
            .FirstOrDefaultAsync(c => c.Id == clientId);

        if (client == null || !client.Merchant.StatementsEnabled)
            return NotFound();

        var invoices = client.Invoices
            .OrderByDescending(i => i.InvoiceDate)
            .Select(i => new
            {
                i.InvoiceNumber,
                i.InvoiceDate,
                i.DueDate,
                i.TotalAmount,
                i.Status,
                i.PaidDate
            })
            .ToList();

        var totalOutstanding = client.Invoices
            .Where(i => i.Status != "Paid")
            .Sum(i => i.TotalAmount);

        return Ok(new
        {
            client = new
            {
                client.Name,
                client.CompanyName
            },
            merchant = new
            {
                client.Merchant.BusinessName,
                client.Merchant.LogoUrl,
                client.Merchant.ContactNumber,
                client.Merchant.Currency
            },
            statement = new
            {
                generatedDate = DateTime.UtcNow,
                totalOutstanding,
                invoices,
                currency = client.Merchant.Currency
            }
        });
    }

    [HttpGet("invoice/{id}/pdf")]
    public async Task<IActionResult> DownloadInvoicePdf(Guid id)
    {
        var exists = await _context.Invoices.AnyAsync(i => i.Id == id);
        if (!exists)
            return NotFound();

        try
        {
            var pdf = await _pdfService.GenerateInvoicePdf(id);
            return File(pdf, "application/pdf", $"invoice-{id}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating public invoice PDF for {InvoiceId}", id);
            return StatusCode(500, new { message = "Failed to generate PDF" });
        }
    }

    [HttpGet("invoice/{id}/receipt/pdf")]
    public async Task<IActionResult> DownloadReceiptPdf(Guid id)
    {
        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == id);
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
            _logger.LogError(ex, "Error generating public receipt PDF for {InvoiceId}", id);
            return StatusCode(500, new { message = "Failed to generate PDF" });
        }
    }

    [HttpGet("pricing/{country}")]
    public IActionResult GetPricing(string country)
    {
        // Pricing rationale:
        //   ZA: R89/mo — undercuts Rebill (R99) and WhatsInvoicing (R99), the two direct SA competitors
        //   GB: £8/mo  — below FreshBooks Lite (£15 regular) and Xero Ignite (£16 regular); no WhatsApp invoicing
        //               competitor exists in the UK market
        //   IE: €9/mo  — above free tools (Conta €7.99 annual-only), well below Sage (€17+) and Xero
        // Annual discount ~11% for ZA (R799 vs R1,068), ~22% for GB (£75 vs £96), ~21% for IE (€85 vs €108)
        // These are intentional "launch / early-adopter" rates — expect to raise 12-18 months post-launch.

        var pricing = country.ToUpperInvariant() switch
        {
            "ZA" => new
            {
                country = "South Africa",
                currency = "ZAR",
                symbol = "R",
                price_monthly = 129,
                price_annual = 1290,
                price_annual_monthly_equiv = 108,
                annual_saving_pct = 17,
                trial_days = 14,
                subscription_provider = "paystack",
                competitor_note = "Built for WhatsApp-first collections, below QuickBooks and Xero"
            },
            "GB" => new
            {
                country = "United Kingdom",
                currency = "GBP",
                symbol = "£",
                price_monthly = 9,
                price_annual = 90,
                price_annual_monthly_equiv = 8,
                annual_saving_pct = 17,
                trial_days = 14,
                subscription_provider = "paypal",
                competitor_note = "WhatsApp-first invoicing at a fraction of full accounting suite pricing"
            },
            "IE" => new
            {
                country = "Ireland",
                currency = "EUR",
                symbol = "€",
                price_monthly = 10,
                price_annual = 100,
                price_annual_monthly_equiv = 8,
                annual_saving_pct = 17,
                trial_days = 14,
                subscription_provider = "paypal",
                competitor_note = "Simpler than accounting suites, priced for owner-managed businesses"
            },
            _ => new
            {
                country = "South Africa",
                currency = "ZAR",
                symbol = "R",
                price_monthly = 129,
                price_annual = 1290,
                price_annual_monthly_equiv = 108,
                annual_saving_pct = 17,
                trial_days = 14,
                subscription_provider = "paystack",
                competitor_note = "Built for WhatsApp-first collections, below QuickBooks and Xero"
            }
        };

        return Ok(pricing);
    }
}

public class WaitlistDto
{
    public string Email { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}
