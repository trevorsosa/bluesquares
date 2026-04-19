using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using BlueSquares.Services;
using System.Text;
using System.Text.Json;

namespace BlueSquares.Controllers;

[ApiController]
[Route("api/webhooks")]
[DisableRateLimiting]
public class WebhooksController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IPaymentService paymentService,
        IWhatsAppService whatsAppService,
        ISubscriptionService subscriptionService,
        IConfiguration configuration,
        ILogger<WebhooksController> logger)
    {
        _paymentService = paymentService;
        _whatsAppService = whatsAppService;
        _subscriptionService = subscriptionService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("payfast")]
    public async Task<IActionResult> PayFastWebhook()
    {
        try
        {
            var data = new Dictionary<string, string>();
            foreach (var key in Request.Form.Keys)
            {
                data[key] = Request.Form[key].ToString();
            }

            _logger.LogInformation("Received PayFast webhook");

            var result = await _paymentService.HandlePayFastWebhook(data);

            return result ? Ok() : BadRequest();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PayFast webhook");
            return StatusCode(500);
        }
    }

    [HttpPost("paystack")]
    public async Task<IActionResult> PaystackWebhook()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();

            var signature = Request.Headers["x-paystack-signature"].ToString();

            _logger.LogInformation("Received Paystack webhook");

            var result = await _paymentService.HandlePaystackWebhook(payload, signature);

            return result ? Ok() : BadRequest();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Paystack webhook");
            return StatusCode(500);
        }
    }

    [HttpPost("ozow")]
    public async Task<IActionResult> OzowWebhook()
    {
        try
        {
            var data = new Dictionary<string, string>();
            foreach (var key in Request.Form.Keys)
            {
                data[key] = Request.Form[key].ToString();
            }

            _logger.LogInformation("Received Ozow webhook");

            var result = await _paymentService.HandleOzowWebhook(data);

            return result ? Ok() : BadRequest();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Ozow webhook");
            return StatusCode(500);
        }
    }

    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();
            var signature = Request.Headers["Stripe-Signature"].ToString();

            _logger.LogInformation("Received Stripe webhook");

            var result = await _paymentService.HandleStripeWebhook(payload, signature);
            return result ? Ok() : BadRequest();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Stripe webhook");
            return StatusCode(500);
        }
    }

    [HttpPost("paypal")]
    public async Task<IActionResult> PayPalWebhook()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();

            _logger.LogInformation("Received PayPal webhook");

            // Handle both invoice payments and subscription events
            JsonElement data;
            try
            {
                data = JsonSerializer.Deserialize<JsonElement>(payload);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "PayPal webhook payload is not valid JSON");
                return BadRequest();
            }

            if (!data.TryGetProperty("event_type", out var eventTypeEl))
            {
                _logger.LogWarning("PayPal webhook missing event_type");
                return BadRequest();
            }

            var eventType = eventTypeEl.GetString();

            if (eventType == "BILLING.SUBSCRIPTION.ACTIVATED" ||
                eventType == "BILLING.SUBSCRIPTION.RENEWED")
            {
                // SaaS subscription confirmed — upgrade the merchant
                if (!data.TryGetProperty("resource", out var resource))
                {
                    _logger.LogWarning("PayPal subscription webhook missing resource");
                    return BadRequest();
                }

                var merchantId = resource.TryGetProperty("custom_id", out var customId)
                    ? customId.GetString()
                    : null;
                var subscriptionId = resource.TryGetProperty("id", out var subId)
                    ? subId.GetString()
                    : null;

                if (!string.IsNullOrEmpty(merchantId) && Guid.TryParse(merchantId, out var mid) &&
                    !string.IsNullOrEmpty(subscriptionId))
                {
                    // Detect annual vs monthly from the plan billing_cycles if present
                    var cycle = "monthly";
                    if (resource.TryGetProperty("billing_info", out var billing) &&
                        billing.TryGetProperty("cycle_executions", out var cycles))
                    {
                        foreach (var c in cycles.EnumerateArray())
                        {
                            if (c.TryGetProperty("tenure_type", out var tenure) &&
                                tenure.GetString() == "REGULAR" &&
                                c.TryGetProperty("total_cycles", out var totalCycles) &&
                                totalCycles.GetInt32() == 1)
                            {
                                cycle = "annual";
                                break;
                            }
                        }
                    }

                    await _subscriptionService.UpgradeToPro(mid, subscriptionId, cycle);
                    _logger.LogInformation("PayPal subscription activated for merchant {MerchantId} ({Cycle})", mid, cycle);
                }

                return Ok();
            }

            // Invoice payment events
            var result = await _paymentService.HandlePayPalWebhook(payload);
            return result ? Ok() : BadRequest();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PayPal webhook");
            return StatusCode(500);
        }
    }

    [HttpGet("whatsapp")]
    public IActionResult WhatsAppVerification(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.verify_token")] string token,
        [FromQuery(Name = "hub.challenge")] string challenge)
    {
        var verifyToken = _configuration["WhatsApp:VerifyToken"] ?? "BLUESQUARES_VERIFY_TOKEN";

        if (mode == "subscribe" && token == verifyToken)
        {
            _logger.LogInformation("WhatsApp webhook verified");
            return Ok(challenge);
        }

        return Forbid();
    }

    [HttpPost("whatsapp")]
    public async Task<IActionResult> WhatsAppWebhook([FromBody] WhatsAppWebhookDto data)
    {
        try
        {
            _logger.LogInformation("Received WhatsApp webhook");

            if (data.Entry == null || !data.Entry.Any())
                return Ok();

            foreach (var entry in data.Entry)
            {
                if (entry.Changes == null) continue;

                foreach (var change in entry.Changes)
                {
                    if (change.Value?.Messages == null) continue;

                    foreach (var message in change.Value.Messages)
                    {
                        var from = message.From;
                        var messageText = message.Text?.Body ?? "";
                        var messageId = message.Id;

                        await _whatsAppService.ProcessIncomingMessage(from, messageText, messageId);
                    }
                }
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp webhook");
            return Ok(); // Always return 200 to WhatsApp to avoid retries
        }
    }
}

public class WhatsAppWebhookDto
{
    public string Object { get; set; } = string.Empty;
    public List<WhatsAppEntry>? Entry { get; set; }
}

public class WhatsAppEntry
{
    public string Id { get; set; } = string.Empty;
    public List<WhatsAppChange>? Changes { get; set; }
}

public class WhatsAppChange
{
    public WhatsAppValue? Value { get; set; }
    public string Field { get; set; } = string.Empty;
}

public class WhatsAppValue
{
    public string MessagingProduct { get; set; } = string.Empty;
    public List<WhatsAppMessage>? Messages { get; set; }
}

public class WhatsAppMessage
{
    public string From { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public WhatsAppMessageText? Text { get; set; }
    public string Type { get; set; } = string.Empty;
}

public class WhatsAppMessageText
{
    public string Body { get; set; } = string.Empty;
}
