using System.ComponentModel.DataAnnotations.Schema;

namespace BlueSquares.Models;

public class Merchant
{
    public Guid Id { get; set; }
    public string FirebaseUid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactNumber { get; set; }
    public string? LogoUrl { get; set; }
    public string? DefaultInvoiceFooter { get; set; }
    public string Country { get; set; } = "ZA"; // ZA, GB, IE
    public string Currency { get; set; } = "ZAR"; // ZAR, GBP, EUR

    // ── SA payment gateways (ZA) ──────────────────────────────────────────────
    public bool PayFastEnabled { get; set; }
    public string? PayFastMerchantId { get; set; }
    public string? PayFastMerchantKey { get; set; }

    public bool PaystackEnabled { get; set; }
    public string? PaystackSecretKey { get; set; }
    public string? PaystackPublicKey { get; set; }

    public bool OzowEnabled { get; set; }
    public string? OzowSiteCode { get; set; }
    public string? OzowPrivateKey { get; set; }

    // ── UK / Ireland payment gateways (GB, IE) ────────────────────────────────
    public bool StripeEnabled { get; set; }
    public string? StripePublishableKey { get; set; }
    public string? StripeSecretKey { get; set; }

    // PayPal invoice payments (IE-preferred; also available in GB)
    public bool PayPalEnabled { get; set; }
    public string? PayPalClientId { get; set; }
    public string? PayPalClientSecret { get; set; }

    // ── Bank / transfer details ───────────────────────────────────────────────
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    /// <summary>Branch code (ZA) or Sort code (GB).</summary>
    public string? BranchCode { get; set; }
    public string? AccountHolderName { get; set; }
    /// <summary>IBAN for IE/EU SEPA transfers.</summary>
    public string? BankIban { get; set; }
    /// <summary>BIC/SWIFT for IE/EU SEPA transfers.</summary>
    public string? BankBic { get; set; }

    // ── Accounting integrations ───────────────────────────────────────────────
    public bool XeroEnabled { get; set; }
    public string? XeroTenantId { get; set; }
    public string? XeroAccessToken { get; set; }
    public string? XeroRefreshToken { get; set; }
    public DateTime? XeroTokenExpiresAt { get; set; }
    public DateTime? XeroConnectedAt { get; set; }
    public DateTime? XeroLastSyncAt { get; set; }

    public bool QuickBooksEnabled { get; set; }
    public string? QuickBooksRealmId { get; set; }
    public string? QuickBooksAccessToken { get; set; }
    public string? QuickBooksRefreshToken { get; set; }
    public DateTime? QuickBooksTokenExpiresAt { get; set; }
    public DateTime? QuickBooksConnectedAt { get; set; }
    public DateTime? QuickBooksLastSyncAt { get; set; }
    public string QuickBooksEnvironment { get; set; } = "sandbox";

    // ── Features ──────────────────────────────────────────────────────────────
    public bool StatementsEnabled { get; set; } = true;
    public bool AutoRemindersEnabled { get; set; }

    // ── Subscription ──────────────────────────────────────────────────────────
    public string SubscriptionTier { get; set; } = "trial"; // trial, pro, expired
    public DateTime? SubscriptionStartDate { get; set; }
    public DateTime? SubscriptionExpiryDate { get; set; }
    public DateTime? TrialStartDate { get; set; }
    public DateTime? TrialEndDate { get; set; }
    /// <summary>
    /// External subscription reference: Paystack subscription code (ZA)
    /// or PayPal subscription ID (GB / IE).
    /// </summary>
    public string? PaystackSubscriptionCode { get; set; }

    [NotMapped]
    public bool IsTrialActive => TrialEndDate.HasValue && DateTime.UtcNow < TrialEndDate.Value;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation properties ─────────────────────────────────────────────────
    public ICollection<Client> Clients { get; set; } = new List<Client>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<RecurringInvoiceSchedule> RecurringInvoiceSchedules { get; set; } = new List<RecurringInvoiceSchedule>();
}
