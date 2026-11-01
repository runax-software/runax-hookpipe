using Runax.Hookpipe.Conditions;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Configuration.Sources;
using Runax.Hookpipe.Webhooks.Validation;

namespace Runax.Hookpipe;

/// <summary>
/// Defines one webhook in code: the code form of a <c>webhooks.&lt;id&gt;</c> entry. Passed to
/// <see cref="HookpipeConfigurator.AddWebhook"/>.
/// </summary>
/// <remarks>
/// Every value is checked as it's set, so a bad JSONPath, an unparseable template, or an unknown
/// validator field throws here rather than at startup. Use either <see cref="To(string[])"/> or
/// <see cref="Route"/>, never both.
/// </remarks>
public sealed class WebhookBuilder
{
    private readonly string _id;
    private readonly List<ValidatorConfiguration> _validators = [];
    private readonly Dictionary<string, string> _metadata = [];
    private readonly Dictionary<string, Template> _metadataTemplates = [];

    private string? _path;
    private IReadOnlyList<string>? _methods;
    private long? _maxBodySize;
    private RateLimitConfiguration? _rateLimit;
    private bool _body = true;
    private bool _includeHeaders;
    private IReadOnlyList<string>? _headerNames;
    private List<Target>? _to;
    private List<RouteRule>? _route;

    internal WebhookBuilder(string id) => _id = id;

    /// <summary>
    /// The route under the webhooks prefix, such as <c>github/push</c> or <c>ingest/{source}</c>.
    /// A leading <c>/</c> is allowed and means the same. Defaults to the webhook's ID.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or whitespace.</exception>
    public WebhookBuilder Path(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path.TrimStart('/');
        return this;
    }

    /// <summary>
    /// The HTTP methods this webhook accepts. Defaults to <c>POST</c>.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException">No methods were given, or one isn't a plain name.</exception>
    public WebhookBuilder Methods(params string[] methods)
    {
        ArgumentNullException.ThrowIfNull(methods);

        if (methods.Length == 0) throw new ArgumentException("at least one method is required", nameof(methods));

        foreach (var method in methods)
            if (string.IsNullOrWhiteSpace(method) || !method.All(char.IsAsciiLetter))
                throw new ArgumentException($"'{method}' isn't an HTTP method", nameof(methods));

        _methods = methods.Select(method => method.ToUpperInvariant()).ToList();
        return this;
    }

    /// <summary>
    /// The largest body this webhook accepts, in bytes. Larger requests get 413. Applied per
    /// request, so it works in library mode too. Defaults to the host's own limit.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bytes"/> is below 1.</exception>
    public WebhookBuilder MaxBodySize(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bytes, 1);

