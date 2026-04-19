using BlueSquares.Data;
using Microsoft.EntityFrameworkCore;

namespace BlueSquares.Services;

/// <summary>
/// Runs once per hour. For each merchant with auto-reminders enabled it evaluates their
/// ReminderSchedule rules and sends WhatsApp reminders for matching unpaid invoices.
///
/// A reminder is only sent if:
///   - The invoice is unpaid
///   - The schedule rule matches today's offset from the due date
///   - No reminder has been sent in the last 20 hours (prevents duplicate sends if the
///     service restarts mid-day)
/// </summary>
public class ReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderBackgroundService> _logger;

    // Run once per hour; reminder logic only fires once per 20-hour window per invoice
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    public ReminderBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reminder background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessReminders(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in reminder background service");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task ProcessReminders(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();

        var today = DateTime.UtcNow.Date;
        var cutoff = DateTime.UtcNow.AddHours(-20); // don't double-send within the same day

        // Load all enabled schedules for merchants that have auto-reminders on
        var schedules = await db.ReminderSchedules
            .Include(s => s.Merchant)
            .Where(s => s.Enabled && s.Merchant.AutoRemindersEnabled)
            .ToListAsync(cancellationToken);

        if (!schedules.Any())
            return;

        _logger.LogInformation("Processing reminders: {Count} active schedules", schedules.Count);

        var remindersSent = 0;

        foreach (var schedule in schedules)
        {
            // Work out the target due date this schedule rule is watching
            // DaysBeforeDue  = positive int → invoice due in N days
            // DaysAfterDue   = positive int → invoice N days overdue

            DateTime targetDueDate;

            if (schedule.DaysBeforeDue > 0)
            {
                // Send reminder when due date is N days from today
                targetDueDate = today.AddDays(schedule.DaysBeforeDue);
            }
            else if (schedule.DaysAfterDue > 0)
            {
                // Send reminder when invoice is N days past due
                targetDueDate = today.AddDays(-schedule.DaysAfterDue);
            }
            else
            {
                // DaysBeforeDue = 0 or -1 → send on the day of or day before due date
                int offset = schedule.DaysBeforeDue; // 0 or -1
                targetDueDate = today.AddDays(-offset);
            }

            // Find unpaid invoices for this merchant with the matching due date
            // that haven't had a reminder sent recently
            var invoices = await db.Invoices
                .Where(i =>
                    i.MerchantId == schedule.MerchantId &&
                    i.Status != "Paid" &&
                    i.DueDate.Date == targetDueDate &&
                    (i.LastReminderSentAt == null || i.LastReminderSentAt < cutoff))
                .ToListAsync(cancellationToken);

            foreach (var invoice in invoices)
            {
                try
                {
                    var sent = await whatsApp.SendReminderMessage(invoice.Id);
                    if (sent)
                    {
                        remindersSent++;
                        _logger.LogInformation(
                            "Auto-reminder sent for invoice {InvoiceNumber} (merchant {MerchantId})",
                            invoice.InvoiceNumber, schedule.MerchantId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to send auto-reminder for invoice {InvoiceId}", invoice.Id);
                }
            }
        }

        if (remindersSent > 0)
            _logger.LogInformation("Auto-reminders sent this cycle: {Count}", remindersSent);
    }
}
