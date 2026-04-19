using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlueSquares.Data;
using BlueSquares.Models;
using BlueSquares.Services;
using System.ComponentModel.DataAnnotations;

namespace BlueSquares.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MerchantsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<MerchantsController> _logger;

    public MerchantsController(
        ApplicationDbContext context,
        IEmailService emailService,
        ILogger<MerchantsController> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var firebaseUid = GetFirebaseUid();
        if (string.IsNullOrEmpty(firebaseUid))
            return Unauthorized();

        var merchant = await _context.Merchants
            .FirstOrDefaultAsync(m => m.FirebaseUid == firebaseUid);

        if (merchant == null)
            return NotFound();

        return Ok(merchant);
    }

    [HttpPost("profile")]
    public async Task<IActionResult> CreateOrUpdateProfile([FromBody] Merchant merchantData)
    {
        var firebaseUid = GetFirebaseUid();
        if (string.IsNullOrEmpty(firebaseUid))
            return Unauthorized();

        var merchant = await _context.Merchants
            .FirstOrDefaultAsync(m => m.FirebaseUid == firebaseUid);

        if (merchant == null)
        {
            // Create new merchant with 14-day trial
            merchant = new Merchant
            {
                Id = Guid.NewGuid(),
                FirebaseUid = firebaseUid,
                Email = merchantData.Email,
                BusinessName = merchantData.BusinessName,
                SubscriptionTier = "trial",
                TrialStartDate = DateTime.UtcNow,
                TrialEndDate = DateTime.UtcNow.AddDays(14),
                CreatedAt = DateTime.UtcNow
            };
            _context.Merchants.Add(merchant);
            
            // Send welcome email
            await _emailService.SendWelcomeEmail(merchant.Email, merchant.BusinessName);
        }

        // Update fields
        merchant.BusinessName = merchantData.BusinessName;
        merchant.Address = merchantData.Address;
        merchant.ContactNumber = merchantData.ContactNumber;
        merchant.LogoUrl = merchantData.LogoUrl;
        merchant.DefaultInvoiceFooter = merchantData.DefaultInvoiceFooter;
        merchant.Country = merchantData.Country;
        merchant.Currency = merchantData.Currency;
        merchant.BankName = merchantData.BankName;
        merchant.AccountNumber = merchantData.AccountNumber;
        merchant.BranchCode = merchantData.BranchCode;
        merchant.AccountHolderName = merchantData.AccountHolderName;
        merchant.StatementsEnabled = merchantData.StatementsEnabled;
        merchant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(merchant);
    }

    [HttpPost("payment-settings")]
    public async Task<IActionResult> UpdatePaymentSettings([FromBody] PaymentSettingsDto settings)
    {
        var firebaseUid = GetFirebaseUid();
        if (string.IsNullOrEmpty(firebaseUid))
            return Unauthorized();

        var merchant = await _context.Merchants
            .FirstOrDefaultAsync(m => m.FirebaseUid == firebaseUid);

        if (merchant == null)
            return NotFound();

        // SA gateways
        merchant.PayFastEnabled = settings.PayFastEnabled;
        merchant.PayFastMerchantId = settings.PayFastMerchantId;
        merchant.PayFastMerchantKey = settings.PayFastMerchantKey;

        merchant.PaystackEnabled = settings.PaystackEnabled;
        merchant.PaystackSecretKey = settings.PaystackSecretKey;
        merchant.PaystackPublicKey = settings.PaystackPublicKey;

        merchant.OzowEnabled = settings.OzowEnabled;
        merchant.OzowSiteCode = settings.OzowSiteCode;
        merchant.OzowPrivateKey = settings.OzowPrivateKey;

        // UK / IE gateways
        merchant.StripeEnabled = settings.StripeEnabled;
        merchant.StripePublishableKey = settings.StripePublishableKey;
        merchant.StripeSecretKey = settings.StripeSecretKey;

        merchant.PayPalEnabled = settings.PayPalEnabled;
        merchant.PayPalClientId = settings.PayPalClientId;
        merchant.PayPalClientSecret = settings.PayPalClientSecret;

        // Bank / transfer details
        merchant.BankIban = settings.BankIban;
        merchant.BankBic = settings.BankBic;

        merchant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Payment settings updated successfully" });
    }

    [HttpGet("dashboard-stats")]
    public async Task<IActionResult> GetDashboardStats()
    {
        var firebaseUid = GetFirebaseUid();
        if (string.IsNullOrEmpty(firebaseUid))
            return Unauthorized();

        var merchant = await _context.Merchants
            .FirstOrDefaultAsync(m => m.FirebaseUid == firebaseUid);

        if (merchant == null)
            return NotFound();

        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);

        var stats = new
        {
            TotalOutstanding = await _context.Invoices
                .Where(i => i.MerchantId == merchant.Id && i.Status != "Paid")
                .SumAsync(i => i.TotalAmount),
            
            OverdueCount = await _context.Invoices
                .CountAsync(i => i.MerchantId == merchant.Id && 
                                 i.Status != "Paid" && 
                                 i.DueDate < now),
            
            ThisMonthPaid = await _context.Invoices
                .Where(i => i.MerchantId == merchant.Id && 
                           i.Status == "Paid" && 
                           i.PaidDate >= startOfMonth)
                .SumAsync(i => i.TotalAmount),
            
            PendingQueries = await _context.InvoiceQueries
                .CountAsync(q => q.Invoice.MerchantId == merchant.Id && !q.IsResolved),
            
            TotalClients = await _context.Clients
                .CountAsync(c => c.MerchantId == merchant.Id)
        };

        return Ok(stats);
    }

    [HttpGet("payment-setup-status")]
    public async Task<IActionResult> GetPaymentSetupStatus()
    {
        var firebaseUid = GetFirebaseUid();
        if (string.IsNullOrEmpty(firebaseUid))
            return Unauthorized();

        var merchant = await _context.Merchants
            .FirstOrDefaultAsync(m => m.FirebaseUid == firebaseUid);

        if (merchant == null)
            return NotFound();

        var recommendedProvider = GetRecommendedProvider(merchant.Country);
        var profileReady = HasProfileConfigured(merchant);
        var bankTransferReady = HasBankTransferConfigured(merchant);
        var onlinePaymentsReady = HasOnlinePaymentsConfigured(merchant);
        var onboardingStage = GetOnboardingStage(profileReady, bankTransferReady, onlinePaymentsReady);

        return Ok(new
        {
            profileReady,
            bankTransferReady,
            onlinePaymentsReady,
            canStartImmediately = bankTransferReady,
            onboardingStage = onboardingStage.Id,
            onboardingStageLabel = onboardingStage.Label,
            recommendedProvider = recommendedProvider.Id,
            recommendedProviderLabel = recommendedProvider.Label,
            recommendedProviderDescription = recommendedProvider.Description,
            recommendedFields = recommendedProvider.Fields,
            steps = new[]
            {
                new
                {
                    id = "profile",
                    label = "Business profile",
                    status = profileReady ? "complete" : "pending",
                    description = profileReady
                        ? "Your business basics are in place."
                        : "Add your business details first."
                },
                new
                {
                    id = "bank-transfer",
                    label = "Bank transfer",
                    status = bankTransferReady ? "complete" : "pending",
                    description = bankTransferReady
                        ? "Invoices can show bank transfer details right away."
                        : "Add bank transfer details to start collecting payments."
                },
                new
                {
                    id = "online-payments",
                    label = "Online payments",
                    status = onlinePaymentsReady ? "complete" : "optional",
                    description = onlinePaymentsReady
                        ? $"{recommendedProvider.Label} credentials saved. Online checkout can be offered on invoices."
                        : $"Optional next step: add {recommendedProvider.Label} when you want online checkout."
                }
            },
            nextStep = bankTransferReady
                ? "You can start sending invoices now with bank transfer. Add online payments when you're ready."
                : "Add your bank transfer details first so you can start collecting payments right away."
        });
    }

    private string? GetFirebaseUid()
    {
        return User.Claims.FirstOrDefault(c => c.Type == "user_id")?.Value;
    }

    private static bool HasProfileConfigured(Merchant merchant)
    {
        return !string.IsNullOrWhiteSpace(merchant.BusinessName) &&
               !string.IsNullOrWhiteSpace(merchant.Country) &&
               !string.IsNullOrWhiteSpace(merchant.Currency);
    }

    private static bool HasBankTransferConfigured(Merchant merchant)
    {
        if (merchant.Country == "IE")
            return !string.IsNullOrWhiteSpace(merchant.BankName) &&
                   !string.IsNullOrWhiteSpace(merchant.AccountHolderName) &&
                   !string.IsNullOrWhiteSpace(merchant.BankIban);

        return !string.IsNullOrWhiteSpace(merchant.BankName) &&
               !string.IsNullOrWhiteSpace(merchant.AccountHolderName) &&
               !string.IsNullOrWhiteSpace(merchant.AccountNumber);
    }

    private static bool HasOnlinePaymentsConfigured(Merchant merchant)
    {
        return (merchant.PaystackEnabled &&
                !string.IsNullOrWhiteSpace(merchant.PaystackPublicKey) &&
                !string.IsNullOrWhiteSpace(merchant.PaystackSecretKey)) ||
               (merchant.PayFastEnabled &&
                !string.IsNullOrWhiteSpace(merchant.PayFastMerchantId) &&
                !string.IsNullOrWhiteSpace(merchant.PayFastMerchantKey)) ||
               (merchant.OzowEnabled &&
                !string.IsNullOrWhiteSpace(merchant.OzowSiteCode) &&
                !string.IsNullOrWhiteSpace(merchant.OzowPrivateKey)) ||
               (merchant.StripeEnabled &&
                !string.IsNullOrWhiteSpace(merchant.StripePublishableKey) &&
                !string.IsNullOrWhiteSpace(merchant.StripeSecretKey)) ||
               (merchant.PayPalEnabled &&
                !string.IsNullOrWhiteSpace(merchant.PayPalClientId) &&
                !string.IsNullOrWhiteSpace(merchant.PayPalClientSecret));
    }

    private static PaymentProviderRecommendation GetRecommendedProvider(string? country) =>
        (country ?? "ZA").ToUpperInvariant() switch
        {
            "GB" => new PaymentProviderRecommendation(
                "stripe",
                "Stripe",
                "Best for card payments in the UK. You can skip this for now and start with bank transfer.",
                new[] { "Publishable key", "Secret key" }),
            "IE" => new PaymentProviderRecommendation(
                "paypal",
                "PayPal",
                "Quickest online payment option for Ireland. SEPA bank transfer also works immediately.",
                new[] { "Client ID", "Client secret" }),
            _ => new PaymentProviderRecommendation(
                "paystack",
                "Paystack",
                "Recommended for getting online payments live quickly. Bank transfer works while you finish setup.",
                new[] { "Public key", "Secret key" })
        };

    private static OnboardingStage GetOnboardingStage(
        bool profileReady,
        bool bankTransferReady,
        bool onlinePaymentsReady)
    {
        if (!profileReady)
            return new OnboardingStage("not_started", "Not started");

        if (!bankTransferReady)
            return new OnboardingStage("profile_done", "Profile done");

        if (!onlinePaymentsReady)
            return new OnboardingStage("bank_transfer_ready", "Bank transfer ready");

        return new OnboardingStage("online_payments_saved", "Online payments saved");
    }
}

public record PaymentProviderRecommendation(
    string Id,
    string Label,
    string Description,
    string[] Fields);

public record OnboardingStage(
    string Id,
    string Label);

public class PaymentSettingsDto
{
    // South Africa
    public bool PayFastEnabled { get; set; }
    public string? PayFastMerchantId { get; set; }
    public string? PayFastMerchantKey { get; set; }

    public bool PaystackEnabled { get; set; }
    public string? PaystackSecretKey { get; set; }
    public string? PaystackPublicKey { get; set; }

    public bool OzowEnabled { get; set; }
    public string? OzowSiteCode { get; set; }
    public string? OzowPrivateKey { get; set; }

    // United Kingdom & Ireland
    public bool StripeEnabled { get; set; }
    public string? StripePublishableKey { get; set; }
    public string? StripeSecretKey { get; set; }

    public bool PayPalEnabled { get; set; }
    public string? PayPalClientId { get; set; }
    public string? PayPalClientSecret { get; set; }

    // SEPA / IBAN (Ireland)
    public string? BankIban { get; set; }
    public string? BankBic { get; set; }
}
