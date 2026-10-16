namespace SpotiSognless.Services;

public sealed class SpotifyOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = [];
}
