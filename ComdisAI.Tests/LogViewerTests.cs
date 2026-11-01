using ComdisAI.Services;
using ComdisAI.ViewModels.Logs;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Parsing;
using Xunit;

namespace ComdisAI.Tests;

public sealed class LogViewerTests
{
    [Fact]
    public void Action_parameter_serialization_redacts_sensitive_fields_and_caps_payload_size()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["id"] = 42,
            ["password"] = "do-not-log",
            ["cancellationToken"] = CancellationToken.None,
            ["input"] = new
            {
                Email = "person@example.test",
                CustomerName = "Private Name",
                Code = "CUS-42"
            }
        };

        var serialized = ActionParameterSerializer.Serialize(parameters);
        using var document = System.Text.Json.JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal(42, root.GetProperty("id").GetInt32());
        Assert.Equal("[REDACTED]", root.GetProperty("password").GetString());
        Assert.False(root.TryGetProperty("cancellationToken", out _));
        Assert.Equal("[REDACTED]", root.GetProperty("input").GetProperty("Email").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("input").GetProperty("CustomerName").GetString());
        Assert.Equal("CUS-42", root.GetProperty("input").GetProperty("Code").GetString());

        var oversized = ActionParameterSerializer.Serialize(new Dictionary<string, object?>
        {
            ["value"] = new string('x', ActionParameterSerializer.MaximumLength * 2)
        });
        Assert.True(oversized.Length <= ActionParameterSerializer.MaximumLength);
        Assert.EndsWith("...[truncated]", oversized);
    }

    [Fact]
    public void GetPage_filters_entries_and_returns_newest_matching_entries_first()
    {
        var directory = CreateLogDirectory();
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            using (var logger = new LoggerConfiguration()
                       .WriteTo.File(
                           new JsonFormatter(renderMessage: true),
                           Path.Combine(directory, "log-.json"),
                           rollingInterval: RollingInterval.Day,
                           retainedFileTimeLimit: TimeSpan.FromDays(30),
                           retainedFileCountLimit: 1000,
                           shared: true)
                       .CreateLogger())
            {
                WriteEvent(logger, today.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc), LogEventLevel.Information, "started");
                WriteEvent(logger, today.ToDateTime(new TimeOnly(9, 1), DateTimeKind.Utc), LogEventLevel.Error, "first failure");
                WriteEvent(logger, today.ToDateTime(new TimeOnly(9, 2), DateTimeKind.Utc), LogEventLevel.Error, "second failure");
                WriteEvent(
                    logger,
                    today.ToDateTime(new TimeOnly(9, 3), DateTimeKind.Utc),
                    LogEventLevel.Error,
                    "third failure with {Detail}",
                    new LogEventProperty("Detail", new ScalarValue("rendered details")));
            }

            var service = new LogViewerService(directory, 30, NullLogger<LogViewerService>.Instance);
            var result = service.GetPage(new LogViewerViewModel
            {
                Level = "error",
                FromDate = today,
                ToDate = today,
                Page = 2,
                PageSize = 2
            });

            Assert.Equal(3, result.TotalEntries);
            Assert.Equal(2, result.TotalPages);
            Assert.Equal(2, result.Page);
            Assert.Single(result.Entries);
            Assert.Equal("first failure", result.Entries[0].Message);
            Assert.Equal("Error", result.Entries[0].Level);

            var latest = service.GetPage(new LogViewerViewModel
            {
                Level = "Error",
                FromDate = today,
                ToDate = today,
                SearchText = "RENDERED DETAILS",
                PageSize = 1
            });
            Assert.Equal("third failure with \"rendered details\"", latest.Entries[0].Message);

            var noMatches = service.GetPage(new LogViewerViewModel
            {
                SearchText = "no matching log text"
            });
            Assert.Empty(noMatches.Entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetPage_skips_malformed_lines_and_handles_reversed_date_ranges()
    {
        var directory = CreateLogDirectory();
        try
        {
            File.AppendAllText(
                Path.Combine(directory, $"log-{DateTime.UtcNow:yyyyMMdd}.json"),
                "{not-json}" + Environment.NewLine);

            var service = new LogViewerService(directory, 30, NullLogger<LogViewerService>.Instance);
            var malformedResult = service.GetPage(new LogViewerViewModel());
            var reversedRangeResult = service.GetPage(new LogViewerViewModel
            {
                FromDate = new DateOnly(2026, 11, 2),
                ToDate = new DateOnly(2026, 11, 1)
            });

            Assert.Empty(malformedResult.Entries);
            Assert.Empty(reversedRangeResult.Entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteEvent(
        Serilog.ILogger logger,
        DateTime timestamp,
        LogEventLevel level,
        string message,
        params LogEventProperty[] properties)
    {
        var logEvent = new LogEvent(
            new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
            level,
            null,
            new MessageTemplateParser().Parse(message),
            properties);
        logger.Write(logEvent);
    }

    private static string CreateLogDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"comdisai-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
