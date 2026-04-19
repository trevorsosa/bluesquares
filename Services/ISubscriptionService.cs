namespace BlueSquares.Services;

public interface ISubscriptionService
{
    Task<bool> IsSubscriptionActive(Guid merchantId);
    Task<bool> IsInTrial(Guid merchantId);
    Task<int> GetDaysRemainingInTrial(Guid merchantId);
    /// <summary>
    /// Upgrade merchant to Pro. <paramref name="externalSubscriptionCode"/> is a Paystack
    /// subscription code (ZA) or PayPal subscription ID (GB / IE).
    /// <paramref name="billingCycle"/> is "monthly" (default) or "annual".
    /// </summary>
    Task<bool> UpgradeToPro(Guid merchantId, string externalSubscriptionCode, string billingCycle = "monthly");
    Task<bool> CancelSubscription(Guid merchantId);
    Task<string> GetSubscriptionStatus(Guid merchantId);
}
