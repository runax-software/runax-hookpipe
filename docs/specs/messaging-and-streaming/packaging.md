# 11. Packaging: Runax.Hookpipe as a library and as a service

Part of [Spec: Runax.Hookpipe: runax-messaging core, streaming, library and service](README.md).

## 11.1 Two ways to run it

The project is renamed to **Runax.Hookpipe** and split into a NuGet library, which holds the
whole engine, and a thin self-hosted service built on that library. The service is the
library's first customer.

| | Library | Self-hosted service |
| --- | --- | --- |
| **Get it** | `dotnet add package Runax.Hookpipe` | `docker run ghcr.io/runax-software/runax-hookpipe` |
| **Host** | Your ASP.NET Core app | `Runax.Hookpipe.Api`, a thin host over the library |
| **Configure with** | A YAML file, `appsettings.json` (any `IConfiguration` section), code, or a mix | A YAML file (`config/hookpipe.yaml`) |
| **Brokers** | Your app's runax-messaging buses, or buses Hookpipe creates | Buses Hookpipe creates from `brokers` |
| **Auth, CORS, rate limiting** | Hookpipe's own, plus anything ASP.NET Core offers on a route group | Hookpipe's own |
| **Logging, metrics export** | Your app's | Serilog (console, Seq, Loki) and Prometheus `/metrics` |

