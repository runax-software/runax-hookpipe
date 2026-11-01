using Runax.Hookpipe.Configuration;

namespace Runax.Hookpipe;

/// <summary>
/// A route's last rule, which has no condition and so catches whatever the rules above didn't.
/// Produced by <see cref="RouteBuilder.Otherwise"/>.
/// </summary>
/// <remarks>
/// This type exists so the compiler can carry one of the spec's invariants: because
/// <see cref="WebhookBuilder.Route"/> takes a lambda returning this, and only
/// <see cref="RouteBuilder.Otherwise"/> produces it, a route that forgets its fallback doesn't
/// compile.
/// </remarks>
public sealed class RouteFallback
{
    private readonly List<RouteRule> _rules;

    internal RouteFallback(List<RouteRule> rules) => _rules = rules;

    internal List<RouteRule> Rules => _rules;

    /// <summary>
    /// Publishes everything that didn't match to the given targets, written <c>broker:topic</c>.
    /// </summary>
    /// <returns>The finished route.</returns>
    /// <exception cref="ArgumentException">No targets were given, or one isn't a valid target.</exception>
    public RouteFallback To(params string[] targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Length == 0) throw new ArgumentException("at least one target is required", nameof(targets));

        return Add(targets.Select(target => WebhookBuilder.ParseTarget(target, nameof(targets))).ToList());
    }

    /// <summary>
    /// Acknowledges everything that didn't match and publishes nothing: the code form of
    /// <c>to: none</c>.
    /// </summary>
    /// <returns>The finished route.</returns>
    public RouteFallback Drop() => Add([]);

    private RouteFallback Add(IReadOnlyList<Target> targets)
    {
        _rules.Add(new RouteRule { When = null, To = targets });
        return this;
    }
}
