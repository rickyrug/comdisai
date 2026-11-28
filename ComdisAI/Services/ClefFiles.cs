using System.Globalization;
using System.Text.Json;

namespace ComdisAI.Services;

internal static class ClefFiles
{
    public static IEnumerable<JsonElement> Read(string directory, string prefix, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (!Directory.Exists(directory) || from > to)
            yield break;
        IEnumerable<string> paths;
        try { paths = Directory.GetFiles(directory, prefix + "-*.json"); }
        catch (IOException) { yield break; }
        foreach (var path in paths.OrderByDescending(path => path, StringComparer.Ordinal))
        {
            var suffix = Path.GetFileNameWithoutExtension(path)[(prefix.Length + 1)..];
            if (!DateOnly.TryParseExact(suffix, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;
            if (from.HasValue && date.DayNumber < DateOnly.FromDateTime(from.Value.UtcDateTime).DayNumber - 2 ||
                to.HasValue && date.DayNumber > DateOnly.FromDateTime(to.Value.UtcDateTime).DayNumber + 2)
                continue;
            StreamReader reader;
            try { reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            using (reader)
            {
                while (true)
                {
                    string? line;
                    try { line = reader.ReadLine(); }
                    catch (IOException) { break; }
                    if (line is null) break;
                    JsonElement element;
                    try
                    {
                        using var document = JsonDocument.Parse(line);
                        element = document.RootElement.Clone();
                    }
                    catch (JsonException) { continue; }
                    if (element.ValueKind == JsonValueKind.Object)
                        yield return element;
                }
            }
        }
    }

    public static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static bool InRange(DateTimeOffset timestamp, DateTimeOffset? from, DateTimeOffset? to) =>
        (!from.HasValue || timestamp >= from) && (!to.HasValue || timestamp <= to);
}