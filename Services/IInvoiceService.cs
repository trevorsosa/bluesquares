namespace BlueSquares.Services;

public interface IInvoiceService
{
    Task<InvoiceCreationResult> CreateInvoiceAsync(Guid merchantId, InvoiceCreationRequest request);
    Task<bool> SendInvoiceAsync(Guid invoiceId, bool sendEmail, bool sendWhatsApp = true);
}

public class InvoiceCreationRequest
{
    public Guid ClientId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public Guid? RecurringInvoiceScheduleId { get; set; }
    public List<InvoiceLineItemRequest> LineItems { get; set; } = new();
}

public class InvoiceLineItemRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}

public class InvoiceCreationResult
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
}
