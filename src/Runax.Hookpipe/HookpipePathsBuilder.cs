using Runax.Hookpipe.Configuration;

namespace Runax.Hookpipe;

/// <summary>
/// The URL prefixes Hookpipe's routes are served under: the code form of <c>server.paths</c>.
/// Reached through <see cref="HookpipeConfigurator.Paths"/>.
/// </summary>
public sealed class HookpipePathsBuilder
{
    private string _webhooks = ServerPathsConfiguration.DefaultWebhooks;

    /// <summary>
    /// The prefix every webhook is served under. Must start with <c>/</c> and may not contain route
    /// parameters. A trailing <c>/</c> is trimmed, and <c>/</c> on its own means no prefix, which is
    /// how v1 URLs are kept when upgrading.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The value is null or whitespace, doesn't start with <c>/</c>, or contains <c>{</c> or <c>}</c>.
    /// </exception>
    public string Webhooks
    {
        get => _webhooks;
        set => _webhooks = Normalize(value, nameof(Webhooks));
    }

    private static string Normalize(string prefix, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix, paramName);

        if (!prefix.StartsWith('/'))
            throw new ArgumentException($"'{prefix}' must start with '/'.", paramName);

        if (prefix.Contains('{') || prefix.Contains('}'))
            throw new ArgumentException($"'{prefix}' can't contain route parameters.", paramName);

        return prefix.Length > 1 ? prefix.TrimEnd('/') : prefix;
    }
}
