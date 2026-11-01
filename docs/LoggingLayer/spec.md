# Logging layer

The application uses Serilog as the host logger and continues to use `ILogger<T>` in application code. ASP.NET Core request summaries and application log events are written as one JSON event per line to daily rolling files under the configured log directory. Request events include the authenticated user ID, active role ID, MVC controller/action, HTTP method/path, response status, and elapsed time. A global MVC action filter additionally records each executed controller action, actor and active role, UTC start time, elapsed time, result type, and serialized action arguments. JSON events include rendered messages so the viewer displays structured property values instead of unexpanded message-template placeholders.

## Configuration

`Serilog:MinimumLevel` controls event levels. `Serilog:File:Directory` is relative to the application content root, and `Serilog:File:RetentionDays` sets the rolling-file age limit. The defaults are `logs` and 30 days. Log files are named `log-yyyyMMdd.json`; the sink retains at most 1,000 files as an additional safety bound.

## Viewer

The `Logs` page is available only to roles granted the `VIEW_LOGS` authorization action. The bootstrap administrator role receives this action through the authorization seeder. Users can search message/exception text, filter by level and UTC date range, and page through matching entries. Search is case-insensitive and capped at 200 characters. For bounded work and responses, the viewer reads at most 1,000 files and retains at most the 10,000 most recent matching entries.

Action arguments are capped at 4,000 characters, cyclic references are ignored, and common credentials and personal-data fields (including passwords, tokens, emails, phone numbers, addresses, and names) are redacted. Log message text and exception details are HTML-encoded by Razor. EF Core sensitive-data logging remains disabled; do not write passwords, authentication tokens, request bodies, query values, or other sensitive values to logs.
