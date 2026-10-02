using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using StremioXboxPrototype.Models;
using Windows.Security.Credentials;
using Windows.Storage;

namespace StremioXboxPrototype.Services;

public sealed class StremioAccountClient
{
    private const string CredentialResource = "StremioXboxPrototype.Session";
    private const string AccountEmailKey = "StremioAccountEmail";
    private static readonly Uri ApiRoot = new("https://api.strem.io/api/");
    private static readonly Uri StremioWebRoot = new("https://www.strem.io/");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<StremioLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var request = new StremioLoginRequest { Email = email.Trim(), Password = password, Facebook = false };
        return await AuthenticateAsync(request, cancellationToken);
    }

    public StremioFacebookLoginAttempt CreateFacebookLoginAttempt()
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return new StremioFacebookLoginAttempt(state, new Uri(StremioWebRoot, $"login-fb/{state}"));
    }

    public async Task<StremioLoginResult> CompleteFacebookLoginAsync(string state,
        CancellationToken cancellationToken = default)
    {
        if (state.Length != 32 || state.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Invalid Facebook login state.", nameof(state));

        var credentialsUri = new Uri(StremioWebRoot, $"login-fb-get-acc/{state}");
        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await Http.GetAsync(credentialsUri, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    var envelope = JsonSerializer.Deserialize(responseJson,
                        StremioJsonContext.Default.StremioFacebookCredentialsEnvelope);
                    if (!string.IsNullOrWhiteSpace(envelope?.User?.Email) &&
                        !string.IsNullOrWhiteSpace(envelope.User.LoginToken))
                    {
                        var request = new StremioLoginRequest
                        {
                            Email = envelope.User.Email,
                            Password = envelope.User.LoginToken,
                            Facebook = true
                        };
                        return await AuthenticateAsync(request, cancellationToken);
                    }
                }
            }
            catch (HttpRequestException) when (attempt < 119)
            {
                // The official flow treats an unavailable credential response as
                // pending while the user finishes Facebook authentication.
            }
            catch (JsonException) when (attempt < 119)
            {
                // The endpoint may not return its final user payload until ready.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException("Facebook sign-in was not completed within two minutes.");
    }

    private static async Task<StremioLoginResult> AuthenticateAsync(StremioLoginRequest request,
        CancellationToken cancellationToken)
    {
        var envelope = await PostAsync("login", request, StremioJsonContext.Default.StremioLoginRequest,
            StremioJsonContext.Default.StremioLoginEnvelope, cancellationToken);
        ThrowIfApiError(envelope.Error);
        if (envelope.Result is null || string.IsNullOrWhiteSpace(envelope.Result.AuthKey))
            throw new InvalidDataException("Stremio login returned no session.");
        SaveSession(envelope.Result.User?.Email ?? request.Email, envelope.Result.AuthKey);
        return envelope.Result;
    }

    public async Task<StremioUser> GetUserAsync(string authKey, CancellationToken cancellationToken = default)
    {
        var request = new StremioAuthenticatedRequest { AuthKey = authKey };
        var envelope = await PostAsync("getUser", request, StremioJsonContext.Default.StremioAuthenticatedRequest,
            StremioJsonContext.Default.StremioUserEnvelope, cancellationToken);
        ThrowIfApiError(envelope.Error);
        if (envelope.Result is null || string.IsNullOrWhiteSpace(envelope.Result.Id))
            throw new InvalidDataException("Stremio returned an invalid user profile.");
        return envelope.Result;
    }

    public async Task<StremioAccountSession?> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var session = TryGetSession();
        if (session is null) return null;

        try
        {
            var user = await GetUserAsync(session.AuthKey, cancellationToken);
            var email = string.IsNullOrWhiteSpace(user.Email) ? session.Email : user.Email;
            if (!string.Equals(email, session.Email, StringComparison.OrdinalIgnoreCase))
                SaveSession(email, session.AuthKey);
            return new StremioAccountSession(email, session.AuthKey);
        }
        catch (StremioApiException exception) when (exception.IsAuthenticationFailure)
        {
            ClearSession();
            return null;
        }
    }

    public async Task<IReadOnlyList<StremioAddonDescriptor>> GetAddonCollectionAsync(string authKey, CancellationToken cancellationToken = default)
    {
        var request = new StremioAddonCollectionRequest { AuthKey = authKey };
        var envelope = await PostAsync("addonCollectionGet", request,
            StremioJsonContext.Default.StremioAddonCollectionRequest,
            StremioJsonContext.Default.StremioAddonCollectionEnvelope, cancellationToken);
        ThrowIfApiError(envelope.Error);
        if (envelope.Result is null) throw new InvalidDataException("Stremio add-on collection returned no result.");
        return envelope.Result.Addons;
    }

    public async Task LogoutAsync(string authKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new StremioAuthenticatedRequest { AuthKey = authKey };
            var envelope = await PostAsync("logout", request, StremioJsonContext.Default.StremioAuthenticatedRequest,
                StremioJsonContext.Default.StremioApiEnvelope, cancellationToken);
            ThrowIfApiError(envelope.Error);
        }
        finally { ClearSession(); }
    }

    public StremioAccountSession? TryGetSession()
    {
        var email = ApplicationData.Current.LocalSettings.Values[AccountEmailKey] as string;
        if (string.IsNullOrWhiteSpace(email)) return null;
        try
        {
            var credential = new PasswordVault().Retrieve(CredentialResource, email);
            credential.RetrievePassword();
            return new StremioAccountSession(email, credential.Password);
        }
        catch { return null; }
    }

    private static void SaveSession(string email, string authKey)
    {
        ClearSession();
        new PasswordVault().Add(new PasswordCredential(CredentialResource, email, authKey));
        ApplicationData.Current.LocalSettings.Values[AccountEmailKey] = email;
    }

    private static void ClearSession()
    {
        var vault = new PasswordVault();
        try { foreach (var credential in vault.FindAllByResource(CredentialResource)) vault.Remove(credential); }
        catch { }
        ApplicationData.Current.LocalSettings.Values.Remove(AccountEmailKey);
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(string method, TRequest request,
        JsonTypeInfo<TRequest> requestType, JsonTypeInfo<TResponse> responseType, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, requestType);
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(new Uri(ApiRoot, method), body, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Stremio API returned HTTP {(int)response.StatusCode}.");
        return JsonSerializer.Deserialize(responseJson, responseType)
               ?? throw new InvalidDataException("Stremio API returned an empty response.");
    }

    private static void ThrowIfApiError(JsonElement? error)
    {
        if (error is null || error.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
        var value = error.Value;
        var message = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind == JsonValueKind.Object && value.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;
        var code = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("code", out var codeElement)
            ? codeElement.ToString()
            : null;
        throw new StremioApiException(message ?? "Stremio API request failed.", code);
    }
}

public sealed class StremioApiException : Exception
{
    public StremioApiException(string message, string? code) : base(message) => Code = code;

    public string? Code { get; }

    public bool IsAuthenticationFailure
    {
        get
        {
            var value = $"{Code} {Message}".ToLowerInvariant();
            return value.Contains("auth") || value.Contains("session") || value.Contains("unauthorized") ||
                   value.Contains("login") || value.Contains("user required");
        }
    }
}
