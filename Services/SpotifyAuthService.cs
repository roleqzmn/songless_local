using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace SpotiSognless.Services;

public sealed class SpotifyAuthService
{
    private readonly HttpClient _httpClient;
    private readonly SpotifyOptions _options;
    private readonly ConcurrentDictionary<string, PendingAuthorization> _pendingAuthorizations = new();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly object _tokenGate = new();
    private SpotifyTokens? _tokens;

    public SpotifyAuthService(IHttpClientFactory httpClientFactory, IOptions<SpotifyOptions> options)
    {
        _httpClient = httpClientFactory.CreateClient("Spotify");
        _options = options.Value;
    }

    public bool IsConnected
    {
        get
        {
            lock (_tokenGate)
            {
                return _tokens is not null;
            }
        }
    }

    public string CreateAuthorizationUrl()
    {
        ValidateConfiguration();

        var now = DateTimeOffset.UtcNow;
        foreach (var pending in _pendingAuthorizations)
        {
            if (pending.Value.ExpiresAt <= now)
            {
                _pendingAuthorizations.TryRemove(pending.Key, out _);
            }
        }

        var state = CreateUrlSafeRandom(32);
        var verifier = CreateUrlSafeRandom(64);
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        _pendingAuthorizations[state] = new PendingAuthorization(verifier, now.AddMinutes(10));

        return QueryHelpers.AddQueryString(
            "https://accounts.spotify.com/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = _options.ClientId,
                ["response_type"] = "code",
                ["redirect_uri"] = _options.RedirectUri,
                ["scope"] = string.Join(' ', _options.Scopes),
                ["state"] = state,
                ["code_challenge_method"] = "S256",
                ["code_challenge"] = challenge
            });
    }

    public async Task CompleteLoginAsync(string code, string state, CancellationToken cancellationToken)
    {
        ValidateConfiguration();

        if (!_pendingAuthorizations.TryRemove(state, out var pending) ||
            pending.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("Logowanie wygasło. Spróbuj połączyć Spotify ponownie.");
        }

        using var response = await _httpClient.PostAsync(
            "https://accounts.spotify.com/api/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _options.RedirectUri,
                ["code_verifier"] = pending.CodeVerifier
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SpotifyApiException(
                $"Spotify nie zaakceptowało logowania (HTTP {(int)response.StatusCode}).",
                response.StatusCode);
        }

        var tokenResponse = await ReadTokenResponseAsync(response, cancellationToken);

        if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
        {
            throw new SpotifyApiException("Spotify nie zwróciło tokenu dostępu.");
        }

        var tokens = new SpotifyTokens(
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(tokenResponse.ExpiresIn, 60)));

        lock (_tokenGate)
        {
            _tokens = tokens;
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _tokenLock.WaitAsync(cancellationToken);

        try
        {
            SpotifyTokens? current;
            lock (_tokenGate)
            {
                current = _tokens;
            }

            if (current is null)
            {
                throw new InvalidOperationException("Najpierw połącz konto Spotify.");
            }

            if (current.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(45))
            {
                return current.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(current.RefreshToken))
            {
                Logout();
                throw new SpotifyApiException("Sesja Spotify wygasła. Połącz konto ponownie.");
            }

            using var response = await _httpClient.PostAsync(
                "https://accounts.spotify.com/api/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = current.RefreshToken
                }),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Logout();
                throw new SpotifyApiException(
                    $"Nie udało się odświeżyć sesji Spotify (HTTP {(int)response.StatusCode}). Połącz konto ponownie.",
                    response.StatusCode);
            }

            var tokenResponse = await ReadTokenResponseAsync(response, cancellationToken);

            if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
            {
                throw new SpotifyApiException("Spotify nie zwróciło odświeżonego tokenu.");
            }

            var refreshedTokens = new SpotifyTokens(
                tokenResponse.AccessToken,
                tokenResponse.RefreshToken ?? current.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(Math.Max(tokenResponse.ExpiresIn, 60)));

            lock (_tokenGate)
            {
                _tokens = refreshedTokens;
            }

            return refreshedTokens.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public void Logout()
    {
        lock (_tokenGate)
        {
            _tokens = null;
        }
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new InvalidOperationException("Brakuje Spotify:ClientId w konfiguracji aplikacji.");
        }

        if (!Uri.TryCreate(_options.RedirectUri, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Spotify:RedirectUri nie jest poprawnym adresem URL.");
        }
    }

    private static async Task<TokenResponse?> ReadTokenResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TokenResponse>(
                cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new SpotifyApiException(
                "Spotify zwróciło nieprawidłową odpowiedź logowania.",
                response.StatusCode,
                exception);
        }
    }

    private static string CreateUrlSafeRandom(int byteCount) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record PendingAuthorization(string CodeVerifier, DateTimeOffset ExpiresAt);

    private sealed record SpotifyTokens(
        string AccessToken,
        string? RefreshToken,
        DateTimeOffset ExpiresAt);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
