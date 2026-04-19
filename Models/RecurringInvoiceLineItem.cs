namespace BlueSquares.Models;

public class RecurringInvoiceLineItem
{
    public Guid Id { get; set; }
    public Guid RecurringInvoiceScheduleId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Total => Quantity * UnitPrice;

    public RecurringInvoiceSchedule RecurringInvoiceSchedule { get; set; } = null!;
}
