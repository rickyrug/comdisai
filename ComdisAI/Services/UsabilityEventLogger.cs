using Serilog.Context;

namespace ComdisAI.Services;

public sealed class UsabilityEventLogger(ILogger<UsabilityEventLogger> logger) : IUsabilityEventLogger
{
    public void Log(UsabilityEvent entry)
    {
        using (LogContext.PushProperty("IsUsabilityEvent", true))
        {
            logger.LogInformation("Usability event {TimestampUtc} {UserId} {UserName} {EventSource} {EventKind} {Module} {ViewName} {SubView} {Route} {HandlerOrAction} {HttpMethod} {StatusCode} {Succeeded} {DurationMs} {@DomainIds}",
                entry.TimestampUtc, entry.UserId, entry.UserName, entry.EventSource, entry.EventKind,
                entry.Module, entry.ViewName, entry.SubView, entry.Route, entry.HandlerOrAction,
                entry.HttpMethod, entry.StatusCode, entry.Succeeded, entry.DurationMs, entry.DomainIds);
        }
    }
}