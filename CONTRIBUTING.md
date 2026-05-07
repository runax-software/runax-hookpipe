# Contributing to Hookpipe

Thanks for your interest in contributing! Here's how to get started.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
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
   dotnet run --project src/Hookpipe.API
   ```

## Project structure

```
src/
  Hookpipe.API/            # HTTP host and request handler
  Hookpipe.Core/           # Config, models, sinks, validators
    Config/                # YAML config models and loader
    Models/                # Message envelope
    Services/              # Envelope builder
    Sinks/                 # ISink interface and all implementations
    Validation/            # IValidator interface and all implementations
config/
  hookpipe.yaml            # Sample configuration
tests/                     # Test projects
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

## Adding a new sink

1. Create a new class in `Hookpipe.Core/Sinks/`
2. Implement the `ISink` interface
3. Register it in `Program.cs`
4. Add a sample config entry in `config/hookpipe.yaml`
5. Add tests

## Adding a new validator

1. Create a new class in `Hookpipe.Core/Validation/`
2. Implement the `IValidator` interface
3. Register it in `Program.cs`
4. Add tests

## Code style

- Follow existing conventions in the codebase
- Use `sealed` on classes that aren't designed for inheritance
- Add XML docs to all public types and members
- Keep methods focused and small

## Questions?

Open a [GitHub Discussion](https://github.com/runax-software/runax-hookpipe/discussions) or an issue.
