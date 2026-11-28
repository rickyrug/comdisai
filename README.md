# ComdisAI

## Application logs

By default Serilog writes daily logs under `ComdisAI/Logs/`, relative to the application content root:

- `app-yyyyMMdd.log`: human-readable application diagnostics.
- `app-yyyyMMdd.json`: rendered compact JSON diagnostics, including exceptions.
- `usability-yyyyMMdd.json`: structured MVC and API usability events.

Configure the directory, retention and tracking with the `UsabilityLogging` settings in `ComdisAI/appsettings.json`. Relative directories are resolved against the content root; the default retention is 31 files per sink. The application process needs write access to this directory. Logs may contain user identities and exception details, so restrict filesystem access.

Roles with `VIEW_LOGS` can use `/Admin/Usability` and `/Admin/AppLogs`. Dashboard date-time filters are UTC. The historical viewer at `/Logs/Index` reads old `log-yyyyMMdd.json` files from the separately configured `Serilog:File:Directory`; new events are not written to that format.

See [logging documentation](docs/LoggingLayer/spec.md) for configuration and privacy details.