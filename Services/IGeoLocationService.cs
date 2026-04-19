namespace BlueSquares.Services;

public interface IGeoLocationService
{
    Task<(string CountryCode, string CountryName)> GetCountryFromIp(string ipAddress);
    bool IsSupportedCountry(string countryCode);
}
