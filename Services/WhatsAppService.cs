using BlueSquares.Data;
using BlueSquares.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace BlueSquares.Services;

public class WhatsAppService : IWhatsAppService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(
        ApplicationDbContext context,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<WhatsAppService> logger)
    {
        _context = context;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
    }

    public async Task<bool> SendInvoiceMessage(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Client)
            .Include(i => i.Merchant)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return false;

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var invoiceUrl = $"{baseUrl}/invoice/{invoice.Id}";
        var paymentUrl = $"{baseUrl}/pay/{invoice.Id}";

        var message = $"📄 *{invoice.Merchant.BusinessName}* sent you an invoice via BlueSquares\n\n" +
                      $"Invoice #: {invoice.InvoiceNumber}\n" +
                      $"Amount: {FormatCurrency(invoice.TotalAmount, invoice.Currency)}\n" +
                      $"Due: {invoice.DueDate:dd MMM yyyy}\n\n" +
                      $"🔗 View Invoice: {invoiceUrl}\n" +
                      $"💳 Pay Now: {paymentUrl}\n\n" +
                      $"Need help? Reply:\n" +
                      $"• PAY - for payment options\n" +
                      $"• QUERY - to ask a question\n" +
                      $"• STATEMENT - for your account statement";

        return await SendWhatsAppMessage(invoice.Client.WhatsAppNumber, message);
    }

    public async Task<bool> SendReminderMessage(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Client)
            .Include(i => i.Merchant)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return false;

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var invoiceUrl = $"{baseUrl}/invoice/{invoice.Id}";
        var paymentUrl = $"{baseUrl}/pay/{invoice.Id}";

        var daysOverdue = (DateTime.UtcNow.Date - invoice.DueDate.Date).Days;
        var overdueText = daysOverdue > 0 ? $" — {daysOverdue} day{(daysOverdue == 1 ? "" : "s")} overdue" : "";

        var message = $"⏰ *{invoice.Merchant.BusinessName}* is reminding you about an outstanding invoice via BlueSquares{overdueText}\n\n" +
                      $"Invoice #: {invoice.InvoiceNumber}\n" +
                      $"Amount: {FormatCurrency(invoice.TotalAmount, invoice.Currency)}\n" +
                      $"Due Date: {invoice.DueDate:dd MMM yyyy}\n\n" +
                      $"🔗 View Invoice: {invoiceUrl}\n" +
                      $"💳 Pay Now: {paymentUrl}\n\n" +
                      $"Reply PAY for payment options";

        var sent = await SendWhatsAppMessage(invoice.Client.WhatsAppNumber, message);
        
        if (sent)
        {
            invoice.LastReminderSentAt = DateTime.UtcNow;
            invoice.ReminderCount++;
            await _context.SaveChangesAsync();
        }

        return sent;
    }

    public async Task<bool> SendReceiptMessage(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Client)
            .Include(i => i.Merchant)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return false;

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var receiptUrl = $"{baseUrl}/receipt/{invoice.Id}";

        var message = $"✅ *{invoice.Merchant.BusinessName}* has received your payment via BlueSquares\n\n" +
                      $"Invoice #: {invoice.InvoiceNumber}\n" +
                      $"Amount Paid: {FormatCurrency(invoice.TotalAmount, invoice.Currency)}\n" +
                      $"Date: {invoice.PaidDate:dd MMM yyyy}\n\n" +
                      $"🧾 View Receipt: {receiptUrl}\n\n" +
                      $"Thank you for your business!";

        return await SendWhatsAppMessage(invoice.Client.WhatsAppNumber, message);
    }

    public async Task<bool> SendStatementMessage(Guid clientId, string statementUrl)
    {
        var client = await _context.Clients
            .Include(c => c.Merchant)
            .FirstOrDefaultAsync(c => c.Id == clientId);

        if (client == null) return false;

        var message = $"📊 *{client.Merchant.BusinessName}* shared your account statement via BlueSquares\n\n" +
                      $"As of: {DateTime.UtcNow:dd MMM yyyy}\n\n" +
                      $"🔗 View Statement: {statementUrl}\n\n" +
                      $"Reply QUERY if you have any questions";

        return await SendWhatsAppMessage(client.WhatsAppNumber, message);
    }

    public async Task<bool> SendMerchantReply(Guid queryId, string replyMessage)
    {
        var query = await _context.InvoiceQueries
            .Include(q => q.Invoice)
                .ThenInclude(i => i.Client)
            .Include(q => q.Invoice)
                .ThenInclude(i => i.Merchant)
            .FirstOrDefaultAsync(q => q.Id == queryId);

        if (query == null) return false;

        var message = $"💬 *{query.Invoice.Merchant.BusinessName}* replied to your query via BlueSquares\n\n" +
                      $"Re: Invoice #{query.Invoice.InvoiceNumber}\n\n" +
                      $"{replyMessage}";

        return await SendWhatsAppMessage(query.Invoice.Client.WhatsAppNumber, message);
    }

    public async Task ProcessIncomingMessage(string from, string message, string messageId)
    {
        try
        {
            var normalizedMessage = message.Trim().ToUpperInvariant();

            // Find client by WhatsApp number
            var client = await _context.Clients
                .Include(c => c.Invoices.Where(i => i.Status != "Paid"))
                .Include(c => c.Merchant)
                .FirstOrDefaultAsync(c => c.WhatsAppNumber == from);

            if (client == null)
            {
                _logger.LogWarning($"Received message from unknown number: {from}");
                return;
            }

            // Get the most recent unpaid invoice for context
            var recentInvoice = client.Invoices.OrderByDescending(i => i.CreatedAt).FirstOrDefault();

            if (normalizedMessage.Contains("PAY"))
            {
                await HandlePayRequest(client, recentInvoice);
            }
            else if (normalizedMessage.Contains("QUERY"))
            {
                await HandleQueryRequest(from, client);
            }
            else if (normalizedMessage.Contains("STATEMENT"))
            {
                await HandleStatementRequest(client);
            }
            else if (normalizedMessage.Contains("WILL PAY") || normalizedMessage.Contains("PAYING"))
            {
                await HandlePromiseToPay(client, recentInvoice, message);
            }
            else
            {
                // Log as potential query
                if (recentInvoice != null)
                {
                    var query = new InvoiceQuery
                    {
                        Id = Guid.NewGuid(),
                        InvoiceId = recentInvoice.Id,
                        QueryText = message,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.InvoiceQueries.Add(query);
                    await _context.SaveChangesAsync();

                    var reply = "Thank you for your message. We've logged your query and will respond shortly.\n\n" +
                               "In the meantime, you can:\n" +
                               "• Reply PAY for payment options\n" +
                               "• Reply STATEMENT for your account statement";
                    await SendWhatsAppMessage(from, reply);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing incoming message from {from}");
        }
    }

    private async Task HandlePayRequest(Client client, Invoice? invoice)
    {
        if (invoice == null)
        {
            await SendWhatsAppMessage(client.WhatsAppNumber, 
                "You don't have any outstanding invoices at the moment.");
            return;
        }

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var paymentUrl = $"{baseUrl}/pay/{invoice.Id}";

        var paymentOptions = new StringBuilder();
        paymentOptions.AppendLine("💳 *Payment Options*\n");
        paymentOptions.AppendLine($"Invoice #{invoice.InvoiceNumber}");
        paymentOptions.AppendLine($"Amount: {FormatCurrency(invoice.TotalAmount, invoice.Currency)}\n");

        if (client.Merchant.PayFastEnabled)
        {
            paymentOptions.AppendLine($"1️⃣ Pay online: {paymentUrl}");
        }

        if (!string.IsNullOrEmpty(client.Merchant.BankName))
        {
            paymentOptions.AppendLine("\n2️⃣ EFT/Bank Transfer:");
            paymentOptions.AppendLine($"Bank: {client.Merchant.BankName}");
            paymentOptions.AppendLine($"Account: {client.Merchant.AccountNumber}");
            paymentOptions.AppendLine($"Branch: {client.Merchant.BranchCode}");
            paymentOptions.AppendLine($"Reference: {invoice.PaymentRefCode}");
        }

        await SendWhatsAppMessage(client.WhatsAppNumber, paymentOptions.ToString());
    }

    private async Task HandleQueryRequest(string from, Client client)
    {
        var message = "Sure! Please type your question and we'll get back to you shortly.";
        await SendWhatsAppMessage(from, message);
    }

    private async Task HandleStatementRequest(Client client)
    {
        if (!client.Merchant.StatementsEnabled)
        {
            await SendWhatsAppMessage(client.WhatsAppNumber,
                "Statements are not enabled. Please reply QUERY to contact the merchant directly.");
            return;
        }

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var statementUrl = $"{baseUrl}/statement/{client.Id}";

        await SendWhatsAppMessage(client.WhatsAppNumber,
            $"📊 Here's your account statement:\n{statementUrl}");
    }

    private async Task HandlePromiseToPay(Client client, Invoice? invoice, string message)
    {
        if (invoice == null) return;

        // Try to extract a date from the message
        // This is simplified - in production you'd use better NLP
        DateTime? promiseDate = ExtractDateFromMessage(message);

        if (promiseDate.HasValue)
        {
            invoice.PromisedPaymentDate = promiseDate.Value;
            invoice.PromisedPaymentNote = message;
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await SendWhatsAppMessage(client.WhatsAppNumber,
                $"✅ Thank you! We've noted your payment commitment for {promiseDate.Value:dd MMM yyyy}.\n\n" +
                $"We'll send you a gentle reminder on that day.");
        }
        else
        {
            await SendWhatsAppMessage(client.WhatsAppNumber,
                "Thank you for letting us know. When do you expect to make the payment? " +
                "Please reply with a date (e.g., 'Friday', '15th Feb', or '2026-02-15')");
        }
    }

    private DateTime? ExtractDateFromMessage(string message)
    {
        // Simplified date extraction - you'd want more robust parsing
        var today = DateTime.UtcNow.Date;
        var lowerMessage = message.ToLowerInvariant();

        if (lowerMessage.Contains("today")) return today;
        if (lowerMessage.Contains("tomorrow")) return today.AddDays(1);
        if (lowerMessage.Contains("friday")) return GetNextDayOfWeek(DayOfWeek.Friday);
        if (lowerMessage.Contains("monday")) return GetNextDayOfWeek(DayOfWeek.Monday);
        
        // Try to parse dates like "15th" or "25th"
        var match = System.Text.RegularExpressions.Regex.Match(message, @"(\d{1,2})(st|nd|rd|th)?");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int day))
        {
            if (day >= 1 && day <= 31)
            {
                var month = today.Month;
                var year = today.Year;
                if (day < today.Day)
                {
                    month++;
                    if (month > 12)
                    {
                        month = 1;
                        year++;
                    }
                }
                try
                {
                    return new DateTime(year, month, day);
                }
                catch { }
            }
        }

        return null;
    }

    private DateTime GetNextDayOfWeek(DayOfWeek day)
    {
        var today = DateTime.UtcNow.Date;
        int daysUntil = ((int)day - (int)today.DayOfWeek + 7) % 7;
        if (daysUntil == 0) daysUntil = 7; // If it's today, get next week
        return today.AddDays(daysUntil);
    }

    private async Task<bool> SendWhatsAppMessage(string to, string message)
    {
        try
        {
            // WhatsApp Cloud API configuration
            var accessToken = _configuration["WhatsApp:AccessToken"];
            var phoneNumberId = _configuration["WhatsApp:PhoneNumberId"];

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(phoneNumberId))
            {
                _logger.LogWarning("WhatsApp credentials not configured");
                return false;
            }

            var requestBody = new
            {
                messaging_product = "whatsapp",
                to = to,
                type = "text",
                text = new { body = message }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://graph.facebook.com/v18.0/{phoneNumberId}/messages")
            {
                Content = content
            };
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation($"WhatsApp message sent successfully to {to}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to send WhatsApp message: {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error sending WhatsApp message to {to}");
            return false;
        }
    }

    private string FormatCurrency(decimal amount, string currency)
    {
        return currency switch
        {
            "ZAR" => $"R {amount:N2}",
            "GBP" => $"£{amount:N2}",
            "EUR" => $"EUR {amount:N2}",
            _ => $"{currency} {amount:N2}"
        };
    }
}
