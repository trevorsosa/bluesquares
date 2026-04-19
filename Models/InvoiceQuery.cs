namespace BlueSquares.Models;

public class InvoiceQuery
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public string? MerchantResponse { get; set; }
    public bool IsResolved { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    
    // Navigation properties
    public Invoice Invoice { get; set; } = null!;
}
