# AGENTS.md

Instructions for AI coding agents working on this repository.

## Project overview

Hookpipe is a config-driven webhook gateway built with .NET 10. It receives HTTP webhooks on configured routes, validates them, routes them by header or JSON body, wraps them in a standardized message envelope, and publishes them to brokers (SQS, Kafka, RabbitMQ, stdout, and more). It ships as the `Runax.Hookpipe` library (a NuGet package) and the `Runax.Hookpipe.Api` service built on it. The v2 design is in `docs/specs/messaging-and-streaming/`.

## Architecture

```
src/
  Runax.Hookpipe/            # The library: all core logic, packaged for NuGet
    Brokers/                 # IBroker, BrokerFactory, BrokerTypes, IKnownTopics
      Kafka/                 # One folder per broker type: the broker and its settings class
      Aws/Sqs/, Azure/ServiceBus/, Google/PubSub/, ...   # Same layout as runax-messaging's transports
      Health/                # IBrokerHealthCheck, /health/brokers
    Conditions/              # Route conditions (JSONPath, header, path) and their evaluation
    Configuration/           # v2 model, ConfigurationLoader, HookpipeConfigurationProvider (hot reload)
      Sources/               # YAML -> node tree -> HookpipeConfigurationBinder (strict), interpolation
    Diagnostics/             # HookpipeMetrics on the Runax.Hookpipe meter
    Hosting/                 # HookpipeRuntime, hosted service (broker startup/shutdown), RetryPipelines
    Models/                  # MessageEnvelope
    Validation/              # IValidator interface, ValidatorFactory, implementations
    Webhooks/                # WebhookHandler, RouteEvaluator, EnvelopeBuilder
    ServiceCollectionExtensions.cs      # AddRunaxHookpipe
    HookpipeConfigurator*.cs            # HookpipeConfigurator, LoadYaml
    EndpointRouteBuilderExtensions.cs   # MapHookpipe, MapHookpipeHealth
  Runax.Hookpipe.Api/        # The self-hosted service: Program.cs, Serilog, /metrics, /health
config/
  hookpipe.yaml              # Sample/dev configuration
tests/
  Runax.Hookpipe.Tests/      # Library tests
  Runax.Hookpipe.Api.Tests/  # Service tests through WebApplicationFactory<Program>
```

Two production projects: `Runax.Hookpipe` (everything) and `Runax.Hookpipe.Api` (hosting only). Brokers and validators live in the library — do not create separate projects for them. The service uses only the library's public API: if `Program.cs` needs something internal, the library's API is missing it.

## Key patterns

