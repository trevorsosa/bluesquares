using BlueSquares.Data;
using BlueSquares.Middleware;
using BlueSquares.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using System.Threading.RateLimiting;

// Community licence — free for projects under $1M/year revenue
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!);

// Configure PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("YOUR_DROPLET_IP"))
    throw new InvalidOperationException("Database connection string is not configured. Set ConnectionStrings:DefaultConnection.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 60;
        limiter.QueueLimit = 0;
    });
});

// Register services
builder.Services.AddHttpClient();
builder.Services.AddScoped<FirebaseAuthService>();
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IGeoLocationService, GeoLocationService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IPdfService, PdfService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IAccountingIntegrationService, AccountingIntegrationService>();

// Auto-reminder background job
builder.Services.AddHostedService<ReminderBackgroundService>();
builder.Services.AddHostedService<RecurringInvoiceBackgroundService>();

// CORS — restrict to production domain; update AppSettings:BaseUrl to include all allowed origins
var allowedOrigins = builder.Configuration["AppSettings:BaseUrl"]?.TrimEnd('/') ?? "https://squares.blue";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins, "https://www.squares.blue")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Global exception handler — never leak stack traces to clients
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        if (feature?.Error != null)
            logger.LogError(feature.Error, "Unhandled exception");

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." });
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Enforce HTTPS in production
    app.UseHttpsRedirection();
    app.UseHsts();
}

// Health check endpoint (no auth required)
app.MapHealthChecks("/health");

// Enable CORS
app.UseCors();

// Apply rate limiting
app.UseRateLimiter();

// Serve static files
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

// Firebase token verification — populates User.Claims for all subsequent handlers
app.UseMiddleware<FirebaseAuthMiddleware>();

app.UseAuthorization();

static Task MapStaticPage(HttpContext context, string filePath)
{
    context.Response.ContentType = "text/html";
    return context.Response.SendFileAsync(filePath);
}

// Clean URL routing
app.MapGet("/login", context => MapStaticPage(context, "wwwroot/login.html"));
app.MapGet("/signup", context => MapStaticPage(context, "wwwroot/signup.html"));
app.MapGet("/forgot-password", context => MapStaticPage(context, "wwwroot/forgot-password.html"));
app.MapGet("/dashboard", context => MapStaticPage(context, "wwwroot/dashboard.html"));
app.MapGet("/settings", context => MapStaticPage(context, "wwwroot/settings.html"));
app.MapGet("/invoices", context => MapStaticPage(context, "wwwroot/invoices.html"));
app.MapGet("/clients", context => MapStaticPage(context, "wwwroot/clients.html"));
app.MapGet("/queries", context => MapStaticPage(context, "wwwroot/queries.html"));
app.MapGet("/create-invoice", context => MapStaticPage(context, "wwwroot/create-invoice.html"));
app.MapGet("/invoice-detail", context => MapStaticPage(context, "wwwroot/invoice-detail.html"));
app.MapGet("/payment/success", context => MapStaticPage(context, "wwwroot/payment-result.html"));
app.MapGet("/payment/cancel", context => MapStaticPage(context, "wwwroot/payment-result.html"));
app.MapGet("/payment/error", context => MapStaticPage(context, "wwwroot/payment-result.html"));

app.MapGet("/invoice/{id:guid}", async context =>
{
    await MapStaticPage(context, "wwwroot/invoice.html");
});

app.MapGet("/pay/{id:guid}", context =>
{
    var id = context.Request.RouteValues["id"];
    context.Response.Redirect($"/payment.html?id={id}");
    return Task.CompletedTask;
});

app.MapGet("/receipt/{id:guid}", async context =>
{
    await MapStaticPage(context, "wwwroot/receipt.html");
});

app.MapGet("/statement/{id:guid}", async context =>
{
    await MapStaticPage(context, "wwwroot/statement.html");
});

app.MapControllers().RequireRateLimiting("api");

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

app.Run();
