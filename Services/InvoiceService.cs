using BlueSquares.Data;
using BlueSquares.Models;
using Microsoft.EntityFrameworkCore;

namespace BlueSquares.Services;

public class InvoiceService : IInvoiceService
{
    private readonly ApplicationDbContext _context;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IEmailService _emailService;

    public InvoiceService(
        ApplicationDbContext context,
        IWhatsAppService whatsAppService,
        IEmailService emailService)
    {
        _context = context;
        _whatsAppService = whatsAppService;
        _emailService = emailService;
    }

    public async Task<InvoiceCreationResult> CreateInvoiceAsync(Guid merchantId, InvoiceCreationRequest request)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId)
            ?? throw new InvalidOperationException("Merchant not found.");

        var clientExists = await _context.Clients.AnyAsync(c => c.Id == request.ClientId && c.MerchantId == merchantId);
        if (!clientExists)
            throw new InvalidOperationException("Client not found.");

        var invoiceNumber = string.IsNullOrWhiteSpace(request.InvoiceNumber)
            ? await GenerateInvoiceNumberAsync(merchantId)
            : request.InvoiceNumber.Trim();

        var invoiceDate = request.InvoiceDate?.ToUniversalTime() ?? DateTime.UtcNow;
        var dueDate = request.DueDate?.ToUniversalTime() ?? invoiceDate.AddDays(7);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            ClientId = request.ClientId,
            RecurringInvoiceScheduleId = request.RecurringInvoiceScheduleId,
            InvoiceNumber = invoiceNumber,
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            Currency = merchant.Currency,
            Notes = request.Notes,
            Status = "Draft",
            PaymentRefCode = GeneratePaymentReference(invoiceNumber),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Invoices.Add(invoice);

        if (request.LineItems.Any())
        {
            foreach (var item in request.LineItems)
            {
                _context.InvoiceLineItems.Add(new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice
                });
            }

            invoice.TotalAmount = request.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        }
        else
        {
            invoice.TotalAmount = request.TotalAmount;
        }

        await _context.SaveChangesAsync();

        return new InvoiceCreationResult
        {
            InvoiceId = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber
        };
    }

    public async Task<bool> SendInvoiceAsync(Guid invoiceId, bool sendEmail, bool sendWhatsApp = true)
    {
        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (invoice == null)
            return false;

        invoice.Status = "Sent";
        invoice.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var whatsappSent = false;
        if (sendWhatsApp)
            whatsappSent = await _whatsAppService.SendInvoiceMessage(invoiceId);

        if (sendEmail)
            await _emailService.SendInvoiceEmail(invoiceId);

        return whatsappSent || sendEmail;
    }

    private async Task<string> GenerateInvoiceNumberAsync(Guid merchantId)
    {
        var count = await _context.Invoices.CountAsync(i => i.MerchantId == merchantId);
        return $"INV-{count + 1:D4}";
    }

    private static string GeneratePaymentReference(string invoiceNumber)
    {
        return $"{invoiceNumber}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
    }
}
