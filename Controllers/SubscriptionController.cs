using BlueSquares.Data;
using BlueSquares.Models;
using BlueSquares.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace BlueSquares.Controllers;

[ApiController]
[Route("api/subscription")]
public class SubscriptionController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SubscriptionController> _logger;

    public SubscriptionController(
        ApplicationDbContext context,
        ISubscriptionService subscriptionService,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<SubscriptionController> logger)
    {
        _context = context;
        _subscriptionService = subscriptionService;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
    }

    // ─── Status ───────────────────────────────────────────────────────────────

    /// <summary>GET /api/subscription/status — returns the merchant's current plan details.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty) return Unauthorized();

        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null) return NotFound();

        var statusKey = await _subscriptionService.GetSubscriptionStatus(merchantId);
        var daysRemaining = await _subscriptionService.GetDaysRemainingInTrial(merchantId);

        var provider = merchant.Country == "ZA" ? "paystack" : "paypal";

        // Determine billing cycle from expiry gap (annual = ~365 days from start)
        string? billingCycle = null;
        if (merchant.SubscriptionStartDate.HasValue && merchant.SubscriptionExpiryDate.HasValue)
        {
            var days = (merchant.SubscriptionExpiryDate.Value - merchant.SubscriptionStartDate.Value).TotalDays;
            billingCycle = days > 60 ? "annual" : "monthly";
        }

        return Ok(new
        {
            tier = merchant.SubscriptionTier,
            status = statusKey,
            isActive = await _subscriptionService.IsSubscriptionActive(merchantId),
            isInTrial = merchant.IsTrialActive,
            trialEndDate = merchant.TrialEndDate,
            daysRemainingInTrial = daysRemaining,
            subscriptionExpiryDate = merchant.SubscriptionExpiryDate,
            subscriptionProvider = provider,
            billingCycle,
            externalSubscriptionCode = merchant.PaystackSubscriptionCode
        });
    }

    // ─── Upgrade ──────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/subscription/upgrade/initiate
    /// Returns a hosted payment URL for the merchant to approve their subscription.
    /// Routes to Paystack (ZA) or PayPal (GB / IE) based on merchant country.
    /// </summary>
    [HttpPost("upgrade/initiate")]
    public async Task<IActionResult> InitiateUpgrade([FromBody] UpgradeInitiateDto dto)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty) return Unauthorized();

        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null) return NotFound();

        var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://squares.blue";

        var cycle = (dto.BillingCycle ?? "monthly").ToLower() == "annual" ? "annual" : "monthly";

        return merchant.Country switch
        {
            "ZA" => await InitiatePaystackUpgrade(merchant, merchantId, baseUrl, dto.PlanCode, cycle),
            "GB" or "IE" => await InitiatePayPalUpgrade(merchant, merchantId, baseUrl, merchant.Country, cycle),
            _ => await InitiatePaystackUpgrade(merchant, merchantId, baseUrl, dto.PlanCode, cycle)
        };
    }

    private async Task<IActionResult> InitiatePaystackUpgrade(
        BlueSquares.Models.Merchant merchant, Guid merchantId, string baseUrl,
        string planCodeOverride, string cycle)
    {
        var secretKey = _configuration["Paystack:SecretKey"];
        if (string.IsNullOrEmpty(secretKey) || secretKey.Contains("YOUR_"))
            return StatusCode(503, new { message = "Paystack not configured" });

        var configKey = cycle == "annual" ? "Paystack:PlanCode:ZA:Annual" : "Paystack:PlanCode:ZA:Monthly";
        var planCode = _configuration[configKey] ?? planCodeOverride;

        var payload = new
        {
            email = merchant.Email,
            plan = planCode,
            callback_url = $"{baseUrl}/dashboard?upgrade=success",
            metadata = new
            {
                merchant_id = merchantId.ToString(),
                action = "subscription_upgrade",
                billing_cycle = cycle,
                country = merchant.Country
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.paystack.co/transaction/initialize");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secretKey);
        request.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Paystack subscription init failed for merchant {MerchantId}", merchantId);
            return StatusCode(502, new { message = "Failed to initiate payment" });
        }

        var data = await System.Text.Json.JsonSerializer.DeserializeAsync<System.Text.Json.JsonElement>(
            await response.Content.ReadAsStreamAsync());
        var authorizationUrl = data.GetProperty("data").GetProperty("authorization_url").GetString();
        return Ok(new { authorizationUrl, provider = "paystack", billingCycle = cycle });
    }

    private async Task<IActionResult> InitiatePayPalUpgrade(
        BlueSquares.Models.Merchant merchant, Guid merchantId, string baseUrl,
        string country, string cycle)
    {
        var clientId = _configuration["PayPal:ClientId"];
        var clientSecret = _configuration["PayPal:ClientSecret"];
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) ||
            clientId.Contains("YOUR_"))
            return StatusCode(503, new { message = "PayPal not configured" });

        // GB monthly = £8/mo plan, GB annual = £75/yr plan
        // IE monthly = €9/mo plan, IE annual = €85/yr plan
        var planConfigKey = cycle == "annual"
            ? $"PayPal:PlanId:{country}:Annual"
            : $"PayPal:PlanId:{country}:Monthly";
        var planId = _configuration[planConfigKey];

        if (string.IsNullOrEmpty(planId) || planId.Contains("YOUR_"))
            return StatusCode(503, new { message = "PayPal subscription plan not configured" });

        var isSandbox = _configuration["PayPal:Sandbox"] == "true";
        var apiBase = isSandbox ? "https://api-m.sandbox.paypal.com" : "https://api-m.paypal.com";

        // Get PayPal OAuth token
        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/v1/oauth2/token");
        tokenRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        tokenRequest.Content = new FormUrlEncodedContent(
            new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });
        var tokenResponse = await _httpClient.SendAsync(tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
            return StatusCode(502, new { message = "Failed to authenticate with PayPal" });

        var tokenData = await System.Text.Json.JsonSerializer.DeserializeAsync<System.Text.Json.JsonElement>(
            await tokenResponse.Content.ReadAsStreamAsync());
        var accessToken = tokenData.GetProperty("access_token").GetString();

        // Create PayPal subscription
        var subscriptionPayload = new
        {
            plan_id = planId,
            subscriber = new { email_address = merchant.Email },
            application_context = new
            {
                brand_name = "BlueSquares",
                user_action = "SUBSCRIBE_NOW",
                return_url = $"{baseUrl}/dashboard?upgrade=success&provider=paypal",
                cancel_url = $"{baseUrl}/dashboard?upgrade=cancelled"
            },
            custom_id = merchantId.ToString()
        };

        using var subRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/v1/billing/subscriptions");
        subRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        subRequest.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(subscriptionPayload),
            System.Text.Encoding.UTF8, "application/json");

        var subResponse = await _httpClient.SendAsync(subRequest);
        if (!subResponse.IsSuccessStatusCode)
        {
            var error = await subResponse.Content.ReadAsStringAsync();
            _logger.LogError("PayPal subscription init failed for merchant {MerchantId}: {Error}", merchantId, error);
            return StatusCode(502, new { message = "Failed to initiate PayPal subscription" });
        }

        var subData = await System.Text.Json.JsonSerializer.DeserializeAsync<System.Text.Json.JsonElement>(
            await subResponse.Content.ReadAsStreamAsync());

        string? approveLink = null;
        foreach (var link in subData.GetProperty("links").EnumerateArray())
        {
            if (link.TryGetProperty("rel", out var rel) && rel.GetString() == "approve" &&
                link.TryGetProperty("href", out var href))
            {
                approveLink = href.GetString();
                break;
            }
        }

        if (string.IsNullOrEmpty(approveLink))
        {
            _logger.LogError("PayPal subscription response missing approve link for merchant {MerchantId}", merchantId);
            return StatusCode(502, new { message = "Failed to initiate PayPal subscription" });
        }

        return Ok(new { authorizationUrl = approveLink, provider = "paypal", billingCycle = cycle });
    }

    /// <summary>
    /// POST /api/subscription/upgrade/confirm
    /// Manual fallback to confirm an upgrade (e.g. after webhook failure).
    /// </summary>
    [HttpPost("upgrade/confirm")]
    public async Task<IActionResult> ConfirmUpgrade([FromBody] UpgradeConfirmDto dto)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty) return Unauthorized();

        var success = await _subscriptionService.UpgradeToPro(merchantId, dto.ExternalSubscriptionCode);

        if (!success)
            return StatusCode(500, new { message = "Failed to upgrade subscription" });

        _logger.LogInformation("Merchant {MerchantId} manually confirmed subscription upgrade", merchantId);
        return Ok(new { message = "Subscription upgraded to Pro successfully" });
    }

    // ─── Cancel ───────────────────────────────────────────────────────────────

    /// <summary>POST /api/subscription/cancel — cancels the current subscription.</summary>
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel()
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty) return Unauthorized();

        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null) return NotFound();

        var subscriptionCode = merchant.PaystackSubscriptionCode;

        if (!string.IsNullOrEmpty(subscriptionCode))
        {
            if (merchant.Country == "ZA")
            {
                // Cancel Paystack subscription
                var secretKey = _configuration["Paystack:SecretKey"];
                if (!string.IsNullOrEmpty(secretKey) && !secretKey.Contains("YOUR_"))
                {
                    try
                    {
                        using var req = new HttpRequestMessage(
                            HttpMethod.Post,
                            $"https://api.paystack.co/subscription/{subscriptionCode}/disable");
                        req.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secretKey);
                        req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                        await _httpClient.SendAsync(req);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not cancel Paystack subscription remotely");
                    }
                }
            }
            else if (merchant.Country == "GB" || merchant.Country == "IE")
            {
                // Cancel PayPal subscription
                var clientId = _configuration["PayPal:ClientId"];
                var clientSecret = _configuration["PayPal:ClientSecret"];
                if (!string.IsNullOrEmpty(clientId) && !clientId.Contains("YOUR_"))
                {
                    try
                    {
                        var isSandbox = _configuration["PayPal:Sandbox"] == "true";
                        var apiBase = isSandbox ? "https://api-m.sandbox.paypal.com" : "https://api-m.paypal.com";
                        var credentials = Convert.ToBase64String(
                            System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

                        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/v1/oauth2/token");
                        tokenReq.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                        tokenReq.Content = new FormUrlEncodedContent(
                            new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });
                        var tokenResp = await _httpClient.SendAsync(tokenReq);
                        var tokenData = await System.Text.Json.JsonSerializer.DeserializeAsync<System.Text.Json.JsonElement>(
                            await tokenResp.Content.ReadAsStreamAsync());
                        var accessToken = tokenData.GetProperty("access_token").GetString();

                        using var cancelReq = new HttpRequestMessage(
                            HttpMethod.Post, $"{apiBase}/v1/billing/subscriptions/{subscriptionCode}/cancel");
                        cancelReq.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                        cancelReq.Content = new StringContent(
                            System.Text.Json.JsonSerializer.Serialize(new { reason = "Customer requested cancellation" }),
                            System.Text.Encoding.UTF8, "application/json");
                        await _httpClient.SendAsync(cancelReq);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not cancel PayPal subscription remotely");
                    }
                }
            }
        }

        await _subscriptionService.CancelSubscription(merchantId);
        return Ok(new { message = "Subscription cancelled" });
    }

    // ─── Reminder Schedules ───────────────────────────────────────────────────

    /// <summary>GET /api/subscription/reminder-schedules — returns this merchant's reminder rules.</summary>
    [HttpGet("reminder-schedules")]
    public async Task<IActionResult> GetReminderSchedules()
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty) return Unauthorized();

        var schedules = await _context.ReminderSchedules
            .Where(s => s.MerchantId == merchantId)
            .OrderBy(s => s.DaysBeforeDue)
            .ThenBy(s => s.DaysAfterDue)
            .ToListAsync();

        return Ok(schedules);
    }

    /// <summary>
    /// PUT /api/subscription/reminder-schedules
    /// Replaces all reminder rules for the merchant. Also toggles the master
    /// AutoRemindersEnabled flag on the merchant record.
    /// </summary>
    [HttpPut("reminder-schedules")]
    public async Task<IActionResult> UpdateReminderSchedules([FromBody] ReminderSchedulesDto dto)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty) return Unauthorized();

        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null) return NotFound();

        // Replace all schedules
        var existing = _context.ReminderSchedules.Where(s => s.MerchantId == merchantId);
        _context.ReminderSchedules.RemoveRange(existing);

        foreach (var rule in dto.Schedules)
        {
            _context.ReminderSchedules.Add(new ReminderSchedule
            {
                Id = Guid.NewGuid(),
                MerchantId = merchantId,
                DaysBeforeDue = rule.DaysBeforeDue,
                DaysAfterDue = rule.DaysAfterDue,
                Enabled = rule.Enabled
            });
        }

        merchant.AutoRemindersEnabled = dto.AutoRemindersEnabled;
        merchant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Reminder schedules updated" });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private Guid GetMerchantId()
    {
        var claim = User.Claims.FirstOrDefault(c => c.Type == "merchant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}

public class UpgradeInitiateDto
{
    /// <summary>
    /// Billing cycle: "monthly" (default) or "annual".
    /// Determines which plan code / PayPal plan ID is used.
    /// </summary>
    public string BillingCycle { get; set; } = "monthly";

    /// <summary>
    /// Paystack plan code (ZA only) — fallback if per-country config keys are not set.
    /// </summary>
    public string PlanCode { get; set; } = string.Empty;
}

public class UpgradeConfirmDto
{
    [Required]
    /// <summary>Paystack subscription code (ZA) or PayPal subscription ID (GB / IE).</summary>
    public string ExternalSubscriptionCode { get; set; } = string.Empty;
}

public class ReminderSchedulesDto
{
    public bool AutoRemindersEnabled { get; set; }
    public List<ReminderRuleDto> Schedules { get; set; } = new();
}

public class ReminderRuleDto
{
    public int DaysBeforeDue { get; set; }
    public int DaysAfterDue { get; set; }
    public bool Enabled { get; set; } = true;
}
