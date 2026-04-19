using BlueSquares.Data;
using Microsoft.EntityFrameworkCore;

namespace BlueSquares.Services;

public class RecurringInvoiceBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringInvoiceBackgroundService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    public RecurringInvoiceBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<RecurringInvoiceBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Recurring invoice background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessSchedules(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in recurring invoice background service");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task ProcessSchedules(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invoiceService = scope.ServiceProvider.GetRequiredService<IInvoiceService>();

        var today = DateTime.UtcNow.Date;
        var dueSchedules = await db.RecurringInvoiceSchedules
            .Include(s => s.LineItems)
            .Where(s => s.IsActive && s.NextRunDate.Date <= today)
            .ToListAsync(cancellationToken);

        foreach (var schedule in dueSchedules)
        {
            try
            {
                if (schedule.LastRunDate?.Date == today)
                    continue;

                var result = await invoiceService.CreateInvoiceAsync(schedule.MerchantId, new InvoiceCreationRequest
                {
                    ClientId = schedule.ClientId,
                    InvoiceDate = today,
                    DueDate = today.AddDays(schedule.DueDaysAfterIssue),
                    Notes = schedule.Notes,
                    RecurringInvoiceScheduleId = schedule.Id,
                    LineItems = schedule.LineItems.Select(item => new InvoiceLineItemRequest
                    {
                        Description = item.Description,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice
                    }).ToList()
                });

                if (schedule.AutoSendWhatsApp || schedule.AutoSendEmail)
                    await invoiceService.SendInvoiceAsync(result.InvoiceId, schedule.AutoSendEmail, schedule.AutoSendWhatsApp);

                schedule.LastRunDate = DateTime.UtcNow;
                schedule.LastGeneratedInvoiceId = result.InvoiceId;
                schedule.NextRunDate = GetNextRunDate(schedule.NextRunDate, schedule.DayOfMonth);
                schedule.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Generated recurring invoice {InvoiceId} for schedule {ScheduleId}",
                    result.InvoiceId,
                    schedule.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed processing recurring invoice schedule {ScheduleId}", schedule.Id);
            }
        }
    }

    private static DateTime GetNextRunDate(DateTime currentRunDate, int dayOfMonth)
    {
        var nextMonth = currentRunDate.AddMonths(1);
        return new DateTime(
            nextMonth.Year,
            nextMonth.Month,
            Math.Min(dayOfMonth, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)));
    }
}
