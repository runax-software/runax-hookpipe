using Runax.Hookpipe.Brokers;
using Runax.Hookpipe.Brokers.Http;
using Runax.Hookpipe.Brokers.Stdout;
using Runax.Hookpipe.Configuration;

namespace Runax.Hookpipe;

/// <summary>
/// Defines one broker in code: the code form of a <c>brokers.&lt;id&gt;</c> entry. Passed to
/// <see cref="HookpipeConfigurator.AddBroker"/>.
/// </summary>
public sealed class BrokerBuilder
{
    private string? _type;
    private object? _settings;
    private RetryConfiguration? _retry;

    /// <summary>
    /// Publishes to standard output. Takes no settings and no topic.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    public BrokerBuilder Stdout() => Type(StdoutBroker.TypeName, settings: null);

    /// <summary>
    /// Relays to an HTTP endpoint. A target's topic is appended to the URL as a path, so
    /// <c>relay:orders</c> posts to <c>&lt;url&gt;/orders</c>.
    /// </summary>
    /// <param name="url">The endpoint URL.</param>
    /// <param name="timeout">Request timeout. Defaults to 30 seconds.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="url"/> is null or whitespace.</exception>
    public BrokerBuilder Http(string url, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var settings = new HttpBrokerSettings { Url = url };
        if (timeout is { } value) settings.Timeout = value;

        return Type(HttpRelayBroker.TypeName, settings);
    }

    /// <summary>
    /// Sets the broker type and its settings directly, for any type Hookpipe knows: <c>rabbitmq</c>,
    /// <c>kafka</c>, <c>sqs</c>, <c>sns</c>, <c>redis</c>, <c>google-pubsub</c>,
    /// <c>azure-servicebus</c>, <c>azure-eventhubs</c>, <c>eventbridge</c>, <c>http</c>, or
    /// <c>stdout</c>. The settings object must be the type that broker expects.
    /// </summary>
    /// <param name="type">The broker type.</param>
    /// <param name="settings">The settings instance, or null for a type that takes none.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="type"/> is null, whitespace, or unknown, or <paramref name="settings"/> isn't
    /// the type that broker expects.
    /// </exception>
    public BrokerBuilder Type(string type, object? settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        if (!BrokerTypes.All.TryGetValue(type, out var known))
        {
            throw new ArgumentException(
                $"Unknown broker type '{type}'. Known types: {string.Join(", ", BrokerTypes.All.Keys.Order())}.",
                nameof(type));
        }

        if (known.SettingsType is null && settings is not null)
            throw new ArgumentException($"'{type}' brokers take no settings.", nameof(settings));

        if (known.SettingsType is { } expected && settings is not null && !expected.IsInstanceOfType(settings))
        {
            throw new ArgumentException(
                $"'{type}' brokers need settings of type {expected.Name}, not {settings.GetType().Name}.",
                nameof(settings));
        }

        _type = type;
        _settings = settings;
        return this;
    }

    /// <summary>
    /// Retries failed publishes to this broker. Without this call there's no retry, as in v1.
    /// </summary>
    /// <param name="maxRetries">Attempts after the first. Defaults to 3.</param>
    /// <param name="delay">The first retry delay. Defaults to 2 seconds.</param>
    /// <param name="backoff">How the delay grows. Defaults to exponential.</param>
    /// <param name="factor">The exponential factor. Defaults to 2.</param>
    /// <param name="maxDelay">Cap on any single delay. Defaults to 30 seconds.</param>
    /// <param name="jitter">Whether delays are randomised. Defaults to true.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public BrokerBuilder Retry(
        int? maxRetries = null,
        TimeSpan? delay = null,
        RetryBackoff? backoff = null,
        double? factor = null,
        TimeSpan? maxDelay = null,
        bool? jitter = null)
    {
        var defaults = new RetryConfiguration();

        _retry = new RetryConfiguration
        {
            MaxRetries = maxRetries ?? defaults.MaxRetries,
            Delay = delay ?? defaults.Delay,
            Backoff = backoff ?? defaults.Backoff,
            Factor = factor ?? defaults.Factor,
            MaxDelay = maxDelay ?? defaults.MaxDelay,
            Jitter = jitter ?? defaults.Jitter,
        };

        return this;
    }

    internal BrokerConfiguration Build(string id)
    {
        if (_type is null)
        {
            throw new InvalidOperationException(
                $"Broker '{id}' has no type. Call Stdout(), Http(url), or Type(type, settings).");
        }

        return new BrokerConfiguration
        {
            Type = _type,
            Settings = _settings,
            Publish = new PublishConfiguration { Retry = _retry },
        };
    }
}
