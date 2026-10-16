using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SpotiSognless.Models;

namespace SpotiSognless.Services;

public sealed class GameEngine
{
    public const int SongsPerGame = 5;
    public const int MaximumAttempts = 7;

    private static readonly int[] RevealDurationsMs = [100, 500, 1_000, 2_000, 4_000, 8_000, 16_000];
    private static readonly int[] AttemptScores = [1_000, 850, 700, 550, 400, 250, 100];

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

    public IReadOnlyList<SpotifyTrack> FindSuggestions(
        IEnumerable<SpotifyTrack> tracks,
        string query,
        int limit = 6)
    {
        if (limit <= 0)
        {
            return [];
        }

        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length < 2)
        {
            return [];
        }

        var maximumFuzzyDistance = Math.Clamp(normalizedQuery.Length / 4, 1, 3);
        var matches = new List<SuggestionMatch>();

        foreach (var track in tracks)
        {
            var score = GetExactSuggestionScore(normalizedQuery, track);
            if (score == int.MaxValue && normalizedQuery.Length <= 64)
            {
                score = GetFuzzySuggestionScore(normalizedQuery, track, maximumFuzzyDistance);
            }

            if (score < int.MaxValue)
            {
                matches.Add(new SuggestionMatch(track, score));
            }
        }

        return matches
            .OrderBy(match => match.Score)
            .ThenBy(match => match.Track.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(match => match.Track)
            .ToArray();
    }

    private static List<SpotifyTrack> GetPlayableTracks(IEnumerable<SpotifyTrack> tracks) =>
        tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.Id) && !string.IsNullOrWhiteSpace(track.Uri))
            .DistinctBy(track => track.Id)
            .ToList();

    private static int GetExactSuggestionScore(string query, SpotifyTrack track)
    {
        var bestScore = int.MaxValue;
        foreach (var candidate in GetAnswerFields(track))
        {
            var normalizedCandidate = Normalize(candidate);
            if (normalizedCandidate.StartsWith(query, StringComparison.Ordinal))
            {
                return 0;
            }

            var position = normalizedCandidate.IndexOf(query, StringComparison.Ordinal);
            if (position >= 0)
            {
                bestScore = Math.Min(bestScore, 10 + position);
            }
        }

        return bestScore;
    }

    private static int GetFuzzySuggestionScore(
        string query,
        SpotifyTrack track,
        int maximumDistance)
    {
        var bestScore = int.MaxValue;
        var minimumLength = Math.Max(1, query.Length - maximumDistance);
        var maximumLength = query.Length + maximumDistance;

        foreach (var candidate in GetAnswerFields(track))
        {
            var normalizedCandidate = Normalize(candidate);
            if (normalizedCandidate.Length == 0)
            {
                continue;
            }

            if (Math.Abs(query.Length - normalizedCandidate.Length) <= maximumDistance)
            {
                var distance = GetLevenshteinDistance(query, normalizedCandidate, maximumDistance);
                if (distance <= maximumDistance)
                {
                    bestScore = Math.Min(bestScore, 40 + distance * 10);
                }
            }

            var prefixLength = Math.Min(normalizedCandidate.Length, maximumLength);
            if (prefixLength < minimumLength)
            {
                continue;
            }

            for (var length = minimumLength; length <= prefixLength; length++)
            {
                var distance = GetLevenshteinDistance(
                    query,
                    normalizedCandidate[..length],
                    maximumDistance);
                if (distance <= maximumDistance)
                {
                    bestScore = Math.Min(bestScore, 40 + distance * 10 + Math.Abs(query.Length - length));
                }
            }
        }

        return bestScore;
    }

    private static IEnumerable<string> GetAnswerFields(SpotifyTrack track)
    {
        yield return track.Name;
        yield return track.ArtistLine;
        yield return track.DisplayName;
    }

    private static int GetLevenshteinDistance(string left, string right, int maximumDistance)
    {
        if (Math.Abs(left.Length - right.Length) > maximumDistance)
        {
            return maximumDistance + 1;
        }

        var outsideDistance = maximumDistance + 1;
        var previousRow = new int[right.Length + 1];
        var currentRow = new int[right.Length + 1];
        Array.Fill(previousRow, outsideDistance);
        for (var column = 0; column <= right.Length; column++)
        {
            if (column > maximumDistance)
            {
                break;
            }

            previousRow[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            var firstColumn = Math.Max(1, row - maximumDistance);
            var lastColumn = Math.Min(right.Length, row + maximumDistance);
            currentRow[0] = row <= maximumDistance ? row : outsideDistance;
            if (firstColumn > 1)
            {
                currentRow[firstColumn - 1] = outsideDistance;
            }

            var rowMinimum = currentRow[0];
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                currentRow[column] = Math.Min(
                    Math.Min(previousRow[column] + 1, currentRow[column - 1] + 1),
                    previousRow[column - 1] + substitutionCost);
                rowMinimum = Math.Min(rowMinimum, currentRow[column]);
            }

            if (lastColumn < right.Length)
            {
                currentRow[lastColumn + 1] = outsideDistance;
            }

            if (rowMinimum > maximumDistance)
            {
                return outsideDistance;
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[right.Length] <= maximumDistance
            ? previousRow[right.Length]
            : outsideDistance;
    }

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

    private sealed record SuggestionMatch(SpotifyTrack Track, int Score);
}
