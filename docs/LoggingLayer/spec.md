# Logging layer

Serilog writes diagnostics to the console and daily rolling files, and captures one structured usability event for each MVC controller request. No database or schema changes are required.

## Configuration

`UsabilityLogging:LogsDirectory` defaults to `Logs` relative to the application content root. Absolute paths are supported. The process must have permission to create and write this directory. `UsabilityLogging:RetainedFileCountLimit` defaults to 31 files per sink; `UsabilityLogging:Enabled` defaults to true and can disable usability capture. `UsabilityLogging:ExcludeSelfViewsFromRanking` defaults to true. `Serilog:MinimumLevel` controls event levels.

Files roll daily using the server's local date; timestamps and dashboard filters are UTC. Application diagnostics are written to `app-yyyyMMdd.log` and rendered compact JSON `app-yyyyMMdd.json`. Usability events are written separately to compact JSON `usability-yyyyMMdd.json`; they are not included in the application files. The readers can open files while Serilog writes or rotates them. Incomplete or malformed lines are skipped.

The legacy viewer continues to read historical `log-yyyyMMdd.json` files from `Serilog:File:Directory` (default `logs`); the application no longer writes that format. Its legacy retention setting and 10,000-entry cap remain in effect. Preserve or archive these files according to your operational retention policy.

## Dashboards

`/Admin/Usability` displays filtered activity, rankings, users and individual events. Lookups are hidden by default. Rankings include page and API views; dashboard self-views are excluded when configured. Active users count distinct authenticated IDs. The logged-user filter matches an exact ID or a case-insensitive name fragment.

`/Admin/AppLogs` displays application log levels, sources, rendered messages and expandable exception stack traces. Both dashboards use inclusive UTC date-time filters, preserve filters when paging and require the selected role to have `VIEW_LOGS`. Anonymous requests receive 401 and authenticated users without the permission receive 403. Historical logs remain available at `/Logs/Index` under the same permission.

## Privacy

Usability events capture the request path, principal ID and name, execution outcome, and a restricted set of domain IDs and view identifiers. They do not capture arbitrary query parameters, form values or request bodies. The existing action diagnostic logger redacts common credentials and personal-data fields and caps serialized arguments. Exception messages and diagnostic logs can still contain sensitive information; restrict directory and dashboard access, and use appropriate retention controls. EF Core sensitive-data logging remains disabled.
