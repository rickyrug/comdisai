namespace ComdisAI.Services;

public sealed class UsabilityEvent
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string UserId { get; set; } = "ANONYMOUS";
    public string UserName { get; set; } = "ANONYMOUS";
    public string EventSource { get; set; } = "Page";
    public string EventKind { get; set; } = "PageView";
    public string Module { get; set; } = "";
    public string ViewName { get; set; } = "";
    public string? SubView { get; set; }
    public string Route { get; set; } = "";
    public string HandlerOrAction { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public int StatusCode { get; set; }
    public bool Succeeded { get; set; }
    public double DurationMs { get; set; }
    public Dictionary<string, string> DomainIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public interface IUsabilityEventLogger
{
    void Log(UsabilityEvent usabilityEvent);
}