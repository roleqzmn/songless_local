using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpotiSognless.Models;

namespace SpotiSognless.Services;

public sealed class SpotifyApiClient
{
    private const int PageSize = 50;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly SpotifyAuthService _authService;

    public SpotifyApiClient(HttpClient httpClient, SpotifyAuthService authService)
    {
        _httpClient = httpClient;
        _authService = authService;
    }

    public async Task<IReadOnlyList<SpotifyTrack>> GetSavedTracksAsync(
        CancellationToken cancellationToken = default)
    {
        var tracks = new List<SpotifyTrack>();
        var offset = 0;

        while (true)
        {
            var accessToken = await _authService.GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.spotify.com/v1/me/tracks?limit={PageSize}&offset={offset}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _authService.Logout();
                    throw new SpotifyApiException(
                        "Spotify odrzuciło sesję. Połącz konto ponownie.",
                        response.StatusCode);
                }

                throw new SpotifyApiException(
                    $"Nie udało się pobrać polubionych utworów (HTTP {(int)response.StatusCode}).",
                    response.StatusCode);
            }

            SavedTracksPage? page;
            try
            {
                page = await response.Content.ReadFromJsonAsync<SavedTracksPage>(
                    JsonOptions,
                    cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new SpotifyApiException(
                    "Spotify zwróciło nieprawidłową odpowiedź z biblioteką utworów.",
                    response.StatusCode,
                    exception);
            }

            if (page?.Items is null)
            {
                throw new SpotifyApiException("Spotify zwróciło nieprawidłową listę utworów.");
            }

            foreach (var item in page.Items)
            {
                var track = item.Track;
                if (track is null ||
                    string.IsNullOrWhiteSpace(track.Id) ||
                    string.IsNullOrWhiteSpace(track.Uri) ||
                    string.IsNullOrWhiteSpace(track.Name) ||
                    track.IsLocal ||
                    track.IsPlayable == false)
                {
                    continue;
                }

                var artists = track.Artists?
                    .Select(artist => artist.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .ToArray() ?? [];

                if (artists.Length == 0)
                {
                    continue;
                }

                tracks.Add(new SpotifyTrack(
                    track.Id,
                    track.Uri,
                    track.Name,
                    artists,
                    track.Album?.Name,
                    track.Album?.Images?.FirstOrDefault()?.Url,
                    track.DurationMs));
            }

            if (string.IsNullOrWhiteSpace(page.Next) || page.Items.Count == 0)
            {
                break;
            }

            offset += page.Items.Count;
        }

        return tracks.DistinctBy(track => track.Id).ToArray();
    }

    private sealed class SavedTracksPage
    {
        public List<SavedTrackItem>? Items { get; init; }

        public string? Next { get; init; }
    }

    private sealed class SavedTrackItem
    {
        public TrackPayload? Track { get; init; }
    }

    private sealed class TrackPayload
    {
        public string? Id { get; init; }

        public string? Uri { get; init; }

        public string? Name { get; init; }

        public List<ArtistPayload>? Artists { get; init; }

        public AlbumPayload? Album { get; init; }

        [JsonPropertyName("is_local")]
        public bool IsLocal { get; init; }

        [JsonPropertyName("is_playable")]
        public bool? IsPlayable { get; init; }

        [JsonPropertyName("duration_ms")]
        public int DurationMs { get; init; }
    }

    private sealed class ArtistPayload
    {
        public string? Name { get; init; }
    }

    private sealed class AlbumPayload
    {
        public string? Name { get; init; }

        public List<ImagePayload>? Images { get; init; }
    }

    private sealed class ImagePayload
    {
        public string? Url { get; init; }
    }
}
