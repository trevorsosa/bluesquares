namespace BlueSquares.Services;

public interface IAccountingIntegrationService
{
    Task<AccountingIntegrationStatusDto> GetStatusAsync(Guid merchantId);
    Task<string> GetAuthorizationUrlAsync(Guid merchantId, string provider);
    Task<bool> HandleCallbackAsync(string provider, string code, string state, string? realmId = null);
    Task<bool> ExportInvoiceAsync(Guid merchantId, Guid invoiceId, string provider);
}

public class AccountingIntegrationStatusDto
{
    public bool XeroConnected { get; set; }
    public bool QuickBooksConnected { get; set; }
    public DateTime? XeroConnectedAt { get; set; }
    public DateTime? QuickBooksConnectedAt { get; set; }
    public DateTime? XeroLastSyncAt { get; set; }
    public DateTime? QuickBooksLastSyncAt { get; set; }
}
