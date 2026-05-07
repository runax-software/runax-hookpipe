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
    Sinks/                   # ISink interface + implementations (StdoutSink, etc.)
    Validation/              # IValidator interface + implementations (Bearer, HMAC-SHA256)
config/
  hookpipe.yaml              # Sample/dev configuration
tests/                       # Test projects
```

Two projects only: `Hookpipe.API` (host) and `Hookpipe.Core` (everything else). Sinks and validators live in Core — do not create separate projects for them.

## Key patterns

- **Config-driven**: All endpoints and sinks are defined in YAML. No hardcoded routes.
- **YAML uses snake_case**: Config files use `snake_case`. C# models use `PascalCase`. YamlDotNet `UnderscoredNamingConvention` handles the mapping.
- **Interfaces**: `ISink` for sinks, `IValidator` for validators. Implementations are registered in `Program.cs` by type string.
- **Sealed classes**: All models and implementations are `sealed`.
- **XML docs**: All public types, members, and interface implementations must have XML docs. Use `/// <inheritdoc />` for interface implementations.
- **Static utilities**: `ConfigLoader` and `EnvelopeBuilder` are static classes.

## Build and run

```bash
dotnet restore
dotnet build
dotnet run --project src/Hookpipe.API
```

Config path defaults to `config/hookpipe.yaml`. Override with `Hookpipe:ConfigPath` in appsettings or env.

## Tests

```bash
dotnet test
```

## Adding a new sink

1. Create a class in `src/Hookpipe.Core/Sinks/` implementing `ISink`
2. Add a case to the `sinkConfig.Type switch` in `Program.cs`
3. Add NuGet packages to `Directory.Packages.props` with a version
4. Add XML docs
5. Add tests

## Adding a new validator

1. Create a class in `src/Hookpipe.Core/Validation/` implementing `IValidator`
2. Add it to the `validators` dictionary in `Program.cs`
3. Always reset `Request.Body.Position = 0` after reading the body
4. Use `CryptographicOperations.FixedTimeEquals` for signature comparison
5. Add XML docs
6. Add tests

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

- `src/Hookpipe.API/Program.cs` — entry point, sink/validator registration, route handler
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
