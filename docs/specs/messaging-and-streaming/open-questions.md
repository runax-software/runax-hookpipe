# 12. Open questions

Part of [Spec: Runax.Hookpipe: runax-messaging core, streaming, library and service](README.md).

1. **A real thread per sink? Resolved:** no. Workers are async loops, and throughput comes from
   brokers running in parallel, batching, and `publish.workers: N` per broker
   ([Going faster](architecture.md#going-faster-batching-and-more-workers)). Revisit only if a transport with a
   blocking SDK appears.
2. **Default `ack` mode.** `delivered` keeps v1's semantics and is the safe default. Should any
   webhook default to `enqueued`? *Recommendation: keep `delivered` everywhere.*
3. **Resuming across replicas.** Per-replica `{epoch}-{sequence}` IDs only resume on the same
   replica (or with sticky sessions). Broker offsets would make IDs global: the Kafka offset per
   partition, or the Redis stream ID. That only works on brokers that have them.
   *Recommendation: ship per-replica IDs and revisit with demand.*
4. **How long v1 configs are supported. Resolved:** they aren't. v2 is a breaking release with
   no translator ([§13](breaking-changes.md)).
5. **Claims-based stream auth.** Validate a JWT and bind path parameters to claims
   (`tenant` must equal claim `tid`), so per-tenant streams are safe with shared credentials.
   This is likely the next feature after SSE. In library mode the host's ASP.NET Core
   authentication already fills `HttpContext.User`, so this mostly means binding claims in
   filters (`{claim.tid}`).
6. **Where EventBridge lives. Resolved:** in runax-messaging, as
   `Runax.Messaging.Transports.Aws.EventBridge` (P8, now on runax-messaging's roadmap). Until it
   ships, the service keeps its own publish-only `eventbridge` transport.
7. **An in-process tap. Resolved:** planned as an improvement, local delivery with dedupe and
   debounce ([§4.9](streaming.md#49-improvement-local-delivery-dedupe-and-debounce)), in Phase 6.
   It works with any number of replicas: other replicas still get the broker's copy.
8. **Repository state.** `runax-hookpipe` is archived on GitHub. It needs to be unarchived
   before any of this can be pushed.
9. **An `IConfiguration` source. Resolved:** yes, in the first library release
   ([§5.12](configuration.md#512-appsettingsjson-and-other-iconfiguration-sources)). Key
   names match ignoring case and underscores, so there's no third key style to learn.
10. **How long the old image name keeps getting releases. Resolved:** it doesn't. Its
    `0.2.x` tags are copied to the new name and the old package is deleted
    ([§11.7](packaging.md#117-docker-image)).
11. **The service project's name. Resolved:** `Runax.Hookpipe.Api`, following the current
    `Hookpipe.API`.
12. **Local delivery's default.** `after_publish` is safe, because dedupe drops the broker's
    copy, and it's faster. The cost is up to 100,000 remembered IDs per stream, and a duplicate if
    the broker's copy arrives after the 2-minute window. *Recommendation: on by default when
    Phase 6 ships, and keep `local_delivery: off` easy to find in the docs.*