Both run the same engine and read the same config format. A YAML file that works in the
service works in the library, as long as its broker types are registered
([§11.5](#115-brokers-in-library-mode)).

## 11.2 The rename

**Rule:** .NET identities and the image get the `Runax` prefix. Names that operators and
downstream consumers already depend on keep "Hookpipe".

| | Today | After |
| --- | --- | --- |
| Solution | `Hookpipe.slnx` | `Runax.Hookpipe.slnx` |
| Library | `src/Hookpipe.Core`, namespaces `Hookpipe.Core.*` | `src/Runax.Hookpipe`, namespaces `Runax.Hookpipe.*`, NuGet package `Runax.Hookpipe` |
| Service | `src/Hookpipe.API`, `Hookpipe.API.dll` | `src/Runax.Hookpipe.Api`, `Runax.Hookpipe.Api.dll` |
| Tests | `tests/Hookpipe.Core.Tests` (library and API tests mixed) | `tests/Runax.Hookpipe.Tests` and `tests/Runax.Hookpipe.Api.Tests` |
| Docker image | `ghcr.io/runax-software/hookpipe` | `ghcr.io/runax-software/runax-hookpipe` ([§11.7](#117-docker-image)) |
| Logger categories | `Hookpipe.*` | `Runax.Hookpipe.*` |
| Repository | `runax-hookpipe` | Unchanged |
| Product name, `[Hookpipe.…]` log prefixes, `hookpipe_*` metrics, `HOOKPIPE_CONFIG_PATH`, `hookpipe.*` headers, `config/hookpipe.yaml`, the container user | — | Unchanged |

**What the rename breaks for people who self-host today**, in addition to the rest of
[§13](breaking-changes.md):

- **The image name.** The old package is removed after its tags are copied to the new name
  ([§11.7](#117-docker-image)).
- **The logger category filter.** `Logging__LogLevel__Hookpipe` stops matching.
  `Logging__LogLevel__Runax` replaces it and also covers runax-messaging.

## 11.3 Projects and packages

```
src/
  Runax.Hookpipe/                    NuGet package: the engine
  Runax.Hookpipe.Api/                the self-hosted service and Docker image (IsPackable=false)
tests/
  Runax.Hookpipe.Tests/              library tests, on a minimal test host
  Runax.Hookpipe.Api.Tests/          service tests: WebApplicationFactory<Program>, /metrics names, config path
samples/
  Runax.Hookpipe.Samples.Embedded/   library mode with an app-owned bus
```

| Concern | `Runax.Hookpipe` (library) | `Runax.Hookpipe.Api` (service) |
| --- | --- | --- |
| Webhooks, validators, envelope, routing, JSONPath conditions | Yes | — |
| Publish workers, streams, SSE | Yes | — |
| Config model, YAML and `IConfiguration` loaders, file watcher | Yes | — |
| `stdout` and `http` transports | Yes | — |
| Broker type registry | Yes, with only `stdout` and `http` registered | Registers the eight runax-messaging transports and `eventbridge` (its own until P8, then the package's) |
| `eventbridge` transport | Through `Runax.Messaging.Transports.Aws.EventBridge` once P8 ships | Its own copy until P8 ships |
| Metrics and traces | A `Meter` and an `ActivitySource`, both named `Runax.Hookpipe` | prometheus-net: `/metrics`, `UseHttpMetrics` |
| Health | Health checks added to the host's `AddHealthChecks()`, plus an optional `MapHookpipeHealth()` | `/health`, `/health/ready`, `/health/brokers`, `/health/streams` |
| Logging | `ILogger` only | Serilog: console, Seq, Loki |
| Process settings | — | Kestrel's body-size limit, `HOOKPIPE_CONFIG_PATH`, the Dockerfile |

**Library dependencies:** the `Microsoft.AspNetCore.App` framework reference, `Runax.Messaging`,
`Polly.Core`, `YamlDotNet`, and `JsonPath.Net`. It brings no broker SDKs, no prometheus-net, and no Serilog.
Users add the runax-messaging transports they actually use.

**Metrics move off prometheus-net.** Today `Hookpipe.Core` references prometheus-net and
defines static counters in `HookpipeMetrics`. A library shouldn't pick the exporter, so they
move to `System.Diagnostics.Metrics`. The service exports them, together with runax-messaging's
meter, through prometheus-net's `MeterAdapter`. The exported names must be exactly the ones
[§6.5](messaging-integration.md#65-telemetry) documents. A service test scrapes `/metrics` and
asserts every documented name, so any renaming by the adapter gets caught instead of assumed.

## 11.4 The library API

```csharp
builder.Services.AddRunaxHookpipe(hookpipe =>
{
    hookpipe.LoadYaml("hookpipe.yaml", reloadOnChange: true);   // optional, or:
    hookpipe.LoadConfiguration(builder.Configuration.GetSection("Hookpipe"));   // appsettings.json
    hookpipe.AddBrokerType<KafkaConfig>("kafka");                // enables `type: kafka` in YAML
    hookpipe.UseBus("rabbit", busName: "main");                  // broker `rabbit` is the app's bus "main"

    hookpipe.ConfigureBroker("rabbit", broker => broker
        .Batch(maxSize: 100, maxDelay: TimeSpan.FromMilliseconds(5)));

    hookpipe.AddWebhook("github", webhook => webhook             // POST /webhooks/github
        .ValidateHmacSha256("X-Hub-Signature-256", builder.Configuration["GitHub:WebhookSecret"]!)
        .Route(route => route
            .When(Condition.JsonPath("$.action").Eq("opened")).To("rabbit:github.opened")
            .Otherwise().To("rabbit:github.events")));

    hookpipe.AddStream("github-live", stream => stream           // GET /streams/github-live
        .From("rabbit:github.events")
        .Filter(Condition.Header("X-GitHub-Event").Eq("push"))
        .UseSse());

    hookpipe.AddValidator<ShopifyValidator>("shopify");          // extension points
    hookpipe.AddStreamProtocol<MyProtocol>();
});

var app = builder.Build();

app.MapHookpipeWebhooks();                                      // webhooks: Hookpipe's validators
app.MapHookpipeStreams().RequireAuthorization("dashboard");     // streams: plus the app's auth
app.Run();
```

**Definitions**

- **One model, three sources.** YAML, an `IConfiguration` section
  ([§5.12](configuration.md#512-appsettingsjson-and-other-iconfiguration-sources)), and code
  produce the same definition model and go through the same load-time checks
  ([§5.9](configuration.md#59-interpolation-and-load-time-checks)).
- **Every YAML key has a code equivalent.** The fluent names above are illustrative, not
  binding.
- **Sources can be combined.** Each item comes from exactly one source, so IDs must be unique
  across all of them.
- **Reloads.** YAML reloads from its file watcher and `IConfiguration` from its change token.
  Anything defined in code is fixed for the life of the process.
- **`AddRunaxHookpipe` may be called only once.**

**Routing**

- **`MapHookpipe()`** maps webhooks and streams together and returns a `RouteGroupBuilder`.
  Their prefixes come from `server.paths` (`/webhooks` and `/streams` by default), or from
  `hookpipe.Paths` in code.
- **`MapHookpipeWebhooks()` and `MapHookpipeStreams()`** map them separately, so each can get
  its own policies. Anything ASP.NET Core offers on a route group applies: authorization, CORS,
  rate limiting, OpenAPI tags.
- **The host's authentication helps streams.** When it runs first, `HttpContext.User` reaches
  the stream core, which makes claim-bound filters
  ([open question 5](open-questions.md#12-open-questions)) much cheaper to build.

**Extension points**

- These are public: `IWebhookValidator` (today's `IValidator`), `IStreamProtocol` with
  `StreamSubscription` and `StreamFrame`, and broker types (`TransportConfig` subclasses).
- Everything else is internal: workers, queues, and the stream core.

**Requirements:** the Generic Host, because hosted services run the workers, and ASP.NET Core
endpoint routing.

## 11.5 Brokers in library mode

| Kind | Config | Who creates the bus | Rules |
| --- | --- | --- | --- |
| **Hookpipe-owned** | `type` and `settings` | Hookpipe, through `AddRunaxMessaging`, as a bus named `hookpipe:{broker id}` | The type must be registered with `AddBrokerType`. An unregistered type fails at startup, and the message names the package to add. |
| **App-owned** | `bus: <name>` in YAML, or `UseBus(id, name)` in code | The app, in its own `AddRunaxMessaging` call | Hookpipe never changes the bus's transport, serializer, or mode. Webhooks can't publish to a `ConsumeOnly` bus, and a stream can't read from a `PublishOnly` one. Streams on an app-owned bus need P6. |

**Bus names.** The `hookpipe:` prefix keeps Hookpipe's bus names apart from the app's. One
catch remains. When no default bus exists, runax-messaging resolves an unkeyed `IBus` to the
only named bus, and throws when there are several. An app that relies on that breaks as soon as
Hookpipe adds a bus of its own. P7 lets Hookpipe's buses opt out of unkeyed resolution. Until it
ships, the docs tell such apps to register their bus as the default bus, or to inject it keyed.

**The envelope's wire shape doesn't depend on the bus.** Hookpipe serializes each envelope
once, with its own options, and publishes the resulting `JsonElement`
([§6.1](messaging-integration.md#61-wiring)). That works on app-owned buses whatever their
serializer settings. A bus with a custom non-JSON `ISerializer` isn't supported as a publish
target.

**Hosted-service order doesn't matter.** In library mode Hookpipe can't control whether its
hosted services start before runax-messaging's consumers, and it doesn't need to. Stream
inboxes are created with the registry, before any hosted service starts, so a consumer that
delivers early just fills the inbox until the worker starts. Once shutdown begins, a late event
is acknowledged and dropped. Streams are at-most-once anyway.

**Settings that depend on the mode:**

| Key | Library | Service |
| --- | --- | --- |
| `server.instance_id` | Yes | Yes |
| `server.shutdown_timeout` | Yes. It's Hookpipe's drain budget and must fit inside the host's `HostOptions.ShutdownTimeout`; startup warns if it doesn't. | Yes |
| `server.max_body_size` | Ignored with a warning, because the limit belongs to the host's Kestrel. Use `webhooks.<id>.max_body_size`. | Yes. Sets Kestrel's limit for the whole process. |
| `webhooks.<id>.max_body_size` (new) | Yes, applied per request with `IHttpMaxRequestBodySizeFeature` | Yes |

## 11.6 The self-hosted service

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddHookpipeServiceDefaults();           // Serilog (console, Seq, Loki), Kestrel body limit

builder.Services.AddRunaxHookpipe(hookpipe =>
{
    hookpipe.LoadYaml(ServicePaths.Config, reloadOnChange: true);    // HOOKPIPE_CONFIG_PATH
    hookpipe.AddAllBrokerTypes();               // service-only: the 8 runax transports + eventbridge
});

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseHttpMetrics();
app.MapMetrics();
app.MapGet("/health", () => Results.Ok());
app.MapHookpipe();
app.MapHookpipeHealth();                        // /health/ready, /health/brokers, /health/streams
app.Run();
```

**The service uses only the library's public API.** There's no `InternalsVisibleTo` for it,
only for tests. If `Program.cs` ever needs something internal, the library's API is missing
it, and library users would hit the same wall.

## 11.7 Docker image

- **New name:** `ghcr.io/runax-software/runax-hookpipe`, matching the repository.
- **One package, not two.** GHCR can't rename a package. The old one,
  `ghcr.io/runax-software/hookpipe`, holds two releases: `0.2.0` and `0.2.1` (also `latest`).
  The first v2 release moves them and removes the old package:
  1. Copy `0.2.0` and `0.2.1` to the new name with all their platforms, for example:

     ```bash
     docker buildx imagetools create --tag ghcr.io/runax-software/runax-hookpipe:0.2.1 ghcr.io/runax-software/hookpipe:0.2.1
     ```

     `crane copy` and `skopeo copy --all` work too. `latest` isn't copied, because on the new
     name it means v2.
  2. Delete the `hookpipe` package in the organization's package settings. GitHub keeps a
     deleted package restorable for 30 days. It refuses to delete public packages with more than
     5,000 downloads; that's unlikely to apply here.
- **After that, the old name stops resolving.** Anyone still pulling
  `ghcr.io/runax-software/hookpipe` switches to the new name: for `0.2.x` that's the only change,
  and for v2 it's part of the upgrade anyway. The README and the release notes say so.
- **OCI labels:** `org.opencontainers.image.source`, which links the GHCR package to the
  repository, plus `title`, `version`, and `licenses`.
- **Dockerfile:** it builds `src/Runax.Hookpipe.Api` and starts `Runax.Hookpipe.Api.dll`. It
  also moves to the `10.0-alpine` base images. Today it still builds on `sdk:8.0-alpine` and
  `aspnet:8.0-alpine` although the projects target net10.0, and `global.json` pins SDK 10, so
  the image can't build as it stands.
- **Unchanged:** the container user (`hookpipe`), the `config/` path, and port 8080.

## 11.8 Releases

- **One GitHub release publishes both artifacts,** the NuGet package `Runax.Hookpipe` and the
  image, with the same version taken from the tag. This works the way runax-messaging does:
  `dotnet pack -p:Version=…`, NuGet Trusted Publishing over OIDC, symbol packages, SourceLink.
- **Packaging settings copy runax-messaging's `src/Directory.Build.props`:**
  - `PackageId` is the project name.
  - A per-package README, an MIT license, `GenerateDocumentationFile`, and `snupkg` symbols.
  - `IsPackable=false` on the service, the tests, and the samples.
- **CI packs on every pull request,** so a broken package shows up before a release does.
- **The public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`
  (`PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`), so every public change shows up in
  review.
- **Versioning:** 0.x until the public API and the config format freeze together at 1.0, as
  TODO.md already plans.
- **The first NuGet release waits for the runax-messaging swap** (Phase 4,
  [§10](implementation-plan.md#10-plan)). Before that the library would still carry eight broker
  SDKs and prometheus-net, and its first public API would include types that are about to be
  removed.
- **No native AOT at first.** YamlDotNet and settings binding rely on reflection.
  runax-messaging is AOT-compatible; Hookpipe can follow later.

## 11.9 Docs

- **Root README:** split into "Run as a service" and "Use as a library".
- **`src/Runax.Hookpipe/README.md`:** the NuGet package page, a short library quick start.
- **`samples/Runax.Hookpipe.Samples.Embedded`:** a working library-mode app.
- **AGENTS.md and CONTRIBUTING.md:** the new project names, and the rule that the service uses
  only the public API.
