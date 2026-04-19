using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;

namespace BlueSquares.Services;

public class FirebaseAuthService
{
    private readonly ILogger<FirebaseAuthService> _logger;

    public FirebaseAuthService(IConfiguration configuration, ILogger<FirebaseAuthService> logger)
    {
        _logger = logger;
        
        // Initialize Firebase Admin SDK
        var credentialsPath = configuration["Firebase:CredentialsPath"];
        
        if (!string.IsNullOrEmpty(credentialsPath) && File.Exists(credentialsPath))
        {
            if (FirebaseApp.DefaultInstance == null)
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(credentialsPath)
                });
            }
        }
        else
        {
            _logger.LogWarning("Firebase credentials not found. Authentication will not work.");
        }
    }

    public async Task<FirebaseToken?> VerifyIdToken(string idToken)
    {
        try
        {
            if (FirebaseApp.DefaultInstance == null)
            {
                _logger.LogWarning("Firebase not initialized - cannot verify token");
                return null;
            }
            var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
            return decodedToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying Firebase token");
            return null;
        }
    }

    public async Task<string?> GetUserEmail(string uid)
    {
        try
        {
            if (FirebaseApp.DefaultInstance == null)
            {
                _logger.LogWarning("Firebase not initialized - cannot get user");
                return null;
            }
            var userRecord = await FirebaseAuth.DefaultInstance.GetUserAsync(uid);
            return userRecord.Email;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user email");
            return null;
        }
    }
}
