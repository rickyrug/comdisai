using System.Text.Json;
using ComdisAI.ViewModels.Logs;
using Microsoft.Extensions.Options;

namespace ComdisAI.Services;

public interface IUsabilityAnalyticsService
{
    UsabilityDashboard GetDashboard(UsabilityDashboard query);
}

public sealed class FileUsabilityAnalyticsService(IOptions<UsabilityLoggingOptions> options, IWebHostEnvironment environment) : IUsabilityAnalyticsService
{
    public UsabilityDashboard GetDashboard(UsabilityDashboard query)
    {
        query.PageSize = Math.Clamp(query.PageSize, 1, 200);
        var entries = new List<UsabilityEvent>();
        foreach (var element in ClefFiles.Read(options.Value.ResolveDirectory(environment.ContentRootPath), "usability", query.From, query.To))
        {
            if (!DateTimeOffset.TryParse(ClefFiles.Text(element, "TimestampUtc"), out _))
                continue;
            UsabilityEvent? entry;
            try { entry = element.Deserialize<UsabilityEvent>(); }
            catch (JsonException) { continue; }
            if (entry is null || entry.UserId is null || entry.UserName is null || entry.Module is null || entry.ViewName is null || entry.Route is null || !ClefFiles.InRange(entry.TimestampUtc, query.From, query.To) ||
                !string.IsNullOrWhiteSpace(query.User) && !string.Equals(entry.UserId, query.User.Trim(), StringComparison.OrdinalIgnoreCase) && !Contains(entry.UserName, query.User) ||
                !Contains(entry.Module, query.Module) ||
                !string.IsNullOrWhiteSpace(query.Source) && !query.Source.Equals("All", StringComparison.OrdinalIgnoreCase) && !query.Source.Equals(entry.EventSource, StringComparison.OrdinalIgnoreCase) ||
                !query.IncludeLookups && entry.EventKind == "ApiLookup")
                continue;
            entries.Add(entry);
        }
        query.TotalEvents = entries.Count;
        query.ActiveUsers = entries.Where(entry => entry.UserId != "ANONYMOUS").Select(entry => entry.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        query.AverageResponseMs = entries.Count == 0 ? 0 : entries.Average(entry => entry.DurationMs);
        query.TopModule = entries.GroupBy(entry => entry.Module).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).FirstOrDefault()?.Key;
        var views = entries.Where(entry => (entry.EventKind is "PageView" or "ApiView") &&
            (!options.Value.ExcludeSelfViewsFromRanking || !IsDashboard(entry))).ToList();
        query.Views = views.GroupBy(entry => (entry.Module, entry.ViewName, entry.SubView))
            .Select(group => new ViewRanking(group.Key.Module, group.Key.ViewName, group.Key.SubView, group.Count(),
                group.Select(entry => entry.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                views.Count == 0 ? 0 : 100d * group.Count() / views.Count, group.Average(entry => entry.DurationMs), group.Max(entry => entry.TimestampUtc)))
            .OrderByDescending(view => view.Hits).ThenBy(view => view.View).ToArray();
        query.Users = entries.GroupBy(entry => entry.UserId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new UserActivity(group.Key, group.Last().UserName, group.Count(),
                group.Select(entry => (entry.Module, entry.ViewName, entry.SubView)).Distinct().Count(),
                group.GroupBy(entry => entry.ViewName).OrderByDescending(view => view.Count()).ThenBy(view => view.Key).FirstOrDefault()?.Key,
                group.Max(entry => entry.TimestampUtc)))
            .OrderByDescending(user => user.Events).ThenBy(user => user.UserId).ToArray();
        query.Page = Math.Clamp(query.Page, 1, Math.Max(1, query.TotalPages));
        query.Events = entries.OrderByDescending(entry => entry.TimestampUtc).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
        return query;
    }

    private static bool Contains(string? value, string? filter) => string.IsNullOrWhiteSpace(filter) || (value?.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase) ?? false);
    private static bool IsDashboard(UsabilityEvent entry) => entry.Route.StartsWith("/Admin/Usability", StringComparison.OrdinalIgnoreCase) || entry.Route.StartsWith("/Admin/AppLogs", StringComparison.OrdinalIgnoreCase) || entry.Route.StartsWith("/Logs", StringComparison.OrdinalIgnoreCase);
}