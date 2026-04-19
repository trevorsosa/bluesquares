namespace BlueSquares.Services;

public class GeoLocationService : IGeoLocationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeoLocationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string[] _supportedCountries = { "ZA", "GB", "IE" };

    public GeoLocationService(
        IConfiguration configuration,
        ILogger<GeoLocationService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<(string CountryCode, string CountryName)> GetCountryFromIp(string ipAddress)
    {
        try
        {
            var response = await _httpClient.GetStringAsync($"http://ip-api.com/json/{ipAddress}");

            var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response);

            var countryCode = data.GetProperty("countryCode").GetString() ?? "UNKNOWN";
            var countryName = data.GetProperty("country").GetString() ?? "Unknown";

            _logger.LogInformation("IP geo-detected as {CountryCode}", countryCode);

            return (countryCode, countryName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting country for IP");
            return ("UNKNOWN", "Unknown");
        }
    }

    public bool IsSupportedCountry(string countryCode)
    {
        return _supportedCountries.Contains(countryCode.ToUpperInvariant());
    }
}
