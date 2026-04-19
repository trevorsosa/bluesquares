namespace BlueSquares.Models;

public class ReminderSchedule
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public int DaysBeforeDue { get; set; } = -1; // -1 = day before, 0 = on due date
    public int DaysAfterDue { get; set; } = 0;
    public bool Enabled { get; set; } = true;
    
    // Navigation properties
    public Merchant Merchant { get; set; } = null!;
}
