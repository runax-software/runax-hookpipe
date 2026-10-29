# Contributing to Hookpipe

Thanks for your interest in contributing! Here's how to get started.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A code editor (Rider, VS Code, Visual Studio)

## Getting started

1. Fork the repository
2. Clone your fork
   ```bash
   git clone https://github.com/<your-username>/runax-hookpipe.git
   cd runax-hookpipe
   ```
3. Restore and build
   ```bash
   dotnet restore
   dotnet build
   ```
4. Run with the sample config
   ```bash
   dotnet run --project src/Runax.Hookpipe.Api
   ```

## Project structure

```
src/
  Runax.Hookpipe/          # The library (NuGet package): config, brokers, validators, routing
    Brokers/               # IBroker, BrokerFactory; one folder per broker type (Kafka/, Aws/Sqs/, ...)
    Conditions/            # Route conditions and their evaluation
    Configuration/         # v2 configuration model, strict YAML loader, hot reload
    Diagnostics/           # Metrics on the Runax.Hookpipe meter
    Hosting/               # Runtime, broker startup and shutdown, retry pipelines
    Models/                # Message envelope
    Validation/            # IValidator interface and all implementations
    Webhooks/              # Request handling, routing, envelope builder
    *Extensions.cs         # AddRunaxHookpipe, LoadYaml, MapHookpipe, MapHookpipeHealth
  Runax.Hookpipe.Api/      # The self-hosted service: Serilog, /metrics, /health, config path
config/
  hookpipe.yaml            # Sample configuration
tests/
  Runax.Hookpipe.Tests/      # Library tests
  Runax.Hookpipe.Api.Tests/  # Service tests (routing and /metrics through the real host)
```

## Making changes

1. Create a branch from `main`
   ```bash
   git checkout -b feature/my-change
   ```
2. Make your changes
3. Ensure the project builds with no errors or warnings
   ```bash
   dotnet build
   ```
4. Run tests
   ```bash
   dotnet test
   ```
5. Commit using [conventional commits](https://www.conventionalcommits.org/)
   - `feat:` new feature
   - `fix:` bug fix
   - `refactor:` code change that neither fixes a bug nor adds a feature
   - `chore:` CI, deps, tooling, cleanup
   - `docs:` documentation only
6. Open a pull request against `main`

## Adding a new broker type

1. Create a folder under `src/Runax.Hookpipe/Brokers/` with a class implementing `IBroker` and a
   settings class, one type per file
2. Register it in `BrokerTypes.All` and `BrokerFactory`
3. Document its settings and what its topic means in `docs/brokers.md`
4. Add tests

## Adding a new validator

1. Create a new class in `src/Runax.Hookpipe/Validation/`
2. Implement the `IValidator` interface
3. Add it to `ValidatorFactory.CreateAll()` and list its fields in `HookpipeConfigurationBinder.ValidatorFields`
4. Add tests

## Code style

- Follow existing conventions in the codebase
- Use `sealed` on classes that aren't designed for inheritance
- Add XML docs to all public types and members
- Keep methods focused and small

## Questions?

Open a [GitHub Discussion](https://github.com/runax-software/runax-hookpipe/discussions) or an issue.
