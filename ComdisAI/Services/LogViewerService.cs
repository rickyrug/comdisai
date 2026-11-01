using System.Globalization;
using System.Text.Json;
using ComdisAI.ViewModels.Logs;

namespace ComdisAI.Services;

public sealed class LogViewerService(
    string logDirectory,
    int retentionDays,
    ILogger<LogViewerService> logger)
{
    public const int MaximumEntries = 10_000;
    public const int MaximumPageSize = 200;
    public const int MaximumSearchLength = 200;
    private const int MaximumFiles = 1_000;

    public LogViewerViewModel GetPage(LogViewerViewModel query)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var entries = new Queue<LogEntryViewModel>(MaximumEntries);
        var fromUtc = query.FromDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = query.ToDate?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var retentionCutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);
        var searchText = NormalizeSearchText(query.SearchText);

        if ((fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value) ||
            !Directory.Exists(logDirectory))
        {
            return EmptyPage(query, pageSize, searchText);
        }

        var files = Directory.EnumerateFiles(logDirectory, "log-*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => file.LastWriteTimeUtc >= retentionCutoffUtc)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaximumFiles)
            .OrderBy(file => file.LastWriteTimeUtc);

        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file.FullName))
            {
                var entry = ParseEntry(line, file.Name);
                if (entry is null ||
                    (fromUtc.HasValue && entry.Timestamp.UtcDateTime < fromUtc.Value) ||
                    (toUtc.HasValue && entry.Timestamp.UtcDateTime > toUtc.Value) ||
                    (!string.IsNullOrWhiteSpace(query.Level) &&
                     !string.Equals(entry.Level, query.Level.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                    (searchText is not null && !MatchesSearch(entry, searchText)))
                {
                    continue;
                }

                if (entries.Count == MaximumEntries)
                    entries.Dequeue();
                entries.Enqueue(entry);
            }
        }

        var orderedEntries = entries
            .OrderByDescending(entry => entry.Timestamp)
            .ToArray();
        var totalEntries = orderedEntries.Length;
        var totalPages = (totalEntries + pageSize - 1) / pageSize;
        var page = Math.Clamp(query.Page, 1, Math.Max(totalPages, 1));

        return new LogViewerViewModel
        {
            SearchText = searchText,
            Level = NormalizeLevel(query.Level),
            FromDate = query.FromDate,
            ToDate = query.ToDate,
            Page = page,
            PageSize = pageSize,
            TotalEntries = totalEntries,
            Entries = orderedEntries
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray()
        };
    }

    private LogEntryViewModel? ParseEntry(string line, string fileName)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGetString(root, "Timestamp", out var timestampText) ||
                !DateTimeOffset.TryParse(
                    timestampText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var timestamp))
            {
                logger.LogWarning("Skipping a log entry with an invalid timestamp in {LogFileName}", fileName);
                return null;
            }

            var level = TryGetString(root, "Level", out var levelText) ? levelText : "Information";
            var message = TryGetString(root, "RenderedMessage", out var renderedMessage)
                ? renderedMessage
                : TryGetString(root, "MessageTemplate", out var messageTemplate)
                    ? messageTemplate
                    : string.Empty;
            var exception = TryGetString(root, "Exception", out var exceptionText)
                ? exceptionText
                : null;

            return new LogEntryViewModel
            {
                Timestamp = timestamp,
                Level = level,
                Message = message,
                Exception = exception
            };
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Skipping a malformed log entry in {LogFileName}",
                fileName);
            return null;
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string? NormalizeLevel(string? level) =>
        string.IsNullOrWhiteSpace(level) ? null : level.Trim();

    private static string? NormalizeSearchText(string? searchText)
    {
        var normalized = searchText?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;

        return normalized.Length <= MaximumSearchLength
            ? normalized
            : normalized[..MaximumSearchLength];
    }

    private static bool MatchesSearch(LogEntryViewModel entry, string searchText) =>
        entry.Message.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
        entry.Level.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
        (entry.Exception?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false);

    private static LogViewerViewModel EmptyPage(
        LogViewerViewModel query,
        int pageSize,
        string? searchText) =>
        new()
        {
            SearchText = searchText,
            Level = NormalizeLevel(query.Level),
            FromDate = query.FromDate,
            ToDate = query.ToDate,
            Page = 1,
            PageSize = pageSize
        };
}
