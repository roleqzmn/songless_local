namespace SpotiSognless.Models;

public sealed record SpotifyTrack(
    string Id,
    string Uri,
    string Name,
    IReadOnlyList<string> Artists,
    string? AlbumName,
    string? ImageUrl,
    int DurationMs)
{
    public string ArtistLine => string.Join(", ", Artists);

    public string DisplayName => $"{Name} — {ArtistLine}";
}
