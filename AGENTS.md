# AGENTS.md

Instructions for AI coding agents working on this repository.

## Project overview

Hookpipe is a config-driven webhook gateway built with .NET 8. It receives HTTP webhooks on configured endpoints, validates them, wraps them in a standardized message envelope, and routes them to message sinks (SQS, Kafka, RabbitMQ, stdout).

## Architecture

```
src/
  Hookpipe.API/              # ASP.NET Core host, minimal API, Program.cs entry point
  Hookpipe.Core/             # All core logic
    Config/                  # YAML config models, ConfigLoader
    Models/                  # MessageEnvelope
    Services/                # EnvelopeBuilder
    Sinks/                   # ISink interface, SinkFactory, implementations
    Validation/              # IValidator interface, ValidatorFactory, implementations
config/
  hookpipe.yaml              # Sample/dev configuration
tests/                       # Test projects
```

Two projects only: `Hookpipe.API` (host) and `Hookpipe.Core` (everything else). Sinks and validators live in Core — do not create separate projects for them.

## Key patterns

- **Config-driven**: All endpoints and sinks are defined in YAML. No hardcoded routes.
- **YAML uses snake_case**: Config files use `snake_case`. C# models use `PascalCase`. YamlDotNet `UnderscoredNamingConvention` handles the mapping.
- **Factories**: `SinkFactory.CreateAllAsync()` and `ValidatorFactory.CreateAll()` handle instantiation. Add new types there, not in Program.cs.
- **Interfaces**: `ISink` for sinks, `IValidator` for validators.
- **Sealed classes**: All models and implementations are `sealed`.
- **XML docs**: All public types, members, and interface implementations must have XML docs. Use `/// <inheritdoc />` for interface implementations. Include `<param>`, `<returns>`, `<exception>`, and `<remarks>` tags where applicable.
- **Logging**: All components use `ILogger<T>` injected via constructor. Follow the logging convention below.

## Build and run

```bash
dotnet restore
dotnet build
dotnet run --project src/Hookpipe.API
```

Config path defaults to `config/hookpipe.yaml`. Override with `HOOKPIPE_CONFIG_PATH` env var.

## Tests

```bash
dotnet test --filter "Category!=Integration"   # unit tests
dotnet test --filter "Category=Integration"     # integration tests (requires docker compose)
```

## Logging convention

All log messages use a prefix format: `[Hookpipe.{Module}:{Type}:{Id}]`

Examples:
- `[Hookpipe.Sink:rabbitmq:rabbitmq-main]` — specific sink instance
- `[Hookpipe.Sink:kafka:kafka-events]` — specific sink instance
- `[Hookpipe.Sink:stdout]` — stdout sink (no ID needed)
- `[Hookpipe.Validator:bearer]` — validator type
- `[Hookpipe.Validator:hmac-sha256]` — validator type
- `[Hookpipe.Endpoint:github-push]` — endpoint handler
- `[Hookpipe.Envelope]` — envelope builder
- `[Hookpipe.Config]` — config loading
- `[Hookpipe.Shutdown]` — graceful shutdown

Rules:
- Always include the `[Hookpipe.Module]` prefix
- Include type and ID when available (e.g. sink type + sink ID)
- Use `LogInformation` for startup, connections, registrations
- Use `LogDebug` for per-request details (validation results, path params, message IDs)
- Use `LogError` for failures with the exception object
- Never log secrets, tokens, or full connection strings with credentials

## Adding a new sink

1. Create a class in `src/Hookpipe.Core/Sinks/` implementing `ISink`
2. Accept `ILogger<T>` via constructor
3. Store `sinkId` from `SinkConfig.Id` for logging
4. Add a case to `SinkFactory.CreateAllAsync()`
5. Add NuGet packages to `Directory.Packages.props` with a version
6. Use `[Hookpipe.Sink:{type}:{id}]` prefix in all log messages
7. Add XML docs with `<param>`, `<returns>`, `<exception>` tags
8. Add tests

## Adding a new validator

1. Create a class in `src/Hookpipe.Core/Validation/` implementing `IValidator`
2. Accept `ILogger<T>` via constructor
3. Add it to `ValidatorFactory.CreateAll()`
4. Always reset `Request.Body.Position = 0` after reading the body
5. Use `CryptographicOperations.FixedTimeEquals` for signature/token comparison
6. Use `[Hookpipe.Validator:{type}]` prefix in all log messages
7. Add XML docs with `<remarks>` documenting failure conditions
8. Add tests

## Code conventions

- .NET 8, C# 12
- `sealed` on all classes not designed for inheritance
- XML docs on all public types and members
- Conventional commits: `feat:`, `fix:`, `refactor:`, `chore:`, `docs:`
- No unused imports, no warnings
- Central package management via `Directory.Packages.props`
- `Directory.Build.props` sets TargetFramework, ImplicitUsings, Nullable globally — do not duplicate in csproj files
- `nuget.config` at repo root clears global sources and uses nuget.org only

## Key files

- `src/Hookpipe.API/Program.cs` — entry point, route handler
- `src/Hookpipe.Core/Sinks/SinkFactory.cs` — sink instantiation
- `src/Hookpipe.Core/Validation/ValidatorFactory.cs` — validator instantiation
- `src/Hookpipe.Core/Config/ConfigLoader.cs` — YAML config parsing and validation
- `src/Hookpipe.Core/Services/EnvelopeBuilder.cs` — builds message envelope from HTTP request
- `src/Hookpipe.Core/Sinks/ISink.cs` — sink interface
- `src/Hookpipe.Core/Validation/IValidator.cs` — validator interface
- `config/hookpipe.yaml` — sample configuration

## Do not

- Create separate projects for sinks — they go in `Hookpipe.Core/Sinks/`
- Add packages without adding them to `Directory.Packages.props` first
- Use `class` without `sealed` unless there's a reason for inheritance
- Skip XML docs on public members
- Use .NET 9+ APIs (project targets net8.0)
- Log secrets, tokens, passwords, or full connection URLs with credentials
- Use log messages without the `[Hookpipe.Module]` prefix convention
