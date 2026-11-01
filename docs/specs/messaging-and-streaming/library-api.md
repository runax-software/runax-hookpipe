# The library API

Part of [Spec: Runax.Hookpipe: runax-messaging core, streaming, library and service](README.md).
Expands [§11.4](packaging.md#114-the-library-api) into the concrete public surface.

| | |
| --- | --- |
| **Status** | Draft |
| **Date** | 2026-11-01 |
| **Lands in** | Public-surface cleanup, then [PR C](pull-requests.md#2-pr-c-the-iconfiguration-source), then [PR D](pull-requests.md#3-pr-d-definitions-in-code) |
| **Frozen by** | [PR 11](pull-requests.md#11-first-nuget-release), which adds `PublicApiAnalyzers` and `PublicAPI.Shipped.txt` |

Everything here is cheap to change now and expensive after PR 11. The surface is the deliverable;
the fluent method names are illustrative where noted.

## 1. Registration

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRunaxHookpipe(hookpipe =>
{
    hookpipe.LoadYaml("hookpipe.yaml", reloadOnChange: true);
});

var app = builder.Build();

app.MapHookpipe();
app.Run();
```

- **`AddRunaxHookpipe` may be called only once.** A second call throws.
- **Requires the Generic Host** (hosted services run the publish and stream workers) and
  **ASP.NET Core endpoint routing**.
- The callback receives a `HookpipeConfigurator`. It is constructed by `AddRunaxHookpipe` and
  **has no public constructor** — it is meaningless outside the callback.

## 2. The three sources

YAML, an `IConfiguration` section, and code all build the same model and run the same load-time
checks ([§5.9](configuration.md#59-interpolation-and-load-time-checks)).

```csharp
builder.Services.AddRunaxHookpipe(hookpipe =>
{
    hookpipe.LoadYaml("hookpipe.yaml", reloadOnChange: true);
    hookpipe.LoadConfiguration(builder.Configuration.GetSection("Hookpipe"));
    hookpipe.AddWebhook("internal", w => w.To("console"));
});
```

| Rule | Why |
| --- | --- |
| Each broker, webhook, and stream comes from **exactly one** source | A definition split across sources has no meaningful precedence |
| IDs are unique **across all sources**; a duplicate is a load error | IDs are compared case-insensitively, as `IConfiguration` does |
| Setting the same `server` key in two sources is a load error | Same reason |
| YAML reloads from its file watcher, `IConfiguration` from its change token | [§5.11](configuration.md#511-hot-reload) |
| **Code definitions never reload** — they are fixed for the life of the process | There is no file or token to watch |

## 3. Routing

```csharp
app.MapHookpipe();                 // everything Hookpipe serves
app.MapHookpipeWebhooks();         // or separately, so each gets its own policies
app.MapHookpipeStreams().RequireAuthorization("dashboard");   // Phase 5
app.MapHookpipeHealth();           // /health/brokers today; /health/ready in PR 8
```

**`MapHookpipeStreams()` does not exist yet** and won't until Phase 5 — a method that maps nothing
is worse than a missing one. `MapHookpipe()` delegates to `MapHookpipeWebhooks()` today and will
map streams too once they ship.

One wrinkle to settle in Phase 5: `MapHookpipe()` returns the webhook `RouteGroupBuilder`, so
conventions applied to its result reach webhooks only. Once streams exist that becomes misleading,
because the two live under different prefixes and can't share one group. Either `MapHookpipe()`
stops returning a group, or it returns one rooted above both prefixes.

Prefixes come from `server.paths`, or from `hookpipe.Paths` in code. Anything ASP.NET Core offers
on a route group applies: authorization, CORS, rate limiting, OpenAPI tags.

**When the host's authentication runs first, `HttpContext.User` reaches the stream core**, which is
what makes claim-bound stream filters cheap to add later
([open question 5](open-questions.md#12-open-questions)).

## 4. Paths

```csharp
hookpipe.Paths.Webhooks = "/hooks";     // default "/webhooks"; "/" is allowed
hookpipe.Paths.Streams = "/live";       // default "/streams"
```

The code equivalent of `server.paths`. Full routes must be unique and may not shadow `/health`,
`/health/*`, or `/metrics`.

## 5. Brokers in code

```csharp
hookpipe.AddBroker("console", b => b.Stdout());
hookpipe.AddBroker("relay", b => b.Http("https://relay.internal", timeout: TimeSpan.FromSeconds(10)));

hookpipe.ConfigureBroker("rabbit", b => b              // adjusts a broker defined in YAML
    .Workers(4)
    .OrderBy("{topic}")
    .Queue(capacity: 1000, whenFull: QueueFullMode.Wait, enqueueTimeout: TimeSpan.FromSeconds(2))
    .Batch(maxSize: 100, maxDelay: TimeSpan.FromMilliseconds(5))
    .Retry(maxRetries: 3, delay: TimeSpan.FromSeconds(2), RetryBackoff.Exponential, factor: 2));
```

- **`AddBroker` defines**, **`ConfigureBroker` adjusts** one that already exists in another source.
  `ConfigureBroker` on an unknown ID is a load error.
- **The library registers only `stdout` and `http`.** Every other broker type needs
  `AddBrokerType` ([§11.5](packaging.md#115-brokers-in-library-mode)); the service registers all of
  them through `AddAllBrokerTypes`.

**Phase 4** ([PR 9](pull-requests.md#9-brokers-on-runax-messaging-buses)):

```csharp
hookpipe.AddBrokerType<KafkaConfig>("kafka");     // enables `type: kafka`
hookpipe.UseBus("rabbit", busName: "main");       // broker `rabbit` is the app's bus "main"
```

With `UseBus`, Hookpipe neither creates nor reconfigures the bus, and the loader checks that
Hookpipe's uses fit the bus's existing mode.

## 6. Webhooks in code

```csharp
hookpipe.AddWebhook("github", w => w
    .Path("github/push")                            // default: the webhook ID
    .Methods("POST")                                // default: POST
    .MaxBodySize(10 * 1024 * 1024)                  // 413 above it
    .ValidateHmacSha256("X-Hub-Signature-256", secret)
    .ValidateIpAllowlist("192.30.252.0/22", "185.199.108.0/22")
    .RateLimit(requests: 100, window: TimeSpan.FromSeconds(60))
    .IncludeBody()                                  // default: on
    .IncludeHeaders("X-GitHub-Event", "X-GitHub-Delivery")
    .Metadata("source", "{source}")
    .Delivery(DeliveryAck.Delivered, AcceptWhen.Any, timeout: TimeSpan.FromSeconds(10))
    .Route(r => r
        .When(Condition.Header("X-GitHub-Event").Eq("ping")).Drop()
        .When(Condition.JsonPath("$.action").Eq("opened")).To("rabbit:github.opened")
        .Otherwise().To("rabbit:github.events")));
```

Without routing, every request goes to the same targets:

```csharp
hookpipe.AddWebhook("ingest", w => w
    .Path("ingest/{source}")
    .To("kafka:ingest.{source}", "console"));
```

**`To` and `Route` are mutually exclusive**, as in YAML. Using both is a load error.

### Validators

One method per built-in type, so the required fields are in the signature:

```csharp
.ValidateHmacSha256(header, secret)
.ValidateStripeV1(header, secret)
.ValidateBearer(token)
.ValidateApiKey(header, key)
.ValidateIpAllowlist(params string[] cidrs)
.Validate<ShopifySettings>("shopify", s => s.Secret = secret)   // a registered custom validator
```

All validators in the list must pass.

### Targets

```csharp
.To("rabbit:github.push")                      // broker:topic
.To("console")                                 // a broker without topics
.To(Target.Broker("kafka").Topic("payments")   // the object form, for per-target settings
    .OrderBy("{$.customer.id}")
    .PartitionKey("{$.customer.id}"))          // needs runax-messaging P2
.Drop()                                        // the code form of `to: []`
```

`PartitionKey` is rejected with a clear message until the referenced runax-messaging version
supports it ([§5.9](configuration.md#59-interpolation-and-load-time-checks)).

## 7. Conditions

Static factories on the existing `Condition` class, whose hierarchy stays closed:

```csharp
Condition.JsonPath("$.status").Eq("paid")
Condition.JsonPath("$.amount").Gte(100_000)
Condition.JsonPath("$.type").In("invoice.paid", "checkout.session.completed")
Condition.JsonPath("$.items[?@.qty > 10]").Exists()
Condition.Header("X-Priority").Eq("high")
Condition.Path("source").Ne("test")

Condition.All(a, b, c)
Condition.Any(a, b)
Condition.Not(a)
```

Every operator from [§5.7](configuration.md#57-conditions-jsonpath-and-templates) has a method:
`Eq`, `Ne`, `In`, `NotIn`, `Gt`, `Gte`, `Lt`, `Lte`, `Contains`, `StartsWith`, `EndsWith`, `Regex`,
`Exists`. JSONPaths are parsed and regexes compiled when the configuration loads, not per request.

### The route builder enforces the last-rule invariant at compile time

[§5.9](configuration.md#59-interpolation-and-load-time-checks) requires a route's last rule to have
no `when`. The `Route` lambda returns `RouteFallback`, a type only `Otherwise()` produces:

```csharp
Func<RouteBuilder, RouteFallback> route
```

```csharp
.Route(r => r
    .When(cond).To("kafka:a")
    .Otherwise().To("kafka:b"))        // compiles

.Route(r => r
    .When(cond).To("kafka:a"))         // does NOT compile — no fallback
```

This is the one spec invariant the type system can carry for free. The rest stay load-time checks.

## 8. Extension points

Three things are public. Everything else — workers, queues, the stream core, `EnvelopeBuilder`,
`HookpipeRuntime` — is internal.

### `IWebhookValidator<TSettings>`

A custom validator declares its own settings type, bound the same way broker `settings` bind onto a
`TransportConfig` subclass: snake_case keys map to properties, and DataAnnotations validation runs
at load with errors carrying the config path.

```csharp
public sealed class ShopifySettings
{
    public string Header { get; init; } = "X-Shopify-Hmac-Sha256";

    [Required]
    public required string Secret { get; init; }
}

public sealed class ShopifyValidator : IWebhookValidator<ShopifySettings>
{
    public Task<bool> ValidateAsync(HttpContext context, ShopifySettings settings, CancellationToken ct);
}
```

```csharp
hookpipe.AddValidator<ShopifyValidator>("shopify");
```

```yaml
validate:
  - type: shopify
    settings:
      secret: ${SHOPIFY_SECRET}
```

- A missing or malformed setting is a **load error**, not a 401 on the first request — the whole
  point of resolving secrets at startup ([§1](README.md#whats-wrong-today)).
- The validator is resolved from DI, so it can take `ILogger<T>`, `IHttpClientFactory`, and so on.
- The non-generic `IWebhookValidator` already exists, in `Runax.Hookpipe.Webhooks.Validation`
  alongside `ValidatorFactory` and the five built-in validators, flat. PR D adds `TSettings`:
  today's `ValidateAsync(HttpContext, ValidatorConfiguration)` hands every validator the built-in
  `Header`/`Secret`/`Token`/`Key`/`Allow` shape, and each built-in moves to its own settings type.

**Streams reuse three of them.** Stream `auth` takes `bearer`, `api-key`, and `ip-allowlist`
([§5.8](configuration.md#58-streams)); body-signature validators are rejected. So `Streams/` will
import `Runax.Hookpipe.Webhooks.Validation` in Phase 5. If that dependency direction becomes
awkward, the token-style validators are the ones to lift into a shared namespace — not all five.

### `IStreamProtocol`

Public with `StreamSubscription` and `StreamFrame`, so WebSockets and WebRTC can be added without
touching subscription, filtering, replay, or back-pressure. Lands in Phase 5.

```csharp
hookpipe.AddStreamProtocol<MyProtocol>();
```

### Broker types

A broker type is a runax-messaging `TransportConfig` subclass, registered with
`AddBrokerType<TConfig>(type)`. Lands in Phase 4.

## 9. The server body limit

`server.max_body_size` belongs to whoever owns the process, so the library never applies it itself:

```csharp
builder.Services.AddRunaxHookpipe(hookpipe =>
{
    hookpipe.LoadYaml("hookpipe.yaml");

    hookpipe.UseServerMaxBodySize(limit =>
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = limit));
});
```

- The callback runs **only when the key is set**, with the limit in bytes.
- **Calling it is what claims the setting.** If `server.max_body_size` is set and nobody calls
  this, startup warns that it was ignored — which is exactly library mode.
- The configuration is parsed once and reused, so the file is never read twice and errors are
  reported once.
- Library hosts should prefer `webhooks.<id>.max_body_size`, which is applied per request through
  `IHttpMaxRequestBodySizeFeature` and therefore works regardless of who owns Kestrel.

This replaces `TakeServerMaxBodySize()`, a public getter that mutated state without saying so.

## 10. YAML ↔ code

| YAML | Code |
| --- | --- |
| `server.paths.webhooks` | `hookpipe.Paths.Webhooks` |
| `server.max_body_size` | `hookpipe.UseServerMaxBodySize(limit => …)` |
| `brokers.<id>.type` | `hookpipe.AddBroker(id, b => b.Stdout())`, or `AddBrokerType<T>` |
| `brokers.<id>.bus` | `hookpipe.UseBus(id, busName)` |
| `brokers.<id>.publish.*` | `hookpipe.ConfigureBroker(id, b => b.Workers(…).Queue(…).Batch(…).Retry(…))` |
| `webhooks.<id>` | `hookpipe.AddWebhook(id, w => …)` |
| `webhooks.<id>.validate` | `.ValidateHmacSha256(…)`, `.Validate<TSettings>(type, …)` |
| `webhooks.<id>.to` | `.To(…)` / `.Drop()` |
| `webhooks.<id>.route` | `.Route(r => r.When(…).To(…).Otherwise().To(…))` |
| `route[].when` | `Condition.JsonPath(…)`, `.Header(…)`, `.Path(…)`, `All`/`Any`/`Not` |
| `streams.<id>` | `hookpipe.AddStream(id, s => …)` — Phase 5 |

Templates (`{source}`, `{$.customer.id}`, `{header.X-Id}`) are written as strings in code exactly as
in YAML, and are parsed and checked at load either way.

## 11. Breaking changes to today's surface

All of these land before PR 11 freezes the API.

| Change | Why |
| --- | --- |
| `HookpipeConfigurator`'s public constructor becomes internal | Constructing one outside `AddRunaxHookpipe` does nothing useful and defeats the call-once rule |
| `EnvelopeBuilder` becomes internal | An implementation detail; §11.4 keeps everything but the three extension points internal |
| `TakeServerMaxBodySize()` → `UseServerMaxBodySize(Action<long>)` | A getter that mutated state, with a name that didn't say so |
| `IValidator` → `IWebhookValidator` **(done)**, moved to `Webhooks/Validation/` in the `Runax.Hookpipe.Webhooks.Validation` namespace | Groups the validators with the feature that owns them |
| `IWebhookValidator` → `IWebhookValidator<TSettings>` | Custom validators need their own settings, not the built-in field shape |
| `MapHookpipeWebhooks()` added **(done)**; `MapHookpipeStreams()` waits for Phase 5 | So webhooks and streams can carry different policies |
| `InternalsVisibleTo DynamicProxyGenAssembly2` **(done)** | `Substitute.For<ILogger<T>>` needs it once `T` is internal, and most of the engine is |

## 12. Availability by phase

| Surface | Available |
| --- | --- |
| `AddRunaxHookpipe`, `LoadYaml`, `MapHookpipe`, `MapHookpipeHealth` | Now |
| The surface cleanup in §11, plus `MapHookpipeWebhooks` | **Done** |
| `LoadConfiguration(section)` | PR C |
| `Paths`, `AddBroker`, `ConfigureBroker`, `AddWebhook`, `Condition` factories, `AddValidator<T>`, `IWebhookValidator<TSettings>` | PR D |
| `AddBrokerType<TConfig>`, `UseBus`, `PartitionKey` | Phase 4 (PR 9) |
| `AddStream`, `AddStreamProtocol<T>`, `IStreamProtocol` | Phase 5 (PRs 12–14), blocked on runax-messaging P1 and P6 |

## 13. Open questions

1. **Does code go through a configuration source, or build the model directly?**
   [§11.4](packaging.md#114-the-library-api) says code "goes through a source that builds the same
   model and the same load-time checks as YAML". Taken literally that means fluent builders emit a
   `ConfigurationNode` tree, which round-trips typed values through strings — a `TimeSpan` becomes
   `"30s"` only to be parsed back. **Recommendation:** builders construct the typed model directly
   and feed the same reference-and-invariant pass (§5.9); the binder's type and format checks are
   unnecessary when the compiler already enforced them. Error paths stay the same
   (`webhooks.github.validate[0]`), so messages don't regress.
2. **Does `ConfigureBroker` compose with `publish` from YAML, or replace it?** Replacing is simpler
   to reason about; composing is friendlier. Proposed: each key wins from whichever source set it,
   and setting the same key in both is a load error — consistent with the `server` rule in §2.
3. **Should `Drop()` be named for the YAML it mirrors (`to: none`)?** `.Drop()` reads better than
   `.ToNone()`; the mapping table carries the connection.
