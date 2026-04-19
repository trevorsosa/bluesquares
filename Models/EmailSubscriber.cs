namespace BlueSquares.Models;

public class EmailSubscriber
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
    public bool NotificationSent { get; set; } = false;
}
