using Runax.Hookpipe.Conditions;
using Runax.Hookpipe.Configuration;

namespace Runax.Hookpipe;

/// <summary>
/// Builds a webhook's ordered route rules. Rules are checked in order and the first match wins.
/// </summary>
/// <remarks>
/// The chain must end in <see cref="Otherwise"/>, because a route's last rule may not have a
/// condition. <see cref="WebhookBuilder.Route"/> takes a lambda returning
/// <see cref="RouteFallback"/>, which only <see cref="Otherwise"/> produces, so the compiler rejects
/// a route with no fallback instead of leaving it to a load-time error.
/// </remarks>
public sealed class RouteBuilder
{
    private readonly string _webhookId;
    private readonly List<RouteRule> _rules = [];
    private Condition? _pending;

    internal RouteBuilder(string webhookId) => _webhookId = webhookId;

    /// <summary>
    /// Starts a rule that applies when <paramref name="condition"/> matches. Follow it with
    /// <see cref="To"/> or <see cref="Drop"/>.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="InvalidOperationException">The previous rule has no targets yet.</exception>
    public RouteBuilder When(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        if (_pending is not null)
            throw new InvalidOperationException($"Webhook '{_webhookId}': call To or Drop before the next When.");

        _pending = condition;
        return this;
    }

    /// <summary>
    /// Publishes matching requests to the given targets, written <c>broker:topic</c>.
    /// </summary>
    /// <returns>The same builder, so more rules can follow.</returns>
    /// <exception cref="InvalidOperationException">No <see cref="When"/> is open.</exception>
    public RouteBuilder To(params string[] targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Length == 0) throw new ArgumentException("at least one target is required", nameof(targets));

        return Add(targets.Select(target => WebhookBuilder.ParseTarget(target, nameof(targets))).ToList());
    }

    /// <summary>
    /// Acknowledges matching requests and publishes nothing: the code form of <c>to: none</c>.
    /// </summary>
    /// <returns>The same builder, so more rules can follow.</returns>
    /// <exception cref="InvalidOperationException">No <see cref="When"/> is open.</exception>
    public RouteBuilder Drop() => Add([]);

    /// <summary>
    /// Ends the route with the rule that applies when nothing above matched. Every route needs one.
    /// </summary>
    /// <returns>A fallback awaiting its targets.</returns>
    /// <exception cref="InvalidOperationException">The previous rule has no targets yet.</exception>
    public RouteFallback Otherwise()
    {
        if (_pending is not null)
            throw new InvalidOperationException($"Webhook '{_webhookId}': call To or Drop before Otherwise.");

        return new RouteFallback(_rules);
    }

    private RouteBuilder Add(IReadOnlyList<Target> targets)
    {
        if (_pending is null)
        {
            throw new InvalidOperationException(
                $"Webhook '{_webhookId}': call When before To or Drop, or Otherwise for the last rule.");
        }

        _rules.Add(new RouteRule { When = _pending, To = targets });
        _pending = null;
        return this;
    }
}