        _maxBodySize = bytes;
        return this;
    }

    /// <summary>
    /// Limits how many requests this webhook accepts per window.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="requests"/> is below 1, or <paramref name="window"/> isn't positive.
    /// </exception>
    public WebhookBuilder RateLimit(int requests, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requests, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        _rateLimit = new RateLimitConfiguration { Requests = requests, Window = window };
        return this;
    }

    /// <summary>
    /// Whether the envelope carries the request body. Defaults to true.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    public WebhookBuilder IncludeBody(bool include = true)
    {
        _body = include;
        return this;
    }

    /// <summary>
    /// Includes only the named request headers in the envelope. Defaults to none.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException">No names were given, or one is blank.</exception>
    public WebhookBuilder IncludeHeaders(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (names.Length == 0) throw new ArgumentException("at least one header name is required", nameof(names));
        if (names.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("a header name is blank", nameof(names));

        _includeHeaders = true;
        _headerNames = names.ToList();
        return this;
    }

    /// <summary>
    /// Includes every request header in the envelope.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    public WebhookBuilder IncludeAllHeaders()
    {
        _includeHeaders = true;
        _headerNames = null;
        return this;
    }

    /// <summary>
    /// Adds a <c>message.metadata</c> entry. The value may be a template, such as
    /// <c>"{source}"</c>, <c>"{webhook_id}"</c>, <c>"{header.X-Request-Id}"</c>, or
    /// <c>"{$.customer.id:-anonymous}"</c>. Metadata values can't refer to other metadata.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException">
    /// The key is blank, already set, or the value's template doesn't parse or uses a placeholder
    /// metadata can't take.
    /// </exception>
    public WebhookBuilder Metadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        if (!_metadata.TryAdd(key, value))
            throw new ArgumentException($"metadata key '{key}' is already set", nameof(key));

        Template template;
        try
        {
            template = Template.Parse(value);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"metadata '{key}': {ex.Message}", nameof(value), ex);
        }

        foreach (var placeholder in template.Placeholders)
        {
            if (placeholder.Source == TemplateSource.Topic)
                throw new ArgumentException(
                    $"metadata '{key}': placeholder '{placeholder.Text}' can't be used in metadata values",
                    nameof(value));

            if (placeholder.Source == TemplateSource.Metadata)
                throw new ArgumentException(
                    $"metadata '{key}': placeholder '{placeholder.Text}': metadata values can't refer to other metadata",
                    nameof(value));
        }

        _metadataTemplates[key] = template;
        return this;
    }

    /// <summary>
    /// Requires an HMAC-SHA256 signature over the raw body.
    /// </summary>
    /// <param name="header">The header carrying the signature, such as <c>X-Hub-Signature-256</c>.</param>
    /// <param name="secret">The shared secret.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public WebhookBuilder ValidateHmacSha256(string header, string secret) =>
        Validate(new ValidatorConfiguration
        {
            Type = HmacSha256Validator.TypeName,
            Header = Required(header, nameof(header)),
            Secret = Required(secret, nameof(secret)),
        });

    /// <summary>
    /// Requires a Stripe <c>v1</c> signature.
    /// </summary>
    /// <param name="secret">The webhook signing secret.</param>
    /// <param name="header">The signature header. Defaults to <c>Stripe-Signature</c>.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    public WebhookBuilder ValidateStripe(string secret, string? header = null) =>
        Validate(new ValidatorConfiguration
        {
            Type = StripeSignatureValidator.TypeName,
            Secret = Required(secret, nameof(secret)),
            Header = header is null ? null : Required(header, nameof(header)),
        });

    /// <summary>
    /// Requires a bearer token.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    public WebhookBuilder ValidateBearer(string token) =>
        Validate(new ValidatorConfiguration
        {
            Type = BearerTokenValidator.TypeName,
            Token = Required(token, nameof(token)),
        });

    /// <summary>
    /// Requires an API key in the given header.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    public WebhookBuilder ValidateApiKey(string header, string key) =>
        Validate(new ValidatorConfiguration
        {
            Type = ApiKeyValidator.TypeName,
            Header = Required(header, nameof(header)),
            Key = Required(key, nameof(key)),
        });

    /// <summary>
    /// Requires the caller's address to fall in one of the given CIDR ranges.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException">No ranges were given, or one is blank.</exception>
    public WebhookBuilder ValidateIpAllowlist(params string[] allow)
    {
        ArgumentNullException.ThrowIfNull(allow);

        if (allow.Length == 0) throw new ArgumentException("at least one range is required", nameof(allow));
        if (allow.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("a range is blank", nameof(allow));

        return Validate(new ValidatorConfiguration
        {
            Type = IpAllowlistValidator.TypeName,
            Allow = allow.ToList(),
        });
    }

    /// <summary>
    /// Publishes every request to the given targets, written <c>broker:topic</c>, or just
    /// <c>broker</c> for a broker without topics. The topic may be a template, such as
    /// <c>kafka:ingest.{source}</c>.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException">No targets were given, or one isn't a valid target.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Route"/> was already used.</exception>
    public WebhookBuilder To(params string[] targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Length == 0) throw new ArgumentException("at least one target is required", nameof(targets));
        if (_route is not null) throw new InvalidOperationException($"Webhook '{_id}' already uses Route.");

        _to = targets.Select(target => ParseTarget(target, nameof(targets))).ToList();
        return this;
    }

    /// <summary>
    /// Acknowledges requests and publishes nothing: the code form of <c>to: none</c>.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Route"/> was already used.</exception>
    public WebhookBuilder Drop()
    {
        if (_route is not null) throw new InvalidOperationException($"Webhook '{_id}' already uses Route.");

        _to = [];
        return this;
    }

    /// <summary>
    /// Routes each request by its content, first match wins. The lambda must end in
    /// <see cref="RouteBuilder.Otherwise"/>, so the compiler enforces the rule that the last rule
    /// has no condition.
    /// </summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="InvalidOperationException"><see cref="To(string[])"/> was already used.</exception>
    public WebhookBuilder Route(Func<RouteBuilder, RouteFallback> route)
    {
        ArgumentNullException.ThrowIfNull(route);

        if (_to is not null) throw new InvalidOperationException($"Webhook '{_id}' already uses To or Drop.");

        var builder = new RouteBuilder(_id);
        _route = route(builder).Rules;
        return this;
    }

    internal static Target ParseTarget(string text, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, paramName);

        var separator = text.IndexOf(':');
        var target = separator < 0
            ? new Target(text, null)
            : new Target(text[..separator], text[(separator + 1)..]);

        if (target.Broker.Length == 0)
            throw new ArgumentException($"'{text}' names no broker; write 'broker:topic'", paramName);

        var errors = new ConfigurationErrors();
        var checkedTarget = ConfigurationChecker.CheckTopic(target, ConfigurationNodePath.CodeRoot, errors);

        if (checkedTarget is null || errors.HasErrors)
            throw new ArgumentException(string.Join("; ", errors.Messages), paramName);

        return checkedTarget;
    }

    internal WebhookConfiguration Build()
    {
        if (_to is null && _route is null)
            throw new InvalidOperationException($"Webhook '{_id}' has no targets. Call To, Drop, or Route.");

        return new WebhookConfiguration
        {
            Path = _path ?? _id,
            Methods = _methods ?? ["POST"],
            Validate = _validators,
            RateLimit = _rateLimit,
            MaxBodySize = _maxBodySize,
            Message = new MessageConfiguration
            {
                Body = _body,
                IncludeHeaders = _includeHeaders,
                HeaderNames = _headerNames,
                Metadata = _metadata,
                MetadataTemplates = _metadataTemplates,
            },
            To = _to,
            Route = _route,
        };
    }

    private static string Required(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }

    private WebhookBuilder Validate(ValidatorConfiguration validator)
    {
        _validators.Add(validator);
        return this;
    }
}
