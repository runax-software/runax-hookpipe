namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// Definitions built in code, collected by <see cref="HookpipeConfigurator"/> and turned into the
/// same <see cref="HookpipeConfiguration"/> every other source produces.
/// </summary>
/// <remarks>
/// Unlike <see cref="YamlSource"/>, this builds the model directly instead of a node tree. The
/// binder's job is to turn text into typed values and report what doesn't fit, and the compiler
/// already did that here: rendering a <see cref="TimeSpan"/> back to <c>"30s"</c> for the binder to
/// re-parse, or serialising an already-compiled <see cref="Conditions.Condition"/> so it could be
/// recompiled, would only lose information. The load-time checks that matter across sources are the
/// reference and invariant ones, and <see cref="ConfigurationChecker"/> runs those on the model.
/// </remarks>
internal sealed class CodeSource
{
    private readonly Dictionary<string, BrokerConfiguration> _brokers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WebhookConfiguration> _webhooks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _duplicates = [];

    /// <summary>
    /// The <c>server</c> section, as set through <see cref="HookpipeConfigurator.Paths"/>.
    /// </summary>
    public HookpipePathsBuilder Paths { get; } = new();

    /// <summary>
    /// Whether anything has been defined in code at all.
    /// </summary>
    public bool IsEmpty => _brokers.Count == 0 && _webhooks.Count == 0;

    /// <summary>
    /// Adds a broker. IDs are compared ignoring case, and a duplicate fails the load rather than
    /// replacing what's there.
    /// </summary>
    public void AddBroker(string id, BrokerConfiguration broker)
    {
        if (!_brokers.TryAdd(id, broker)) _duplicates.Add($"brokers.{id}");
    }

    /// <summary>
    /// Adds a webhook, under the same duplicate rule as <see cref="AddBroker"/>.
    /// </summary>
    public void AddWebhook(string id, WebhookConfiguration webhook)
    {
        if (!_webhooks.TryAdd(id, webhook)) _duplicates.Add($"webhooks.{id}");
    }

    /// <summary>
    /// Builds the model and runs the load-time checks.
    /// </summary>
    /// <returns>The configuration, with every target's broker ID in its defined casing.</returns>
    /// <exception cref="HookpipeConfigurationException">Any check failed.</exception>
    public HookpipeConfiguration Build()
    {
        var errors = new ConfigurationErrors();
        var root = ConfigurationNodePath.CodeRoot;

        foreach (var duplicate in _duplicates)
            errors.Add(root, $"{duplicate}: defined twice; IDs are compared ignoring case");

        var configuration = new HookpipeConfiguration
        {
            Server = new ServerConfiguration
            {
                // server.max_body_size has no code form: it belongs to whoever owns the process,
                // and a library host uses a per-webhook MaxBodySize instead (§11.5).
                Paths = new ServerPathsConfiguration { Webhooks = Paths.Webhooks },
            },
            Brokers = _brokers,
            Webhooks = _webhooks,
        };

        var resolved = new ConfigurationChecker(errors).Check(configuration, root.Child("webhooks"));

        errors.ThrowIfAny("code");
        return resolved;
    }
}
