using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SpotiSognless.Models;

namespace SpotiSognless.Services;

public sealed class GameEngine
{
    public const int SongsPerGame = 5;
    public const int MaximumAttempts = 6;

    private static readonly int[] RevealDurationsMs = [1_000, 2_000, 4_000, 7_000, 12_000, 20_000];
    private static readonly int[] AttemptScores = [1_000, 800, 600, 400, 250, 100];

    public IReadOnlyList<SpotifyTrack> CreateDailySet(IEnumerable<SpotifyTrack> tracks, DateOnly date)
    {
        var available = GetPlayableTracks(tracks);
        EnsureEnoughTracks(available.Count);

        var dateKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return available
            .OrderBy(
                track => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{dateKey}:{track.Id}"))),
                StringComparer.Ordinal)
            .Take(SongsPerGame)
            .ToArray();
    }

    public IReadOnlyList<SpotifyTrack> CreatePracticeSet(IEnumerable<SpotifyTrack> tracks)
    {
        var available = GetPlayableTracks(tracks).ToList();
        EnsureEnoughTracks(available.Count);

        for (var index = available.Count - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (available[index], available[swapIndex]) = (available[swapIndex], available[index]);
        }

        return available.Take(SongsPerGame).ToArray();
    }

    public int GetRevealDurationMilliseconds(int attemptsUsed) =>
        RevealDurationsMs[Math.Clamp(attemptsUsed, 0, MaximumAttempts - 1)];

    public int GetScoreForAttempt(int attemptsUsed) =>
        AttemptScores[Math.Clamp(attemptsUsed, 0, MaximumAttempts - 1)];

    public bool IsCorrectGuess(string guess, SpotifyTrack track)
    {
        var normalizedGuess = Normalize(guess);
        if (normalizedGuess.Length == 0)
        {
            return false;
        }

        var artistLine = track.ArtistLine;
        var acceptedAnswers = new[]
        {
            track.Name,
            $"{track.Name} {artistLine}",
            $"{artistLine} {track.Name}"
        };

        return acceptedAnswers.Any(answer => Normalize(answer) == normalizedGuess);
    }

    private static List<SpotifyTrack> GetPlayableTracks(IEnumerable<SpotifyTrack> tracks) =>
        tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.Id) && !string.IsNullOrWhiteSpace(track.Uri))
            .DistinctBy(track => track.Id)
            .ToList();

    private static void EnsureEnoughTracks(int count)
    {
        if (count < SongsPerGame)
        {
            throw new InvalidOperationException(
                $"Potrzeba co najmniej {SongsPerGame} dostępnych utworów. W bibliotece jest ich {count}.");
        }
    }

    private static string Normalize(string value)
    {
        var decomposed = value
            .Normalize(NormalizationForm.FormD)
            .ToLowerInvariant();

        return string.Concat(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark &&
            char.IsLetterOrDigit(character)));
    }
}
