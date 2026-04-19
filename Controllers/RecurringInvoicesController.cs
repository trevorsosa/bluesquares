using BlueSquares.Data;
using BlueSquares.Filters;
using BlueSquares.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueSquares.Controllers;

[ApiController]
[Route("api/recurring-invoices")]
public class RecurringInvoicesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public RecurringInvoicesController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetSchedules()
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var schedules = await _context.RecurringInvoiceSchedules
            .Include(s => s.Client)
            .Include(s => s.LineItems)
            .Where(s => s.MerchantId == merchantId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Frequency,
                s.DayOfMonth,
                s.DueDaysAfterIssue,
                s.Currency,
                s.AutoSendWhatsApp,
                s.AutoSendEmail,
                s.IsActive,
                s.StartDate,
                s.NextRunDate,
                s.LastRunDate,
                s.LastGeneratedInvoiceId,
                client = new { s.ClientId, s.Client.Name, s.Client.Email, s.Client.WhatsAppNumber },
                lineItems = s.LineItems.Select(li => new
                {
                    li.Id,
                    li.Description,
                    li.Quantity,
                    li.UnitPrice,
                    total = li.Total
                }),
                totalAmount = s.LineItems.Sum(li => li.Quantity * li.UnitPrice)
            })
            .ToListAsync();

        return Ok(schedules);
    }

    [HttpPost]
    [RequireActiveSubscription]
    public async Task<IActionResult> CreateSchedule([FromBody] RecurringInvoiceScheduleDto dto)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var merchant = await _context.Merchants.FindAsync(merchantId);
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == dto.ClientId && c.MerchantId == merchantId);
        if (merchant == null || client == null)
            return BadRequest(new { message = "Merchant or client not found." });

        if (dto.DayOfMonth is < 1 or > 28)
            return BadRequest(new { message = "Day of month must be between 1 and 28 for reliable monthly sending." });

        if (dto.LineItems == null || !dto.LineItems.Any())
            return BadRequest(new { message = "At least one line item is required." });

        var startDate = (dto.StartDate?.Date ?? DateTime.UtcNow.Date);
        var nextRunDate = BuildNextRunDate(startDate, dto.DayOfMonth);

        var schedule = new RecurringInvoiceSchedule
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            ClientId = dto.ClientId,
            Name = string.IsNullOrWhiteSpace(dto.Name) ? $"{client.Name} monthly invoice" : dto.Name.Trim(),
            Frequency = "monthly",
            DayOfMonth = dto.DayOfMonth,
            DueDaysAfterIssue = dto.DueDaysAfterIssue <= 0 ? 7 : dto.DueDaysAfterIssue,
            Currency = merchant.Currency,
            Notes = dto.Notes,
            AutoSendWhatsApp = dto.AutoSendWhatsApp,
            AutoSendEmail = dto.AutoSendEmail,
            IsActive = dto.IsActive,
            StartDate = startDate,
            NextRunDate = nextRunDate,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LineItems = dto.LineItems.Select(item => new RecurringInvoiceLineItem
            {
                Id = Guid.NewGuid(),
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            }).ToList()
        };

        _context.RecurringInvoiceSchedules.Add(schedule);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Recurring invoice schedule created.", scheduleId = schedule.Id });
    }

    [HttpPut("{id}")]
    [RequireActiveSubscription]
    public async Task<IActionResult> UpdateSchedule(Guid id, [FromBody] RecurringInvoiceScheduleDto dto)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var schedule = await _context.RecurringInvoiceSchedules
            .Include(s => s.LineItems)
            .FirstOrDefaultAsync(s => s.Id == id && s.MerchantId == merchantId);

        if (schedule == null)
            return NotFound();

        if (dto.DayOfMonth is < 1 or > 28)
            return BadRequest(new { message = "Day of month must be between 1 and 28." });

        schedule.Name = string.IsNullOrWhiteSpace(dto.Name) ? schedule.Name : dto.Name.Trim();
        schedule.ClientId = dto.ClientId;
        schedule.DayOfMonth = dto.DayOfMonth;
        schedule.DueDaysAfterIssue = dto.DueDaysAfterIssue <= 0 ? schedule.DueDaysAfterIssue : dto.DueDaysAfterIssue;
        schedule.Notes = dto.Notes;
        schedule.AutoSendWhatsApp = dto.AutoSendWhatsApp;
        schedule.AutoSendEmail = dto.AutoSendEmail;
        schedule.IsActive = dto.IsActive;
        schedule.StartDate = dto.StartDate?.Date ?? schedule.StartDate;
        schedule.NextRunDate = BuildNextRunDate(schedule.StartDate, schedule.DayOfMonth);
        schedule.UpdatedAt = DateTime.UtcNow;

        _context.RecurringInvoiceLineItems.RemoveRange(schedule.LineItems);
        schedule.LineItems = dto.LineItems.Select(item => new RecurringInvoiceLineItem
        {
            Id = Guid.NewGuid(),
            RecurringInvoiceScheduleId = schedule.Id,
            Description = item.Description,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice
        }).ToList();

        await _context.SaveChangesAsync();
        return Ok(new { message = "Recurring invoice schedule updated." });
    }

    [HttpPost("{id}/toggle")]
    [RequireActiveSubscription]
    public async Task<IActionResult> ToggleSchedule(Guid id)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var schedule = await _context.RecurringInvoiceSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.MerchantId == merchantId);

        if (schedule == null)
            return NotFound();

        schedule.IsActive = !schedule.IsActive;
        schedule.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = schedule.IsActive ? "Recurring invoice schedule resumed." : "Recurring invoice schedule paused." });
    }

    [HttpDelete("{id}")]
    [RequireActiveSubscription]
    public async Task<IActionResult> DeleteSchedule(Guid id)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        var schedule = await _context.RecurringInvoiceSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.MerchantId == merchantId);

        if (schedule == null)
            return NotFound();

        _context.RecurringInvoiceSchedules.Remove(schedule);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Recurring invoice schedule deleted." });
    }

    private static DateTime BuildNextRunDate(DateTime startDate, int dayOfMonth)
    {
        var today = DateTime.UtcNow.Date;
        var candidate = new DateTime(startDate.Year, startDate.Month, Math.Min(dayOfMonth, DateTime.DaysInMonth(startDate.Year, startDate.Month)));
        if (candidate < today)
        {
            var nextMonth = startDate.AddMonths(1);
            candidate = new DateTime(nextMonth.Year, nextMonth.Month, Math.Min(dayOfMonth, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)));
        }

        return candidate;
    }

    private Guid GetMerchantId()
    {
        var claim = User.Claims.FirstOrDefault(c => c.Type == "merchant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}

public class RecurringInvoiceScheduleDto
{
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DayOfMonth { get; set; } = 1;
    public int DueDaysAfterIssue { get; set; } = 7;
    public string? Notes { get; set; }
    public bool AutoSendWhatsApp { get; set; } = true;
    public bool AutoSendEmail { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? StartDate { get; set; }
    public List<RecurringInvoiceLineItemDto> LineItems { get; set; } = new();
}

public class RecurringInvoiceLineItemDto
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}
