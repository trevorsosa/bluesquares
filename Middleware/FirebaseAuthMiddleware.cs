using BlueSquares.Data;
using BlueSquares.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BlueSquares.Middleware;

public class FirebaseAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FirebaseAuthMiddleware> _logger;

    public FirebaseAuthMiddleware(RequestDelegate next, ILogger<FirebaseAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, FirebaseAuthService authService, ApplicationDbContext db)
    {
        var authHeader = context.Request.Headers["Authorization"].ToString();

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
        {
            var token = authHeader["Bearer ".Length..].Trim();

            try
            {
                var firebaseToken = await authService.VerifyIdToken(token);

                if (firebaseToken != null)
                {
                    var firebaseUid = firebaseToken.Uid;

                    var merchant = await db.Merchants
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.FirebaseUid == firebaseUid);

                    var claims = new List<Claim>
                    {
                        new Claim("user_id", firebaseUid)
                    };

                    if (merchant != null)
                    {
                        claims.Add(new Claim("merchant_id", merchant.Id.ToString()));
                    }

                    var identity = new ClaimsIdentity(claims, "Firebase");
                    context.User = new ClaimsPrincipal(identity);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to verify Firebase token");
            }
        }

        await _next(context);
    }
}
