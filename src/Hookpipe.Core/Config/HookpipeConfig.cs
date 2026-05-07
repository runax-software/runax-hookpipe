namespace Hookpipe.Core.Config;

/// <summary>
/// Root configuration containing all endpoint and sink definitions.
/// </summary>
public sealed class HookpipeConfig
{
    /// <summary>
    /// Webhook endpoints that Hookpipe listens on.
    /// </summary>
    public List<EndpointConfig> Endpoints { get; set; } = [];

    /// <summary>
    /// Message sinks that endpoints route messages to.
    /// </summary>
    public List<SinkConfig> Sinks { get; set; } = [];
}