- **Config-driven**: All brokers and webhooks are defined in YAML (config format v2, `version: 2`). No hardcoded routes. Webhooks publish to targets written `broker:topic`.
- **Strict loader**: `YamlSource` reads YAML into `ConfigurationNode`s (interpolating `${…}`), and `HookpipeConfigurationBinder` turns them into `HookpipeConfiguration`. Unknown keys and bad references are errors with their config path; collect errors rather than throwing on the first. Never echo an interpolated value in an error.
- **YAML uses snake_case**: Config keys are documented in `snake_case` and match ignoring case and underscores (`MappingReader.Normalize`). C# models use `PascalCase`. IDs, header names, and dictionary keys are never rewritten.
- **Factories**: `BrokerFactory.CreateAllAsync()` (one instance per configured broker) and `ValidatorFactory.CreateAll()` handle instantiation. Broker types are listed in `BrokerTypes.All`.
- **Metrics**: record through `Diagnostics/HookpipeMetrics` on the `Runax.Hookpipe` meter. The library never references prometheus-net; the service exports the meter with `HookpipeMetricsExporter`, which keeps the existing Prometheus names.
- **Interfaces**: `IBroker` for brokers (the target's topic comes with each publish), `IValidator` for validators.
- **Sealed classes**: All models and implementations are `sealed`.
- **XML docs**: All public types, members, and interface implementations must have XML docs. Use `/// <inheritdoc />` for interface implementations. Include `<param>`, `<returns>`, `<exception>`, and `<remarks>` tags where applicable.
- **Logging**: All components use `ILogger<T>` injected via constructor. Follow the logging convention below.
- **Hot-reload**: `HookpipeConfigurationProvider` holds the live configuration. `ConfigurationFileWatcher` watches the YAML file. Handlers read `ConfigurationProvider.Current` per request — don't capture config in closures.
- **Internal classes**: Use `internal` for classes only used within `Runax.Hookpipe` (e.g. `LogHelper`). Classes the service or library users need must be `public`.

## Build and run

```bash
dotnet restore
dotnet build
dotnet run --project src/Runax.Hookpipe.Api
```

Config path defaults to `config/hookpipe.yaml`. Override with `HOOKPIPE_CONFIG_PATH` env var.

## Tests

```bash
dotnet test -- --filter-not-trait "Category=Integration"   # unit tests
dotnet test -- --filter-trait "Category=Integration"       # integration tests (requires docker compose)
```

Tests use xUnit v3 on Microsoft.Testing.Platform, NSubstitute, and Shouldly, as in runax-messaging.
Don't add paid or commercially licensed packages.

## Logging convention

All log messages use a prefix format: `[Hookpipe.{Module}:{Type}:{Id}]`

Examples:
- `[Hookpipe.Broker:rabbitmq:rabbit]` — specific broker
- `[Hookpipe.Broker:kafka:kafka]` — specific broker
- `[Hookpipe.Broker:stdout]` — stdout broker (no ID needed)
- `[Hookpipe.Validator:bearer]` — validator type
- `[Hookpipe.Validator:hmac-sha256]` — validator type
- `[Hookpipe.Webhook:github]` — webhook handler
- `[Hookpipe.Envelope]` — envelope builder
- `[Hookpipe.Configuration]` — configuration loading
- `[Hookpipe.Shutdown]` — graceful shutdown

Rules:
- Always include the `[Hookpipe.Module]` prefix
- Include type and ID when available (e.g. broker type + broker ID)
- Use `LogInformation` for startup, connections, registrations
- Use `LogDebug` for per-request details (validation results, path params, message IDs)
- Use `LogError` for failures with the exception object
- Never log secrets, tokens, or full connection strings with credentials

## Adding a new broker type

1. Create a folder for it under `src/Runax.Hookpipe/Brokers/` (`Kafka/`, `Aws/Sqs/`, and so on), with a class implementing `IBroker` and a settings class, each in its own file. Name settings properties after the runax-messaging transport config when there is one; mark required ones `[Required]`
2. Accept `ILogger<T>` via constructor and store the broker ID for logging
3. Register it in `BrokerTypes.All` (name, settings type, whether targets need a topic)
4. Add a case to `BrokerFactory.CreateAsync()`
5. Add NuGet packages to `Directory.Packages.props` with a version
6. Use `[Hookpipe.Broker:{type}:{id}]` prefix in all log messages
7. Add XML docs with `<param>`, `<returns>`, `<exception>` tags
8. Add tests

## Adding a new validator

1. Create a class in `src/Runax.Hookpipe/Validation/` implementing `IValidator`
2. Accept `ILogger<T>` via constructor
3. Add it to `ValidatorFactory.CreateAll()` and list its fields in `HookpipeConfigurationBinder.ValidatorFields`
4. Always reset `Request.Body.Position = 0` after reading the body
5. Use `CryptographicOperations.FixedTimeEquals` for signature/token comparison
6. Use `[Hookpipe.Validator:{type}]` prefix in all log messages
7. Add XML docs with `<remarks>` documenting failure conditions
8. Add tests

## Code conventions

- .NET 10, latest C#
- `sealed` on all classes not designed for inheritance
- One type per file (class, record, struct, interface, enum), named after the type
- No abbreviations in names: `Configuration`, not `Config`
- The library registers like runax-messaging: `services.AddRunaxHookpipe(hookpipe => …)` in `ServiceCollectionExtensions`, configured through `HookpipeConfigurator` and its extension methods
- XML docs on all public types and members
- Conventional commits: `feat:`, `fix:`, `refactor:`, `chore:`, `docs:`
- No unused imports, no warnings
- Central package management via `Directory.Packages.props`
- `Directory.Build.props` sets TargetFramework, ImplicitUsings, Nullable globally — do not duplicate in csproj files
- `nuget.config` at repo root clears global sources and uses nuget.org only

## Key files

- `src/Runax.Hookpipe.Api/Program.cs` — the service's entry point
- `src/Runax.Hookpipe/ServiceCollectionExtensions.cs` — `AddRunaxHookpipe`, the library's entry point
- `src/Runax.Hookpipe/EndpointRouteBuilderExtensions.cs` — `MapHookpipe`, `MapHookpipeHealth`
- `src/Runax.Hookpipe/Hosting/` — startup and shutdown of brokers
- `src/Runax.Hookpipe/Webhooks/WebhookHandler.cs` — request handling per webhook
- `src/Runax.Hookpipe/Brokers/BrokerFactory.cs` — one instance per configured broker
- `src/Runax.Hookpipe/Validation/ValidatorFactory.cs` — validator instantiation
- `src/Runax.Hookpipe/Configuration/ConfigurationLoader.cs` — loads a v2 YAML file
- `src/Runax.Hookpipe/Configuration/Sources/HookpipeConfigurationBinder.cs` — strict binding and load-time checks
- `src/Runax.Hookpipe/Webhooks/EnvelopeBuilder.cs` — builds message envelope from HTTP request
- `src/Runax.Hookpipe/Brokers/IBroker.cs` — broker interface
- `src/Runax.Hookpipe/Validation/IValidator.cs` — validator interface
- `config/hookpipe.yaml` — sample configuration

## Do not

- Create separate projects for broker types — each gets a folder in `src/Runax.Hookpipe/Brokers/`
- Add `InternalsVisibleTo` for the service — it uses only the library's public API
- Add packages without adding them to `Directory.Packages.props` first
- Use `class` without `sealed` unless there's a reason for inheritance
- Skip XML docs on public members
- Log secrets, tokens, passwords, or full connection URLs with credentials
- Use log messages without the `[Hookpipe.Module]` prefix convention
