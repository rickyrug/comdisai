using System.Text.Json;
using ComdisAI.ViewModels.Logs;
using Microsoft.Extensions.Options;

namespace ComdisAI.Services;

public interface IAppLogService
{
    AppLogDashboard GetDashboard(AppLogDashboard query);
}

public sealed class FileAppLogService(IOptions<UsabilityLoggingOptions> options, IWebHostEnvironment environment) : IAppLogService
{
    public AppLogDashboard GetDashboard(AppLogDashboard query)
    {
        query.PageSize = Math.Clamp(query.PageSize, 1, 200);
        var entries = new List<AppLogEntry>();
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in ClefFiles.Read(options.Value.ResolveDirectory(environment.ContentRootPath), "app", query.From, query.To))
        {
            if (element.TryGetProperty("IsUsabilityEvent", out var flag) && flag.ValueKind == JsonValueKind.True ||
                !DateTimeOffset.TryParse(ClefFiles.Text(element, "@t"), out var timestamp) || !ClefFiles.InRange(timestamp, query.From, query.To))
                continue;
            var source = ClefFiles.Text(element, "SourceContext");
            if (!string.IsNullOrEmpty(source)) sources.Add(source);
            var entry = new AppLogEntry(timestamp, ClefFiles.Text(element, "@l") ?? "Information", ClefFiles.Text(element, "@m") ?? ClefFiles.Text(element, "@mt") ?? "", ClefFiles.Text(element, "@x"), source, ClefFiles.Text(element, "UserId"), ClefFiles.Text(element, "RequestPath"), element.TryGetProperty("StatusCode", out var status) && status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var code) ? code : null, ClefFiles.Text(element, "RequestId"));
            if (!MatchesLevel(entry.Level, query.Level) || !Contains(entry.SourceContext, query.Source) ||
                query.ExceptionsOnly && string.IsNullOrEmpty(entry.Exception) ||
                !string.IsNullOrWhiteSpace(query.Search) && !Contains(entry.Message, query.Search) && !Contains(entry.Exception, query.Search) && !Contains(entry.SourceContext, query.Search))
                continue;
            entries.Add(entry);
        }
        query.Sources = sources.OrderBy(source => source).ToArray();
        query.TotalEntries = entries.Count;
        query.ErrorCount = entries.Count(entry => entry.Level is "Error" or "Fatal");
        query.WarningCount = entries.Count(entry => entry.Level == "Warning");
        query.InformationCount = entries.Count(entry => entry.Level == "Information");
        query.ExceptionCount = entries.Count(entry => !string.IsNullOrEmpty(entry.Exception));
        query.Page = Math.Clamp(query.Page, 1, Math.Max(1, query.TotalPages));
        query.Entries = entries.OrderByDescending(entry => entry.Timestamp).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
        return query;
    }

    private static bool Contains(string? value, string? filter) => string.IsNullOrWhiteSpace(filter) || (value?.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase) ?? false);
    private static bool MatchesLevel(string level, string? filter) => string.IsNullOrWhiteSpace(filter) || filter.Equals("All", StringComparison.OrdinalIgnoreCase) ||
        (filter.Equals("ErrorOrAbove", StringComparison.OrdinalIgnoreCase) ? level is "Error" or "Fatal" :
        filter.Equals("WarningOrAbove", StringComparison.OrdinalIgnoreCase) ? level is "Warning" or "Error" or "Fatal" : level.Equals(filter, StringComparison.OrdinalIgnoreCase));
}