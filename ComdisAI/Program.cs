using ComdisAI.Data;
using ComdisAI.Filters;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var logDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    builder.Configuration["Serilog:File:Directory"] ?? "logs");
var retentionDays = builder.Configuration.GetValue("Serilog:File:RetentionDays", 30);
if (retentionDays < 1)
    throw new InvalidOperationException("Serilog:File:RetentionDays must be greater than zero.");
Directory.CreateDirectory(logDirectory);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "ComdisAI")
        .WriteTo.File(
            new Serilog.Formatting.Json.JsonFormatter(renderMessage: true),
            Path.Combine(logDirectory, "log-.json"),
            rollingInterval: RollingInterval.Day,
            retainedFileTimeLimit: TimeSpan.FromDays(retentionDays),
            retainedFileCountLimit: 1000,
            shared: true);
});

// Add services to the container.
builder.Services.AddScoped<GlobalActionLoggingFilter>();
builder.Services.AddControllersWithViews(options =>
    options.Filters.AddService<GlobalActionLoggingFilter>());
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Authentication/Login";
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IActionPermissionService, ActionPermissionService>();
builder.Services.AddSingleton<LogViewerService>(services =>
    new LogViewerService(
        logDirectory,
        retentionDays,
        services.GetRequiredService<ILogger<LogViewerService>>()));
builder.Services.AddSingleton<SalesOrderPdfService>();
builder.Services.AddSingleton<PurchaseOrderPdfService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<CustomerPaymentService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var initialAdminPassword = app.Configuration["BootstrapAdmin:InitialPassword"];
    if (string.IsNullOrWhiteSpace(initialAdminPassword))
    {
        throw new InvalidOperationException(
            "Configure BootstrapAdmin:InitialPassword through a secret provider before starting the application.");
    }

    await AuthorizationSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
        app.Configuration["BootstrapAdmin:Email"] ?? AuthorizationSeeder.DefaultAdminEmail,
        initialAdminPassword);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} by user {UserId} in role {ActiveRoleId} via {Controller}.{Action} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var principal = httpContext.User;
        diagnosticContext.Set(
            "UserId",
            principal.Identity?.IsAuthenticated == true
                ? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown"
                : "anonymous");
        diagnosticContext.Set(
            "ActiveRoleId",
            principal.FindFirstValue(AuthorizationClaims.ActiveRoleId) ?? "none");
        diagnosticContext.Set(
            "Controller",
            httpContext.Request.RouteValues["controller"]?.ToString() ?? "unknown");
        diagnosticContext.Set(
            "Action",
            httpContext.Request.RouteValues["action"]?.ToString() ?? "unknown");
    };
});

app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
