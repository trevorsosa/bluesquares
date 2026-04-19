namespace BlueSquares.Models;

public class RecurringInvoiceSchedule
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Frequency { get; set; } = "monthly";
    public int DayOfMonth { get; set; } = 1;
    public int DueDaysAfterIssue { get; set; } = 7;
    public string Currency { get; set; } = "ZAR";
    public string? Notes { get; set; }
    public bool AutoSendWhatsApp { get; set; } = true;
    public bool AutoSendEmail { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime NextRunDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? LastRunDate { get; set; }
    public Guid? LastGeneratedInvoiceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Merchant Merchant { get; set; } = null!;
    public Client Client { get; set; } = null!;
    public Invoice? LastGeneratedInvoice { get; set; }
    public ICollection<RecurringInvoiceLineItem> LineItems { get; set; } = new List<RecurringInvoiceLineItem>();
}
