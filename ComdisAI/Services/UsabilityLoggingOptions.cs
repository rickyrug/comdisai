namespace ComdisAI.Services;

public sealed class UsabilityLoggingOptions
{
    public const string SectionName = "UsabilityLogging";

    public bool Enabled { get; set; } = true;
    public string LogsDirectory { get; set; } = "Logs";
    public int RetainedFileCountLimit { get; set; } = 31;
    public bool ExcludeSelfViewsFromRanking { get; set; } = true;

    public string ResolveDirectory(string contentRootPath) =>
        Path.GetFullPath(LogsDirectory, contentRootPath);
}