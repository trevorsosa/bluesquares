using BlueSquares.Data;
using BlueSquares.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace BlueSquares.Services;

public class EmailService : IEmailService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        ApplicationDbContext context,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<EmailService> logger)
    {
        _context = context;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
    }

    public async Task<bool> SendInvoiceEmail(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Client)
            .Include(i => i.Merchant)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null || string.IsNullOrEmpty(invoice.Client.Email))
            return false;

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var invoiceUrl = $"{baseUrl}/invoice/{invoice.Id}";

        var subject = $"Invoice #{invoice.InvoiceNumber} from {invoice.Merchant.BusinessName}";
        
        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #0066CC; color: white; padding: 20px; text-align: center; }}
        .content {{ background: #f9f9f9; padding: 30px; }}
        .invoice-details {{ background: white; padding: 20px; margin: 20px 0; border-radius: 5px; }}
        .button {{ display: inline-block; background: #0066CC; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; margin: 10px 5px; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Invoice from {invoice.Merchant.BusinessName}</h1>
        </div>
        <div class='content'>
            <p>Dear {invoice.Client.Name},</p>
            <p>Please find your invoice details below:</p>
            
            <div class='invoice-details'>
                <h2>Invoice #{invoice.InvoiceNumber}</h2>
                <p><strong>Date:</strong> {invoice.InvoiceDate:dd MMMM yyyy}</p>
                <p><strong>Due Date:</strong> {invoice.DueDate:dd MMMM yyyy}</p>
                <p><strong>Amount:</strong> {FormatCurrency(invoice.TotalAmount, invoice.Currency)}</p>
            </div>
            
            <div style='text-align: center;'>
                <a href='{invoiceUrl}' class='button'>View Invoice</a>
                <a href='{baseUrl}/pay/{invoice.Id}' class='button'>Pay Now</a>
            </div>
            
            {(!string.IsNullOrEmpty(invoice.Notes) ? $"<p><strong>Notes:</strong><br>{invoice.Notes}</p>" : "")}
        </div>
        <div class='footer'>
            <p>Powered by BlueSquares - Get Paid Faster</p>
            <p><a href='{baseUrl}'>squares.blue</a></p>
        </div>
    </div>
</body>
</html>";

        return await SendEmail(invoice.Client.Email, subject, htmlBody);
    }

    public async Task<bool> SendReceiptEmail(Guid invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Client)
            .Include(i => i.Merchant)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null || string.IsNullOrEmpty(invoice.Client.Email))
            return false;

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var receiptUrl = $"{baseUrl}/receipt/{invoice.Id}";

        var subject = $"Payment Receipt - Invoice #{invoice.InvoiceNumber}";
        
        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #28a745; color: white; padding: 20px; text-align: center; }}
        .content {{ background: #f9f9f9; padding: 30px; }}
        .receipt-details {{ background: white; padding: 20px; margin: 20px 0; border-radius: 5px; }}
        .button {{ display: inline-block; background: #0066CC; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>✓ Payment Received</h1>
        </div>
        <div class='content'>
            <p>Dear {invoice.Client.Name},</p>
            <p>Thank you for your payment! This confirms that we have received your payment.</p>
            
            <div class='receipt-details'>
                <h2>Receipt</h2>
                <p><strong>Invoice #:</strong> {invoice.InvoiceNumber}</p>
                <p><strong>Amount Paid:</strong> {FormatCurrency(invoice.TotalAmount, invoice.Currency)}</p>
                <p><strong>Payment Date:</strong> {invoice.PaidDate:dd MMMM yyyy}</p>
                {(!string.IsNullOrEmpty(invoice.PaymentReference) ? $"<p><strong>Reference:</strong> {invoice.PaymentReference}</p>" : "")}
            </div>
            
            <div style='text-align: center;'>
                <a href='{receiptUrl}' class='button'>Download Receipt</a>
            </div>
        </div>
        <div class='footer'>
            <p>Thank you for your business!</p>
            <p>{invoice.Merchant.BusinessName}</p>
            <p>Powered by BlueSquares - <a href='{baseUrl}'>squares.blue</a></p>
        </div>
    </div>
</body>
</html>";

        return await SendEmail(invoice.Client.Email, subject, htmlBody);
    }

    public async Task<bool> SendWelcomeEmail(string merchantEmail, string businessName)
    {
        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";
        var subject = "Welcome to BlueSquares!";
        
        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #0066CC; color: white; padding: 20px; text-align: center; }}
        .content {{ background: #f9f9f9; padding: 30px; }}
        .button {{ display: inline-block; background: #0066CC; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; margin: 10px 0; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Welcome to BlueSquares!</h1>
        </div>
        <div class='content'>
            <p>Hi {businessName},</p>
            <p>Welcome to BlueSquares - your mobile-first invoicing and debt collection assistant!</p>
            
            <h3>Get Started in 3 Easy Steps:</h3>
            <ol>
                <li><strong>Complete Your Profile</strong> - Add your business details and payment methods</li>
                <li><strong>Add Your First Client</strong> - Enter client details and WhatsApp number</li>
                <li><strong>Create & Send Invoice</strong> - Create an invoice and send it via WhatsApp in 60 seconds!</li>
            </ol>
            
            <div style='text-align: center; margin: 30px 0;'>
                <a href='{baseUrl}/dashboard' class='button'>Go to Dashboard</a>
            </div>
            
            <h3>Need Help?</h3>
            <p>Check out our guides or contact support at support@squares.blue</p>
        </div>
        <div class='footer'>
            <p>BlueSquares - Get Paid Faster</p>
            <p><a href='{baseUrl}'>squares.blue</a></p>
        </div>
    </div>
</body>
</html>";

        return await SendEmail(merchantEmail, subject, htmlBody);
    }

    public async Task<bool> AddToWaitlist(string email, string country, string countryCode)
    {
        try
        {
            // Check if already subscribed
            var existing = await _context.EmailSubscribers
                .FirstOrDefaultAsync(e => e.Email == email);

            if (existing == null)
            {
                var subscriber = new EmailSubscriber
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    Country = country,
                    CountryCode = countryCode,
                    SubscribedAt = DateTime.UtcNow
                };

                _context.EmailSubscribers.Add(subscriber);
                await _context.SaveChangesAsync();
            }

            // Send confirmation email
            var subject = "You're on the BlueSquares Waitlist!";
            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #0066CC; color: white; padding: 20px; text-align: center; }}
        .content {{ background: #f9f9f9; padding: 30px; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Thank You for Your Interest!</h1>
        </div>
        <div class='content'>
            <p>Hi there,</p>
            <p>Thank you for joining our waitlist for {country}!</p>
            <p>We're working hard to bring BlueSquares to your country. You'll be among the first to know when we launch in {country}.</p>
            <p>We'll send you an email as soon as we're available in your area.</p>
            <p><strong>What is BlueSquares?</strong></p>
            <p>BlueSquares is a mobile-first invoicing and debt collection assistant that helps small businesses get paid faster through WhatsApp.</p>
        </div>
        <div class='footer'>
            <p>BlueSquares - Get Paid Faster</p>
            <p><a href='https://squares.blue'>squares.blue</a></p>
        </div>
    </div>
</body>
</html>";

            return await SendEmail(email, subject, htmlBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding to waitlist");
            return false;
        }
    }

    private async Task<bool> SendEmail(string to, string subject, string htmlBody)
    {
        try
        {
            // SendPulse API integration
            var sendPulseId = _configuration["SendPulse:Id"];
            var sendPulseSecret = _configuration["SendPulse:Secret"];
            var fromEmail = _configuration["SendPulse:FromEmail"] ?? "noreply@squares.blue";
            var fromName = _configuration["SendPulse:FromName"] ?? "BlueSquares";

            if (string.IsNullOrEmpty(sendPulseId) || string.IsNullOrEmpty(sendPulseSecret))
            {
                _logger.LogWarning("SendPulse credentials not configured");
                return false;
            }

            // Get access token
            var tokenRequest = new
            {
                grant_type = "client_credentials",
                client_id = sendPulseId,
                client_secret = sendPulseSecret
            };

            var tokenContent = new StringContent(
                JsonSerializer.Serialize(tokenRequest),
                Encoding.UTF8,
                "application/json");

            var tokenResponse = await _httpClient.PostAsync(
                "https://api.sendpulse.com/oauth/access_token",
                tokenContent);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get SendPulse access token");
                return false;
            }

            var tokenData = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(
                await tokenResponse.Content.ReadAsStreamAsync());

            var accessToken = tokenData?["access_token"]?.ToString();

            if (string.IsNullOrEmpty(accessToken))
                return false;

            // Send email
            var emailRequest = new
            {
                email = new
                {
                    html = htmlBody,
                    text = subject,
                    subject = subject,
                    from = new { name = fromName, email = fromEmail },
                    to = new[] { new { email = to } }
                }
            };

            var emailContent = new StringContent(
                JsonSerializer.Serialize(emailRequest),
                Encoding.UTF8,
                "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendpulse.com/smtp/emails")
            {
                Content = emailContent
            };
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var emailResponse = await _httpClient.SendAsync(request);

            if (emailResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation($"Email sent successfully to {to}");
                return true;
            }
            else
            {
                var errorContent = await emailResponse.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to send email: {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error sending email to {to}");
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
