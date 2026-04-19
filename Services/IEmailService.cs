namespace BlueSquares.Services;

public interface IEmailService
{
    Task<bool> SendInvoiceEmail(Guid invoiceId);
    Task<bool> SendReceiptEmail(Guid invoiceId);
    Task<bool> SendWelcomeEmail(string merchantEmail, string businessName);
    Task<bool> AddToWaitlist(string email, string country, string countryCode);
}
