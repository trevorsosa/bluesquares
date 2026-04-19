using BlueSquares.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlueSquares.Controllers;

[ApiController]
[Route("api/accounting-integrations")]
public class AccountingIntegrationsController : ControllerBase
{
    private readonly IAccountingIntegrationService _accountingIntegrationService;

    public AccountingIntegrationsController(IAccountingIntegrationService accountingIntegrationService)
    {
        _accountingIntegrationService = accountingIntegrationService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        try
        {
            return Ok(await _accountingIntegrationService.GetStatusAsync(merchantId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{provider}/connect")]
    public async Task<IActionResult> StartConnection(string provider)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        try
        {
            var authorizationUrl = await _accountingIntegrationService.GetAuthorizationUrlAsync(merchantId, provider);
            return Ok(new { authorizationUrl });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
    }

    [HttpGet("xero/callback")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> XeroCallback([FromQuery] string code, [FromQuery] string state)
    {
        var success = await _accountingIntegrationService.HandleCallbackAsync("xero", code, state);
        return Redirect(success ? "/settings?integration=xero_success" : "/settings?integration=xero_failed");
    }

    [HttpGet("quickbooks/callback")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> QuickBooksCallback([FromQuery] string code, [FromQuery] string state, [FromQuery] string? realmId)
    {
        var success = await _accountingIntegrationService.HandleCallbackAsync("quickbooks", code, state, realmId);
        return Redirect(success ? "/settings?integration=quickbooks_success" : "/settings?integration=quickbooks_failed");
    }

    [HttpPost("{provider}/export/{invoiceId:guid}")]
    public async Task<IActionResult> ExportInvoice(string provider, Guid invoiceId)
    {
        var merchantId = GetMerchantId();
        if (merchantId == Guid.Empty)
            return Unauthorized();

        try
        {
            var success = await _accountingIntegrationService.ExportInvoiceAsync(merchantId, invoiceId, provider);
            if (!success)
                return BadRequest(new { message = $"Could not export invoice to {provider}." });

            return Ok(new { message = $"Invoice exported to {provider} successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Guid GetMerchantId()
    {
        var claim = User.Claims.FirstOrDefault(c => c.Type == "merchant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}
