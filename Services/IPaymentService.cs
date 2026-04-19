namespace BlueSquares.Services;

public interface IPaymentService
{
    // ── South Africa ──────────────────────────────────────────────────────────
    Task<string> GeneratePayFastUrl(Guid invoiceId);
    Task<string> GeneratePaystackUrl(Guid invoiceId);
    Task<string> GenerateOzowUrl(Guid invoiceId);
    Task<bool> HandlePayFastWebhook(Dictionary<string, string> data);
    Task<bool> HandlePaystackWebhook(string payload, string signature);
    Task<bool> HandleOzowWebhook(Dictionary<string, string> data);

    // ── United Kingdom & Ireland ──────────────────────────────────────────────
    Task<string> GenerateStripeUrl(Guid invoiceId);
    Task<string> GeneratePayPalInvoiceUrl(Guid invoiceId);
    Task<bool> HandleStripeWebhook(string payload, string signature);
    Task<bool> HandlePayPalWebhook(string payload);
}
