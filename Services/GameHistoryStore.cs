using System.Text.Json;

namespace SpotiSognless.Services;

public sealed class GameHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotiSognless",
        "game-history.json");
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public async Task<GameStatistics> GetStatisticsAsync(
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var results = await ReadResultsAsync(cancellationToken);
            var playedDates = results.Select(result => result.Date).ToHashSet();
            var streakDate = playedDates.Contains(today) ? today : today.AddDays(-1);
            var streak = 0;

            while (playedDates.Contains(streakDate))
            {
                streak++;
                streakDate = streakDate.AddDays(-1);
            }

            var latestResult = results.OrderByDescending(result => result.Date).FirstOrDefault();
            return new GameStatistics(
                streak,
                results.Count == 0 ? 0 : results.Max(result => result.Score),
                latestResult?.Score);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task RecordDailyResultAsync(
        DateOnly date,
        int score,
        int maximumScore,
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var results = await ReadResultsAsync(cancellationToken);
            var result = new GameHistoryEntry(date, score, maximumScore);
            var existingIndex = results.FindIndex(entry => entry.Date == date);

            if (existingIndex >= 0)
            {
                results[existingIndex] = result;
            }
            else
            {
                results.Add(result);
            }

            results.Sort((left, right) => left.Date.CompareTo(right.Date));

            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("Nie można ustalić katalogu historii gry.");
            Directory.CreateDirectory(directory);

            var temporaryPath = $"{_filePath}.tmp";
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(results, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<List<GameHistoryEntry>> ReadResultsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
        return JsonSerializer.Deserialize<List<GameHistoryEntry>>(json, JsonOptions)
            ?? throw new InvalidDataException("Plik historii gry zawiera nieprawidłowe dane.");
    }
}

public sealed record GameHistoryEntry(DateOnly Date, int Score, int MaximumScore);

public sealed record GameStatistics(int CurrentStreak, int BestScore, int? LastScore);
