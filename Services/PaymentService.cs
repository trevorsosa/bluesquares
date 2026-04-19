using BlueSquares.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlueSquares.Services;

public class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentService> _logger;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IEmailService _emailService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly HttpClient _httpClient;

    public PaymentService(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<PaymentService> logger,
        IWhatsAppService whatsAppService,
        IEmailService emailService,
        ISubscriptionService subscriptionService,
        IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _whatsAppService = whatsAppService;
        _emailService = emailService;
        _subscriptionService = subscriptionService;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<string> GeneratePayFastUrl(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Merchant)
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null || !invoice.Merchant.PayFastEnabled)
            return string.Empty;

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var merchantId = invoice.Merchant.PayFastMerchantId;
        var merchantKey = invoice.Merchant.PayFastMerchantKey;

        // Build PayFast payment data
        var paymentData = new Dictionary<string, string>
        {
            { "merchant_id", merchantId ?? "" },
            { "merchant_key", merchantKey ?? "" },
            { "return_url", $"{baseUrl}/payment/success" },
            { "cancel_url", $"{baseUrl}/payment/cancel" },
            { "notify_url", $"{baseUrl}/api/webhooks/payfast" },
            { "name_first", invoice.Client.Name },
            { "email_address", invoice.Client.Email ?? "noreply@squares.blue" },
            { "m_payment_id", invoice.Id.ToString() },
            { "amount", invoice.TotalAmount.ToString("F2") },
            { "item_name", $"Invoice {invoice.InvoiceNumber}" },
            { "item_description", $"Payment for invoice {invoice.InvoiceNumber}" }
        };

        // Generate signature
        var signature = GeneratePayFastSignature(paymentData, merchantKey ?? "");
        paymentData.Add("signature", signature);

        // Build query string
        var queryString = string.Join("&", paymentData.Select(kvp => 
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var payfastUrl = _configuration["PayFast:Sandbox"] == "true"
            ? "https://sandbox.payfast.co.za/eng/process"
            : "https://www.payfast.co.za/eng/process";

        return $"{payfastUrl}?{queryString}";
    }

    public async Task<string> GeneratePaystackUrl(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Merchant)
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null || !invoice.Merchant.PaystackEnabled)
            return string.Empty;

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var publicKey = invoice.Merchant.PaystackPublicKey;

        // Amount in kobo/cents (Paystack uses smallest currency unit)
        var amountInMinorUnits = (int)(invoice.TotalAmount * 100);

        var initializeData = new
        {
            email = invoice.Client.Email ?? "noreply@squares.blue",
            amount = amountInMinorUnits,
            currency = invoice.Currency,
            reference = $"INV-{invoice.Id}",
            callback_url = $"{baseUrl}/payment/success",
            metadata = new
            {
                invoice_id = invoice.Id.ToString(),
                invoice_number = invoice.InvoiceNumber,
                merchant_id = invoice.MerchantId.ToString()
            }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.paystack.co/transaction/initialize");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", invoice.Merchant.PaystackSecretKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(initializeData),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseData = await JsonSerializer.DeserializeAsync<JsonElement>(
                    await response.Content.ReadAsStreamAsync());

                var authorizationUrl = responseData
                    .GetProperty("data")
                    .GetProperty("authorization_url")
                    .GetString();

                return authorizationUrl ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Paystack URL");
        }

        return string.Empty;
    }

    public async Task<string> GenerateOzowUrl(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Merchant)
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null || !invoice.Merchant.OzowEnabled)
            return string.Empty;

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var siteCode = invoice.Merchant.OzowSiteCode;
        var privateKey = invoice.Merchant.OzowPrivateKey;

        var transactionData = new Dictionary<string, string>
        {
            { "SiteCode", siteCode ?? "" },
            { "CountryCode", invoice.Merchant.Country },
            { "CurrencyCode", invoice.Currency },
            { "Amount", invoice.TotalAmount.ToString("F2") },
            { "TransactionReference", invoice.Id.ToString() },
            { "BankReference", invoice.InvoiceNumber },
            { "CancelUrl", $"{baseUrl}/payment/cancel" },
            { "ErrorUrl", $"{baseUrl}/payment/error" },
            { "SuccessUrl", $"{baseUrl}/payment/success" },
            { "NotifyUrl", $"{baseUrl}/api/webhooks/ozow" },
            { "Customer", invoice.Client.Name },
            { "IsTest", _configuration["Ozow:IsTest"] == "true" ? "true" : "false" }
        };

        // Generate Ozow hash
        var hashString = string.Join("", transactionData.OrderBy(x => x.Key.ToLower()).Select(x => x.Value));
        hashString += privateKey;
        
        using var sha512 = SHA512.Create();
        var hashBytes = sha512.ComputeHash(Encoding.UTF8.GetBytes(hashString));
        var hashCheck = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

        transactionData.Add("HashCheck", hashCheck);

        var queryString = string.Join("&", transactionData.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var ozowUrl = _configuration["Ozow:IsTest"] == "true"
            ? "https://testpay.ozow.com"
            : "https://pay.ozow.com";

        return $"{ozowUrl}?{queryString}";
    }

    public async Task<bool> HandlePayFastWebhook(Dictionary<string, string> data)
    {
        try
        {
            // Verify signature - get merchant key from invoice
            var signature = data.ContainsKey("signature") ? data["signature"] : "";
            var dataCopy = new Dictionary<string, string>(data);
            dataCopy.Remove("signature");

            if (!dataCopy.TryGetValue("m_payment_id", out var mPaymentId) || !Guid.TryParse(mPaymentId, out var invoiceId))
                return false;

            var invoice = await _context.Invoices
                .Include(i => i.Merchant)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);
            var merchantKey = invoice?.Merchant?.PayFastMerchantKey ?? "";
            var generatedSignature = GeneratePayFastSignature(dataCopy, merchantKey);

            if (signature != generatedSignature)
            {
                _logger.LogWarning("PayFast webhook signature mismatch");
                return false;
            }

            // Get payment status
            if (!data.TryGetValue("payment_status", out var paymentStatus) || paymentStatus != "COMPLETE")
                return false;

            // Invoice already loaded above for signature verification
            if (invoice == null || invoice.Status == "Paid")
                return false;

            // Mark as paid
            invoice.Status = "Paid";
            invoice.PaidDate = DateTime.UtcNow;
            invoice.PaymentMethod = "PayFast";
            invoice.PaymentTransactionId = data.ContainsKey("pf_payment_id") ? data["pf_payment_id"] : null;
            invoice.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Send receipt
            await _whatsAppService.SendReceiptMessage(invoiceId);
            await _emailService.SendReceiptEmail(invoiceId);

            _logger.LogInformation($"Invoice {invoice.InvoiceNumber} marked as paid via PayFast");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling PayFast webhook");
            return false;
        }
    }

    public async Task<bool> HandlePaystackWebhook(string payload, string signature)
    {
        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(payload);
            var eventType = data.GetProperty("event").GetString();
            var eventData = data.GetProperty("data");
            var metadata = eventData.TryGetProperty("metadata", out var meta) ? meta : default;
            var merchantId = Guid.Empty;
            var countryCode = "ZA";

            if (metadata.ValueKind != JsonValueKind.Undefined &&
                metadata.TryGetProperty("merchant_id", out var merchantIdValue))
            {
                Guid.TryParse(merchantIdValue.GetString(), out merchantId);
            }

            string secretKey;
            if (merchantId != Guid.Empty)
            {
                var merchant = await _context.Merchants.FindAsync(merchantId);
                if (merchant == null)
                    return false;

                countryCode = merchant.Country;
                secretKey = merchant.PaystackSecretKey
                    ?? _configuration[$"Paystack:SecretKey:{merchant.Country}"]
                    ?? _configuration["Paystack:SecretKey"]
                    ?? string.Empty;
            }
            else
            {
                var lookupReference = eventData.TryGetProperty("reference", out var refProp) ? refProp.GetString() : null;
                if (!string.IsNullOrWhiteSpace(lookupReference) && lookupReference.StartsWith("INV-") &&
                    Guid.TryParse(lookupReference.Replace("INV-", ""), out var invoiceIdForLookup))
                {
                    var invoiceForLookup = await _context.Invoices
                        .Include(i => i.Merchant)
                        .FirstOrDefaultAsync(i => i.Id == invoiceIdForLookup);
                    if (invoiceForLookup == null)
                        return false;

                    merchantId = invoiceForLookup.MerchantId;
                    countryCode = invoiceForLookup.Merchant.Country;
                    secretKey = invoiceForLookup.Merchant.PaystackSecretKey
                        ?? _configuration[$"Paystack:SecretKey:{invoiceForLookup.Merchant.Country}"]
                        ?? _configuration["Paystack:SecretKey"]
                        ?? string.Empty;
                }
                else
                {
                    secretKey = _configuration["Paystack:SecretKey"] ?? string.Empty;
                }
            }

            var hash = ComputeHmacSha512(payload, secretKey);
            if (!string.Equals(hash, signature, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Paystack webhook signature mismatch");
                return false;
            }

            if (eventType != "charge.success")
                return true;

            var reference = eventData.GetProperty("reference").GetString();
            if (metadata.ValueKind != JsonValueKind.Undefined &&
                metadata.TryGetProperty("action", out var actionProp) &&
                actionProp.GetString() == "subscription_upgrade" &&
                merchantId != Guid.Empty)
            {
                var billingCycle = metadata.TryGetProperty("billing_cycle", out var cycleProp)
                    ? cycleProp.GetString() ?? "monthly"
                    : "monthly";
                await _subscriptionService.UpgradeToPro(merchantId, reference ?? $"paystack-{countryCode}-{DateTime.UtcNow:yyyyMMddHHmmss}", billingCycle);
                _logger.LogInformation("Merchant {MerchantId} upgraded via Paystack webhook", merchantId);
                return true;
            }

            if (string.IsNullOrEmpty(reference) || !reference.StartsWith("INV-"))
                return false;

            var invoiceId = Guid.Parse(reference.Replace("INV-", ""));
            var invoice = await _context.Invoices.FindAsync(invoiceId);

            if (invoice == null || invoice.Status == "Paid")
                return false;

            // Mark as paid
            invoice.Status = "Paid";
            invoice.PaidDate = DateTime.UtcNow;
            invoice.PaymentMethod = "Paystack";
            invoice.PaymentReference = reference;
            invoice.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Send receipt
            await _whatsAppService.SendReceiptMessage(invoiceId);
            await _emailService.SendReceiptEmail(invoiceId);

            _logger.LogInformation($"Invoice {invoice.InvoiceNumber} marked as paid via Paystack");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Paystack webhook");
            return false;
        }
    }

    public async Task<bool> HandleOzowWebhook(Dictionary<string, string> data)
    {
        try
        {
            if (!data.TryGetValue("Status", out var ozowStatus) || !string.Equals(ozowStatus, "Complete", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!Guid.TryParse(data.GetValueOrDefault("TransactionReference"), out var invoiceId))
                return false;

            // Verify Ozow hash signature before processing
            var invoice = await _context.Invoices
                .Include(i => i.Merchant)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);

            if (invoice?.Merchant?.OzowPrivateKey != null)
            {
                var receivedHash = data.GetValueOrDefault("Hash", "");
                var dataCopy = new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
                dataCopy.Remove("Hash");

                var hashInput = string.Join("",
                    dataCopy.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Value));
                hashInput += invoice.Merchant.OzowPrivateKey;

                using var sha512 = SHA512.Create();
                var computedHash = BitConverter.ToString(
                    sha512.ComputeHash(Encoding.UTF8.GetBytes(hashInput))).Replace("-", "").ToLower();

                if (!string.Equals(computedHash, receivedHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Ozow webhook hash mismatch for invoice {InvoiceId}", invoiceId);
                    return false;
                }
            }

            if (invoice == null || invoice.Status == "Paid")
                return false;

            // Mark as paid
            invoice.Status = "Paid";
            invoice.PaidDate = DateTime.UtcNow;
            invoice.PaymentMethod = "Ozow";
            invoice.PaymentTransactionId = data.GetValueOrDefault("TransactionId");
            invoice.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Send receipt
            await _whatsAppService.SendReceiptMessage(invoiceId);
            await _emailService.SendReceiptEmail(invoiceId);

            _logger.LogInformation($"Invoice {invoice.InvoiceNumber} marked as paid via Ozow");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Ozow webhook");
            return false;
        }
    }

    // ── Stripe ────────────────────────────────────────────────────────────────

    public async Task<string> GenerateStripeUrl(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Merchant)
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null || !invoice.Merchant.StripeEnabled)
            return string.Empty;

        var secretKey = invoice.Merchant.StripeSecretKey;
        if (string.IsNullOrEmpty(secretKey))
            return string.Empty;

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var amountInMinorUnits = (long)(invoice.TotalAmount * 100);

        var formData = new Dictionary<string, string>
        {
            { "mode", "payment" },
            { "success_url", $"{baseUrl}/payment/success?session_id={{CHECKOUT_SESSION_ID}}" },
            { "cancel_url", $"{baseUrl}/payment/cancel" },
            { "line_items[0][price_data][currency]", invoice.Currency.ToLower() },
            { "line_items[0][price_data][unit_amount]", amountInMinorUnits.ToString() },
            { "line_items[0][price_data][product_data][name]", $"Invoice {invoice.InvoiceNumber}" },
            { "line_items[0][quantity]", "1" },
            { "payment_intent_data[metadata][invoice_id]", invoice.Id.ToString() },
            { "payment_intent_data[metadata][merchant_id]", invoice.MerchantId.ToString() },
            { "client_reference_id", $"INV-{invoice.Id}" },
            { "customer_email", invoice.Client.Email ?? "" }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.stripe.com/v1/checkout/sessions");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secretKey);
            request.Content = new FormUrlEncodedContent(formData);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Stripe checkout session error: {Error}", error);
                return string.Empty;
            }

            var responseData = await System.Text.Json.JsonSerializer.DeserializeAsync<System.Text.Json.JsonElement>(
                await response.Content.ReadAsStreamAsync());

            return responseData.GetProperty("url").GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Stripe checkout URL");
            return string.Empty;
        }
    }

    public async Task<bool> HandleStripeWebhook(string payload, string signature)
    {
        try
        {
            var webhookSecret = _configuration["Stripe:WebhookSecret"] ?? "";
            if (string.IsNullOrWhiteSpace(webhookSecret) || webhookSecret.Contains("YOUR_", StringComparison.Ordinal))
            {
                _logger.LogWarning("Stripe webhook secret not configured; rejecting webhook");
                return false;
            }

            // Verify Stripe-Signature header: t=<timestamp>,v1=<hmac>
            var parts = signature.Split(',').Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1]);

            if (!parts.TryGetValue("t", out var timestamp) || !parts.TryGetValue("v1", out var sig))
            {
                _logger.LogWarning("Stripe webhook missing signature parts");
                return false;
            }

            var signedPayload = $"{timestamp}.{payload}";
            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
            var computed = BitConverter.ToString(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).Replace("-", "").ToLower();

            if (!string.Equals(computed, sig, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Stripe webhook signature mismatch");
                return false;
            }

            var data = JsonSerializer.Deserialize<JsonElement>(payload);
            var eventType = data.GetProperty("type").GetString();

            if (eventType != "checkout.session.completed" && eventType != "payment_intent.succeeded")
                return true; // not an error, just irrelevant event

            // Try to extract invoice ID from client_reference_id or metadata
            Guid invoiceId = Guid.Empty;

            if (data.TryGetProperty("data", out var eventData) &&
                eventData.TryGetProperty("object", out var obj))
            {
                if (obj.TryGetProperty("client_reference_id", out var refProp))
                {
                    var refStr = refProp.GetString() ?? "";
                    if (refStr.StartsWith("INV-"))
                        Guid.TryParse(refStr.Replace("INV-", ""), out invoiceId);
                }

                if (invoiceId == Guid.Empty &&
                    obj.TryGetProperty("metadata", out var meta) &&
                    meta.TryGetProperty("invoice_id", out var idProp))
                {
                    Guid.TryParse(idProp.GetString(), out invoiceId);
                }
            }

            if (invoiceId == Guid.Empty)
                return false;

            var invoice = await _context.Invoices.FindAsync(invoiceId);
            if (invoice == null || invoice.Status == "Paid")
                return true;

            invoice.Status = "Paid";
            invoice.PaidDate = DateTime.UtcNow;
            invoice.PaymentMethod = "Stripe";
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _whatsAppService.SendReceiptMessage(invoiceId);
            await _emailService.SendReceiptEmail(invoiceId);

            _logger.LogInformation("Invoice {InvoiceId} marked as paid via Stripe", invoiceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Stripe webhook");
            return false;
        }
    }

    // ── PayPal ────────────────────────────────────────────────────────────────

    private async Task<string> GetPayPalAccessToken(string clientId, string clientSecret, bool isSandbox)
    {
        var url = isSandbox
            ? "https://api-m.sandbox.paypal.com/v1/oauth2/token"
            : "https://api-m.paypal.com/v1/oauth2/token";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return string.Empty;

        var data = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
        return data.GetProperty("access_token").GetString() ?? string.Empty;
    }

    public async Task<string> GeneratePayPalInvoiceUrl(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Merchant)
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null || !invoice.Merchant.PayPalEnabled)
            return string.Empty;

        var clientId = invoice.Merchant.PayPalClientId;
        var clientSecret = invoice.Merchant.PayPalClientSecret;
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return string.Empty;

        var isSandbox = _configuration["PayPal:Sandbox"] == "true";
        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";

        try
        {
            var accessToken = await GetPayPalAccessToken(clientId, clientSecret, isSandbox);
            if (string.IsNullOrEmpty(accessToken))
                return string.Empty;

            var apiBase = isSandbox ? "https://api-m.sandbox.paypal.com" : "https://api-m.paypal.com";

            var orderPayload = new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new
                    {
                        reference_id = $"INV-{invoice.Id}",
                        description = $"Invoice {invoice.InvoiceNumber}",
                        amount = new
                        {
                            currency_code = invoice.Currency,
                            value = invoice.TotalAmount.ToString("F2")
                        }
                    }
                },
                application_context = new
                {
                    return_url = $"{baseUrl}/payment/success",
                    cancel_url = $"{baseUrl}/payment/cancel",
                    brand_name = invoice.Merchant.BusinessName,
                    user_action = "PAY_NOW"
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/v2/checkout/orders");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(orderPayload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("PayPal order creation failed: {Error}", error);
                return string.Empty;
            }

            var responseData = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
            string? approveLink = null;
            foreach (var link in responseData.GetProperty("links").EnumerateArray())
            {
                if (link.TryGetProperty("rel", out var rel) && rel.GetString() == "approve" &&
                    link.TryGetProperty("href", out var href))
                {
                    approveLink = href.GetString();
                    break;
                }
            }

            return approveLink ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PayPal invoice URL");
            return string.Empty;
        }
    }

    public async Task<bool> HandlePayPalWebhook(string payload)
    {
        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(payload);
            var eventType = data.GetProperty("event_type").GetString();

            if (eventType != "CHECKOUT.ORDER.APPROVED" && eventType != "PAYMENT.CAPTURE.COMPLETED")
                return true;

            // Extract reference from purchase units
            Guid invoiceId = Guid.Empty;

            if (data.TryGetProperty("resource", out var resource))
            {
                if (resource.TryGetProperty("purchase_units", out var units))
                {
                    foreach (var unit in units.EnumerateArray())
                    {
                        if (unit.TryGetProperty("reference_id", out var refProp))
                        {
                            var refStr = refProp.GetString() ?? "";
                            if (refStr.StartsWith("INV-"))
                            {
                                Guid.TryParse(refStr.Replace("INV-", ""), out invoiceId);
                                break;
                            }
                        }
                    }
                }

                // For PAYMENT.CAPTURE.COMPLETED the structure differs
                if (invoiceId == Guid.Empty &&
                    resource.TryGetProperty("supplementary_data", out var suppData) &&
                    suppData.TryGetProperty("related_ids", out var relIds) &&
                    relIds.TryGetProperty("order_id", out var orderIdProp))
                {
                    var orderId = orderIdProp.GetString();
                    _logger.LogInformation("PayPal capture completed for order {OrderId}", orderId);
                }
            }

            if (invoiceId == Guid.Empty)
            {
                _logger.LogWarning("PayPal webhook could not extract invoice ID from payload");
                return false;
            }

            var invoice = await _context.Invoices.FindAsync(invoiceId);
            if (invoice == null || invoice.Status == "Paid")
                return true;

            invoice.Status = "Paid";
            invoice.PaidDate = DateTime.UtcNow;
            invoice.PaymentMethod = "PayPal";
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _whatsAppService.SendReceiptMessage(invoiceId);
            await _emailService.SendReceiptEmail(invoiceId);

            _logger.LogInformation("Invoice {InvoiceId} marked as paid via PayPal", invoiceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling PayPal webhook");
            return false;
        }
    }

    private string GeneratePayFastSignature(Dictionary<string, string> data, string passPhrase)
    {
        var pfParamString = string.Join("&", data
            .OrderBy(x => x.Key)
            .Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));

        if (!string.IsNullOrEmpty(passPhrase))
            pfParamString += $"&passphrase={Uri.EscapeDataString(passPhrase)}";

        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(pfParamString));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    private string ComputeHmacSha512(string data, string key)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(key));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }
}
