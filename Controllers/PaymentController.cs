using Microsoft.AspNetCore.Mvc;
using BlueSquares.Services;

namespace BlueSquares.Controllers;

[ApiController]
[Route("api/payment")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(IPaymentService paymentService, ILogger<PaymentController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    [HttpGet("payfast/{invoiceId}")]
    public async Task<IActionResult> GetPayFastUrl(Guid invoiceId)
    {
        var url = await _paymentService.GeneratePayFastUrl(invoiceId);
        if (string.IsNullOrEmpty(url))
            return BadRequest(new { message = "PayFast is not configured for this invoice" });
        return Ok(new { paymentUrl = url });
    }

    [HttpGet("paystack/{invoiceId}")]
    public async Task<IActionResult> GetPaystackUrl(Guid invoiceId)
    {
        var url = await _paymentService.GeneratePaystackUrl(invoiceId);
        if (string.IsNullOrEmpty(url))
            return BadRequest(new { message = "Paystack is not configured for this invoice" });
        return Ok(new { authorizationUrl = url });
    }

    [HttpGet("ozow/{invoiceId}")]
    public async Task<IActionResult> GetOzowUrl(Guid invoiceId)
    {
        var url = await _paymentService.GenerateOzowUrl(invoiceId);
        if (string.IsNullOrEmpty(url))
            return BadRequest(new { message = "Ozow is not configured for this invoice" });
        return Ok(new { paymentUrl = url });
    }

    [HttpGet("stripe/{invoiceId}")]
    public async Task<IActionResult> GetStripeUrl(Guid invoiceId)
    {
        var url = await _paymentService.GenerateStripeUrl(invoiceId);
        if (string.IsNullOrEmpty(url))
            return BadRequest(new { message = "Stripe is not configured for this invoice" });
        return Ok(new { paymentUrl = url });
    }

    [HttpGet("paypal/{invoiceId}")]
    public async Task<IActionResult> GetPayPalUrl(Guid invoiceId)
    {
        var url = await _paymentService.GeneratePayPalInvoiceUrl(invoiceId);
        if (string.IsNullOrEmpty(url))
            return BadRequest(new { message = "PayPal is not configured for this invoice" });
        return Ok(new { paymentUrl = url });
    }
}
