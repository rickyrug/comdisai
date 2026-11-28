using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Services;
using ComdisAI.ViewModels.Logs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ComdisAI.Tests;

public sealed class UsabilityLoggingTests
{
    [Fact]
    public async Task Requests_and_exceptions_are_separated_and_dashboards_enforce_access()
    {
        using var factory = new LoggingFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Admin/Usability")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Admin/AppLogs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Authentication/Login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tracking-test/view?siteId=12&tab=summary&password=private")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tracking-test/options")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tracking-test/download")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tracking-test/receipt")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/tracking-test/change", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/tracking-test/search", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/tracking-test/bad")).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync("/api/tracking-test/fail")).StatusCode);

        client.DefaultRequestHeaders.Add("X-Test-Role", "99999");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/Usability")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var roleId = await db.Roles.Where(role => role.Code == AuthorizationSeeder.AdminRoleCode).Select(role => role.Id).SingleAsync();
            client.DefaultRequestHeaders.Add("X-Test-Role", roleId.ToString());
        }
        var appLogs = await client.GetAsync("/Admin/AppLogs");
        Assert.Equal(HttpStatusCode.OK, appLogs.StatusCode);
        var html = await appLogs.Content.ReadAsStringAsync();
        Assert.Contains("View exception &amp; stack trace", html);
        Assert.Contains("tracking test failure", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Usability")).StatusCode);

        var events = Directory.GetFiles(factory.LogDirectory, "usability-*.json").SelectMany(File.ReadLines)
            .Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.Contains(events, document => document.RootElement.GetProperty("Route").GetString() == "/Authentication/Login" && document.RootElement.GetProperty("EventKind").GetString() == "PageView");
            Assert.Contains(events, document => document.RootElement.GetProperty("Route").GetString() == "/api/tracking-test/view" && document.RootElement.GetProperty("EventKind").GetString() == "ApiView");
            Assert.Contains(events, document => document.RootElement.GetProperty("Route").GetString() == "/api/tracking-test/view" && document.RootElement.GetProperty("SubView").GetString() == "summary" && document.RootElement.GetProperty("DomainIds").GetProperty("siteId").GetString() == "12");
            Assert.DoesNotContain(events, document => document.RootElement.ToString().Contains("private", StringComparison.Ordinal));
            Assert.Contains(events, document => document.RootElement.GetProperty("Route").GetString() == "/api/tracking-test/options" && document.RootElement.GetProperty("EventKind").GetString() == "ApiLookup");
            Assert.Contains(events, document => document.RootElement.GetProperty("Route").GetString() == "/api/tracking-test/download" && document.RootElement.GetProperty("EventKind").GetString() == "Export");
            Assert.Contains(events, document => document.RootElement.GetProperty("Route").GetString() == "/api/tracking-test/receipt" && document.RootElement.GetProperty("EventKind").GetString() == "Export");
            Assert.Contains(events, document => document.RootElement.GetProperty("Route").GetString() == "/api/tracking-test/change" && document.RootElement.GetProperty("EventKind").GetString() == "Mutation");
            Assert.Contains(events, document => document.RootElement.GetProperty("Route").GetString() == "/api/tracking-test/search" && document.RootElement.GetProperty("EventKind").GetString() == "ApiView");
            Assert.Contains(events, document => document.RootElement.GetProperty("Route").GetString() == "/api/tracking-test/bad" && document.RootElement.GetProperty("StatusCode").GetInt32() == 400 && !document.RootElement.GetProperty("Succeeded").GetBoolean());
            Assert.Single(events, document => document.RootElement.GetProperty("Route").GetString() == "/api/tracking-test/fail" && document.RootElement.GetProperty("StatusCode").GetInt32() == 500);
            Assert.DoesNotContain(events, document => document.RootElement.GetProperty("Route").GetString() == "/Home/Error");
        }
        finally { foreach (var document in events) document.Dispose(); }
        var appText = string.Join('\n', Directory.GetFiles(factory.LogDirectory, "app-*.json").SelectMany(File.ReadLines));
        Assert.Contains("tracking test failure", appText);
        Assert.DoesNotContain("\"IsUsabilityEvent\":true", appText);
        Assert.Contains("tracking test failure", string.Join('\n', Directory.GetFiles(factory.LogDirectory, "app-*.log").SelectMany(File.ReadLines)));
    }

    [Fact]
    public async Task Disabled_tracking_does_not_create_usability_files()
    {
        using var factory = new LoggingFactory(enabled: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Authentication/Login")).StatusCode);
        Assert.Empty(Directory.GetFiles(factory.LogDirectory, "usability-*.json"));
        Assert.NotEmpty(Directory.GetFiles(factory.LogDirectory, "app-*.json"));
    }

    [Fact]
    public void Readers_filter_and_rank_while_serilog_files_are_open()
    {
        var directory = Path.Combine(Path.GetTempPath(), "comdisai-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new UsabilityLoggingOptions { LogsDirectory = directory });
            var environment = new TestEnvironment { ContentRootPath = directory };
            using var writer = new StreamWriter(new FileStream(Path.Combine(directory, $"usability-{DateTime.UtcNow:yyyyMMdd}.json"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
            var now = DateTimeOffset.UtcNow;
            writer.WriteLine(JsonSerializer.Serialize(new UsabilityEvent { TimestampUtc = now, UserId = "1", UserName = "Alice", Module = "Sales", ViewName = "Orders", EventKind = "PageView", Route = "/SalesOrders", DurationMs = 10 }));
            writer.WriteLine(JsonSerializer.Serialize(new UsabilityEvent { TimestampUtc = now.AddSeconds(-1), UserId = "2", UserName = "Bob", Module = "Sales", ViewName = "Orders", EventKind = "PageView", Route = "/SalesOrders", DurationMs = 20 }));
            writer.WriteLine(JsonSerializer.Serialize(new UsabilityEvent { TimestampUtc = now, UserId = "1", UserName = "Alice", Module = "Admin", ViewName = "Admin", EventKind = "PageView", Route = "/Admin/Usability" }));
            writer.WriteLine(JsonSerializer.Serialize(new UsabilityEvent { TimestampUtc = now, UserId = "10", UserName = "Charlie", Module = "Sales", ViewName = "Options", EventKind = "ApiLookup", EventSource = "Api", Route = "/api/options" }));
            writer.WriteLine("{incomplete");
            var analytics = new FileUsabilityAnalyticsService(options, environment);
            var result = analytics.GetDashboard(new UsabilityDashboard { From = now.AddMinutes(-1), To = now.AddMinutes(1), PageSize = 1 });
            Assert.Equal(3, result.TotalEvents);
            Assert.Equal(2, result.ActiveUsers);
            Assert.Single(result.Views);
            Assert.Equal(2, result.Views[0].Hits);
            Assert.Single(result.Events);
            Assert.Equal(2, analytics.GetDashboard(new UsabilityDashboard { User = "Alice" }).TotalEvents);
            Assert.Equal(2, analytics.GetDashboard(new UsabilityDashboard { User = "1", IncludeLookups = true }).TotalEvents);
            Assert.Equal(1, analytics.GetDashboard(new UsabilityDashboard { Source = "Api", IncludeLookups = true }).TotalEvents);
            Assert.Empty(analytics.GetDashboard(new UsabilityDashboard { From = now.AddDays(1), To = now.AddDays(2) }).Events);

            using var appWriter = new StreamWriter(new FileStream(Path.Combine(directory, $"app-{DateTime.UtcNow:yyyyMMdd}.json"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
            appWriter.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["@t"] = now, ["@l"] = "Error", ["@m"] = "failure details", ["@x"] = "stack trace", ["SourceContext"] = "Example.Component" }));
            appWriter.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["@t"] = now, ["@l"] = "Warning", ["@m"] = "warning" }));
            var logs = new FileAppLogService(options, environment);
            var error = logs.GetDashboard(new AppLogDashboard { Level = "ErrorOrAbove", Search = "DETAILS", ExceptionsOnly = true });
            Assert.Equal(1, error.ErrorCount);
            Assert.Equal("stack trace", Assert.Single(error.Entries).Exception);
            Assert.Equal(2, logs.GetDashboard(new AppLogDashboard { Level = "WarningOrAbove" }).TotalEntries);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class LoggingFactory : WebApplicationFactory<Program>
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "comdisai-host-" + Guid.NewGuid().ToString("N"));
        public string LogDirectory => Path.Combine(root, "logs");

        private readonly bool enabled;

        public LoggingFactory(bool enabled = true)
        {
            this.enabled = enabled;
            Directory.CreateDirectory(root);
            using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={Path.Combine(root, "test.db")}").Options, new HttpContextAccessor());
            db.Database.EnsureCreated();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UsabilityLogging:LogsDirectory"] = LogDirectory,
                ["UsabilityLogging:Enabled"] = enabled.ToString(),
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={Path.Combine(root, "test.db")}",
                ["BootstrapAdmin:InitialPassword"] = "test-password",
                ["BootstrapAdmin:Email"] = "test@example.test"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddControllersWithViews().AddApplicationPart(typeof(TrackingTestController).Assembly);
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = "Test");
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

public sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Name, "Test Admin"), new Claim(AuthorizationClaims.ActiveRoleId, role.ToString())], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

[ApiController]
[AllowAnonymous]
[Route("api/tracking-test")]
public sealed class TrackingTestController : ControllerBase
{
    [HttpGet("view")] public IActionResult ViewDataEndpoint() => Ok(new { Value = 1 });
    [HttpGet("options")] public IActionResult Options() => Ok(Array.Empty<string>());
    [HttpGet("download")] public IActionResult Download() => File([1, 2, 3], "application/octet-stream");
    [HttpGet("receipt")] public IActionResult Receipt() => File([1, 2, 3], "application/octet-stream");
    [HttpPost("change")] public IActionResult Change() => Ok();
    [HttpPost("search")] public IActionResult Search() => Ok();
    [HttpGet("bad")] public IActionResult Bad() => BadRequest();
    [HttpGet("fail")] public IActionResult Fail() => throw new InvalidOperationException("tracking test failure");
}