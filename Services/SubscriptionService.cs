using BlueSquares.Data;
using Microsoft.EntityFrameworkCore;

namespace BlueSquares.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        ApplicationDbContext context,
        ILogger<SubscriptionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> IsSubscriptionActive(Guid merchantId)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null) return false;

        // Check if in trial
        if (merchant.IsTrialActive)
            return true;

        // Check if paid subscription is active
        if (merchant.SubscriptionTier == "pro" && 
            merchant.SubscriptionExpiryDate.HasValue && 
            merchant.SubscriptionExpiryDate.Value > DateTime.UtcNow)
            return true;

        return false;
    }

    public async Task<bool> IsInTrial(Guid merchantId)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null) return false;

        return merchant.IsTrialActive;
    }

    public async Task<int> GetDaysRemainingInTrial(Guid merchantId)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null || !merchant.TrialEndDate.HasValue)
            return 0;

        if (!merchant.IsTrialActive)
            return 0;

        var daysRemaining = (merchant.TrialEndDate.Value - DateTime.UtcNow).Days;
        return Math.Max(0, daysRemaining);
    }

    public async Task<bool> UpgradeToPro(Guid merchantId, string externalSubscriptionCode, string billingCycle = "monthly")
    {
        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null) return false;

        var isAnnual = billingCycle?.ToLower() == "annual";

        merchant.SubscriptionTier = "pro";
        merchant.SubscriptionStartDate = DateTime.UtcNow;
        merchant.SubscriptionExpiryDate = isAnnual
            ? DateTime.UtcNow.AddYears(1)
            : DateTime.UtcNow.AddMonths(1);
        merchant.PaystackSubscriptionCode = externalSubscriptionCode;
        merchant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Merchant {MerchantId} upgraded to Pro ({Cycle})", merchantId, billingCycle);

        return true;
    }

    public async Task<bool> CancelSubscription(Guid merchantId)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null) return false;

        merchant.SubscriptionTier = "expired";
        merchant.PaystackSubscriptionCode = null;
        merchant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation($"Merchant {merchantId} subscription cancelled");

        return true;
    }

    public async Task<string> GetSubscriptionStatus(Guid merchantId)
    {
        var merchant = await _context.Merchants.FindAsync(merchantId);
        if (merchant == null) return "not_found";

        if (merchant.IsTrialActive)
        {
            var daysRemaining = await GetDaysRemainingInTrial(merchantId);
            return $"trial_{daysRemaining}_days";
        }

        if (merchant.SubscriptionTier == "pro" && 
            merchant.SubscriptionExpiryDate.HasValue && 
            merchant.SubscriptionExpiryDate.Value > DateTime.UtcNow)
        {
            return "active";
        }

        return "expired";
    }
}
