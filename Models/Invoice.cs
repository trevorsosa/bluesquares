namespace BlueSquares.Models;

public class Invoice
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? RecurringInvoiceScheduleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;
    public DateTime DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string? Notes { get; set; }
    
    // Status: Draft, Sent, Viewed, Paid, Overdue, Disputed
    public string Status { get; set; } = "Draft";
    
    // Payment tracking
    public DateTime? PaidDate { get; set; }
    public string? PaymentMethod { get; set; } // PayFast, Paystack, Ozow, EFT, Cash
    public string? PaymentReference { get; set; }
    public string? PaymentTransactionId { get; set; }
    
    // Promise to pay tracking
    public DateTime? PromisedPaymentDate { get; set; }
    public string? PromisedPaymentNote { get; set; }
    
    // Unique payment reference for EFT
    public string PaymentRefCode { get; set; } = string.Empty; // e.g., INV-1023-TREVOR
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastReminderSentAt { get; set; }
    public int ReminderCount { get; set; } = 0;
    
    // Navigation properties
    public Merchant Merchant { get; set; } = null!;
    public Client Client { get; set; } = null!;
    public RecurringInvoiceSchedule? RecurringInvoiceSchedule { get; set; }
    public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();
    public ICollection<InvoiceQuery> Queries { get; set; } = new List<InvoiceQuery>();
}
