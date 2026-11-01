namespace ComdisAI.ViewModels.Logs;

public sealed class LogViewerViewModel
{
    public string? SearchText { get; set; }
    public string? Level { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalEntries { get; set; }
    public IReadOnlyList<LogEntryViewModel> Entries { get; set; } = [];
    public int TotalPages => (TotalEntries + PageSize - 1) / PageSize;
}

public sealed class LogEntryViewModel
{
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Exception { get; init; }
}
