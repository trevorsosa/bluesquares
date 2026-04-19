using BlueSquares.Data;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BlueSquares.Services;

public class AccountingIntegrationService : IAccountingIntegrationService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AccountingIntegrationService> _logger;

    public AccountingIntegrationService(
        ApplicationDbContext context,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<AccountingIntegrationService> logger)
    {
        _context = context;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<AccountingIntegrationStatusDto> GetStatusAsync(Guid merchantId)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId)
            ?? throw new InvalidOperationException("Merchant not found.");

        return new AccountingIntegrationStatusDto
        {
            XeroConnected = merchant.XeroEnabled && !string.IsNullOrWhiteSpace(merchant.XeroAccessToken),
            QuickBooksConnected = merchant.QuickBooksEnabled && !string.IsNullOrWhiteSpace(merchant.QuickBooksAccessToken),
            XeroConnectedAt = merchant.XeroConnectedAt,
            QuickBooksConnectedAt = merchant.QuickBooksConnectedAt,
            XeroLastSyncAt = merchant.XeroLastSyncAt,
            QuickBooksLastSyncAt = merchant.QuickBooksLastSyncAt
        };
    }

    public async Task<string> GetAuthorizationUrlAsync(Guid merchantId, string provider)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId)
            ?? throw new InvalidOperationException("Merchant not found.");

        provider = provider.ToLowerInvariant();
        var state = merchant.Id.ToString();

        return provider switch
        {
            "xero" => BuildXeroAuthorizationUrl(state),
            "quickbooks" => BuildQuickBooksAuthorizationUrl(state),
            _ => throw new InvalidOperationException("Unsupported accounting provider.")
        };
    }

    public async Task<bool> HandleCallbackAsync(string provider, string code, string state, string? realmId = null)
    {
        if (!Guid.TryParse(state, out var merchantId))
            return false;

        return provider.ToLowerInvariant() switch
        {
            "xero" => await HandleXeroCallbackAsync(merchantId, code),
            "quickbooks" => await HandleQuickBooksCallbackAsync(merchantId, code, realmId),
            _ => false
        };
    }

    public async Task<bool> ExportInvoiceAsync(Guid merchantId, Guid invoiceId, string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "xero" => await ExportInvoiceToXeroAsync(merchantId, invoiceId),
            "quickbooks" => await ExportInvoiceToQuickBooksAsync(merchantId, invoiceId),
            _ => false
        };
    }

    private string BuildXeroAuthorizationUrl(string state)
    {
        var clientId = _configuration["AccountingIntegrations:Xero:ClientId"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Contains("YOUR_"))
            throw new InvalidOperationException("Xero client ID is not configured.");

        var redirectUri = GetRedirectUri("xero");
        var scope = Uri.EscapeDataString("openid profile email accounting.transactions accounting.contacts offline_access");

        return $"https://login.xero.com/identity/connect/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={scope}&state={Uri.EscapeDataString(state)}";
    }

    private string BuildQuickBooksAuthorizationUrl(string state)
    {
        var clientId = _configuration["AccountingIntegrations:QuickBooks:ClientId"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Contains("YOUR_"))
            throw new InvalidOperationException("QuickBooks client ID is not configured.");

        var redirectUri = GetRedirectUri("quickbooks");
        var scope = Uri.EscapeDataString("com.intuit.quickbooks.accounting");

        return $"https://appcenter.intuit.com/connect/oauth2?client_id={Uri.EscapeDataString(clientId)}&response_type=code&scope={scope}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";
    }

    private async Task<bool> HandleXeroCallbackAsync(Guid merchantId, string code)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null)
            return false;

        var clientId = _configuration["AccountingIntegrations:Xero:ClientId"] ?? string.Empty;
        var clientSecret = _configuration["AccountingIntegrations:Xero:ClientSecret"] ?? string.Empty;
        if (clientId.Contains("YOUR_") || clientSecret.Contains("YOUR_"))
            return false;

        var httpClient = _httpClientFactory.CreateClient();
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://identity.xero.com/connect/token");
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = GetRedirectUri("xero")
        });

        var tokenResponse = await httpClient.SendAsync(tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
            return false;

        var tokenData = JsonSerializer.Deserialize<JsonElement>(await tokenResponse.Content.ReadAsStringAsync());
        var accessToken = tokenData.GetProperty("access_token").GetString();
        var refreshToken = tokenData.GetProperty("refresh_token").GetString();
        var expiresIn = tokenData.GetProperty("expires_in").GetInt32();

        using var connectionsRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.xero.com/connections");
        connectionsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var connectionsResponse = await httpClient.SendAsync(connectionsRequest);
        if (!connectionsResponse.IsSuccessStatusCode)
            return false;

        var connections = JsonSerializer.Deserialize<JsonElement>(await connectionsResponse.Content.ReadAsStringAsync());
        var tenantId = connections.EnumerateArray().FirstOrDefault().GetProperty("tenantId").GetString();

        merchant.XeroEnabled = true;
        merchant.XeroTenantId = tenantId;
        merchant.XeroAccessToken = accessToken;
        merchant.XeroRefreshToken = refreshToken;
        merchant.XeroTokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
        merchant.XeroConnectedAt = DateTime.UtcNow;
        merchant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    private async Task<bool> HandleQuickBooksCallbackAsync(Guid merchantId, string code, string? realmId)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null)
            return false;

        var clientId = _configuration["AccountingIntegrations:QuickBooks:ClientId"] ?? string.Empty;
        var clientSecret = _configuration["AccountingIntegrations:QuickBooks:ClientSecret"] ?? string.Empty;
        if (clientId.Contains("YOUR_") || clientSecret.Contains("YOUR_"))
            return false;

        var httpClient = _httpClientFactory.CreateClient();
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer");
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = GetRedirectUri("quickbooks")
        });

        var tokenResponse = await httpClient.SendAsync(tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
            return false;

        var tokenData = JsonSerializer.Deserialize<JsonElement>(await tokenResponse.Content.ReadAsStringAsync());
        merchant.QuickBooksEnabled = true;
        merchant.QuickBooksRealmId = realmId;
        merchant.QuickBooksAccessToken = tokenData.GetProperty("access_token").GetString();
        merchant.QuickBooksRefreshToken = tokenData.GetProperty("refresh_token").GetString();
        merchant.QuickBooksTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenData.GetProperty("expires_in").GetInt32());
        merchant.QuickBooksConnectedAt = DateTime.UtcNow;
        merchant.QuickBooksEnvironment = (_configuration["AccountingIntegrations:QuickBooks:Environment"] ?? "sandbox").ToLowerInvariant();
        merchant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    private async Task<bool> ExportInvoiceToXeroAsync(Guid merchantId, Guid invoiceId)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null || !merchant.XeroEnabled || string.IsNullOrWhiteSpace(merchant.XeroTenantId))
            return false;

        await EnsureXeroTokenAsync(merchant);

        var invoice = await LoadInvoiceAsync(merchantId, invoiceId);
        var payload = new
        {
            Type = "ACCREC",
            Contact = new
            {
                Name = invoice.Client.CompanyName ?? invoice.Client.Name,
                EmailAddress = invoice.Client.Email
            },
            Date = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
            DueDate = invoice.DueDate.ToString("yyyy-MM-dd"),
            InvoiceNumber = invoice.InvoiceNumber,
            Reference = invoice.PaymentRefCode,
            Status = "AUTHORISED",
            LineItems = invoice.LineItems.Select(item => new
            {
                Description = item.Description,
                Quantity = item.Quantity,
                UnitAmount = item.UnitPrice
            }).ToArray()
        };

        var httpClient = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.xero.com/api.xro/2.0/Invoices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", merchant.XeroAccessToken);
        request.Headers.Add("Xero-tenant-id", merchant.XeroTenantId);
        request.Content = new StringContent(JsonSerializer.Serialize(new { Invoices = new[] { payload } }), Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Xero export failed for merchant {MerchantId} invoice {InvoiceId}", merchantId, invoiceId);
            return false;
        }

        merchant.XeroLastSyncAt = DateTime.UtcNow;
        merchant.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    private async Task<bool> ExportInvoiceToQuickBooksAsync(Guid merchantId, Guid invoiceId)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null || !merchant.QuickBooksEnabled || string.IsNullOrWhiteSpace(merchant.QuickBooksRealmId))
            return false;

        await EnsureQuickBooksTokenAsync(merchant);

        var invoice = await LoadInvoiceAsync(merchantId, invoiceId);
        var customerId = await EnsureQuickBooksCustomerAsync(merchant, invoice.Client);
        if (string.IsNullOrWhiteSpace(customerId))
            return false;

        var baseUrl = merchant.QuickBooksEnvironment == "production"
            ? "https://quickbooks.api.intuit.com"
            : "https://sandbox-quickbooks.api.intuit.com";

        var payload = new
        {
            CustomerRef = new { value = customerId },
            TxnDate = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
            DueDate = invoice.DueDate.ToString("yyyy-MM-dd"),
            PrivateNote = invoice.Notes,
            CustomerMemo = new { value = $"Invoice {invoice.InvoiceNumber}" },
            Line = invoice.LineItems.Select(item => new
            {
                DetailType = "SalesItemLineDetail",
                Amount = item.Total,
                Description = item.Description,
                SalesItemLineDetail = new
                {
                    Qty = item.Quantity,
                    UnitPrice = item.UnitPrice
                }
            }).ToArray()
        };

        var httpClient = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v3/company/{merchant.QuickBooksRealmId}/invoice");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", merchant.QuickBooksAccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("QuickBooks export failed for merchant {MerchantId} invoice {InvoiceId}", merchantId, invoiceId);
            return false;
        }

        merchant.QuickBooksLastSyncAt = DateTime.UtcNow;
        merchant.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    private async Task<Models.Invoice> LoadInvoiceAsync(Guid merchantId, Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Client)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.MerchantId == merchantId);

        return invoice ?? throw new InvalidOperationException("Invoice not found.");
    }

    private async Task EnsureXeroTokenAsync(Models.Merchant merchant)
    {
        if (!merchant.XeroTokenExpiresAt.HasValue || merchant.XeroTokenExpiresAt > DateTime.UtcNow.AddMinutes(5))
            return;

        var clientId = _configuration["AccountingIntegrations:Xero:ClientId"] ?? string.Empty;
        var clientSecret = _configuration["AccountingIntegrations:Xero:ClientSecret"] ?? string.Empty;
        var httpClient = _httpClientFactory.CreateClient();

        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://identity.xero.com/connect/token");
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = merchant.XeroRefreshToken ?? string.Empty
        });

        var response = await httpClient.SendAsync(tokenRequest);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Unable to refresh Xero token.");

        var data = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        merchant.XeroAccessToken = data.GetProperty("access_token").GetString();
        merchant.XeroRefreshToken = data.GetProperty("refresh_token").GetString();
        merchant.XeroTokenExpiresAt = DateTime.UtcNow.AddSeconds(data.GetProperty("expires_in").GetInt32());
        merchant.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task EnsureQuickBooksTokenAsync(Models.Merchant merchant)
    {
        if (!merchant.QuickBooksTokenExpiresAt.HasValue || merchant.QuickBooksTokenExpiresAt > DateTime.UtcNow.AddMinutes(5))
            return;

        var clientId = _configuration["AccountingIntegrations:QuickBooks:ClientId"] ?? string.Empty;
        var clientSecret = _configuration["AccountingIntegrations:QuickBooks:ClientSecret"] ?? string.Empty;
        var httpClient = _httpClientFactory.CreateClient();

        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer");
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = merchant.QuickBooksRefreshToken ?? string.Empty
        });

        var response = await httpClient.SendAsync(tokenRequest);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Unable to refresh QuickBooks token.");

        var data = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        merchant.QuickBooksAccessToken = data.GetProperty("access_token").GetString();
        merchant.QuickBooksRefreshToken = data.GetProperty("refresh_token").GetString();
        merchant.QuickBooksTokenExpiresAt = DateTime.UtcNow.AddSeconds(data.GetProperty("expires_in").GetInt32());
        merchant.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task<string?> EnsureQuickBooksCustomerAsync(Models.Merchant merchant, Models.Client client)
    {
        var baseUrl = merchant.QuickBooksEnvironment == "production"
            ? "https://quickbooks.api.intuit.com"
            : "https://sandbox-quickbooks.api.intuit.com";

        var payload = new
        {
            DisplayName = client.CompanyName ?? client.Name,
            FullyQualifiedName = client.CompanyName ?? client.Name,
            PrimaryEmailAddr = string.IsNullOrWhiteSpace(client.Email) ? null : new { Address = client.Email }
        };

        var httpClient = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v3/company/{merchant.QuickBooksRealmId}/customer");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", merchant.QuickBooksAccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        var data = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        return data.GetProperty("Customer").GetProperty("Id").GetString();
    }

    private string GetRedirectUri(string provider)
    {
        var providerSection = provider.Equals("xero", StringComparison.OrdinalIgnoreCase) ? "Xero" : "QuickBooks";
        var configured = _configuration[$"AccountingIntegrations:{providerSection}:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(configured) && !configured.Contains("YOUR_"))
            return configured;

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        return $"{baseUrl}/api/accounting-integrations/{provider}/callback";
    }
}
