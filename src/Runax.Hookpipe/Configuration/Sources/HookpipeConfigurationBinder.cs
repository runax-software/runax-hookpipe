using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing.Patterns;
using Runax.Hookpipe.Conditions;
using Runax.Hookpipe.Brokers;
using Runax.Hookpipe.Validation;

namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// Turns a configuration node tree into <see cref="HookpipeConfiguration"/>. Strict: unknown keys, missing
/// required values, unknown references, and malformed conditions are all errors, collected so one
/// load reports them all.
/// </summary>
internal sealed partial class HookpipeConfigurationBinder(ConfigurationErrors errors)
{
    /// <summary>
    /// The upgrade guide a configuration without <c>version: 2</c> is pointed to.
    /// </summary>
    public const string UpgradeGuideUrl = "https://github.com/runax-software/runax-hookpipe/blob/main/docs/upgrading.md";

    private const string NoTargets = "none";

    private static readonly string[] SourceKeys = ["jsonpath", "header", "path"];
    private static readonly string[] CombinatorKeys = ["all", "any", "not"];
    private static readonly HashSet<string> ReservedPathParameters = ["webhook_id", "topic"];

    private static readonly Dictionary<string, ConditionOperator> OperatorKeys =
        Enum.GetValues<ConditionOperator>().ToDictionary(
            @operator => MappingReader.Normalize(CompareCondition.Name(@operator)));

    private static readonly Dictionary<string, (string[] Required, string[] Optional)> ValidatorFields = new()
    {
        [HmacSha256Validator.TypeName] = (["header", "secret"], []),
        [StripeSignatureValidator.TypeName] = (["secret"], ["header"]),
        [BearerTokenValidator.TypeName] = (["token"], []),
        [ApiKeyValidator.TypeName] = (["header", "key"], []),
        [IpAllowlistValidator.TypeName] = (["allow"], []),
    };

    private readonly ScalarReader _reader = new(errors);

    /// <summary>
    /// Binds the root node. Returns null when the file can't be read as v2 at all.
    /// </summary>
    public HookpipeConfiguration? Bind(ConfigurationNode? root)
    {
        if (root is null)
        {
            errors.Add(ConfigurationNodePath.YamlRoot, "the config is empty");
            return null;
        }

        if (_reader.Mapping(root) is not { } map) return null;
        if (!CheckVersion(map)) return null;

        var server = map.Get("server") is { } serverNode ? BindServer(serverNode) : new ServerConfiguration();
        var brokers = map.Get("brokers") is { } brokersNode
            ? BindMap(brokersNode, BindBroker)
            : new Dictionary<string, BrokerConfiguration>(StringComparer.OrdinalIgnoreCase);
        var webhooks = map.Get("webhooks") is { } webhooksNode
            ? BindMap(webhooksNode, BindWebhook)
            : new Dictionary<string, WebhookConfiguration>(StringComparer.OrdinalIgnoreCase);

        map.ReportUnknownKeys();

        var configuration = new HookpipeConfiguration { Server = server, Brokers = brokers, Webhooks = webhooks };
        return CheckReferences(configuration, map.PathOf("webhooks"));
    }

    private bool CheckVersion(MappingReader map)
    {
        var versionNode = map.Get("version");
        if (versionNode is null)
        {
            errors.Add(map.PathOf("version"),
                $"missing. This file looks like config format v1, which isn't supported; add 'version: 2' and " +
                $"rewrite it with the upgrade guide: {UpgradeGuideUrl}");
            return false;
        }

        if (versionNode is ConfigurationScalar { Value: "2" }) return true;

        errors.Add(versionNode.Path, $"unsupported version; this release reads 'version: {HookpipeConfiguration.SupportedVersion}'");
        return false;
    }

    private ServerConfiguration BindServer(ConfigurationNode node)
    {
        if (_reader.Mapping(node) is not { } map) return new ServerConfiguration();

        var paths = new ServerPathsConfiguration();
        if (map.Get("paths") is { } pathsNode && _reader.Mapping(pathsNode) is { } pathsMap)
        {
            if (pathsMap.Get("webhooks") is { } webhooksNode && _reader.String(webhooksNode) is { } prefix)
                paths = new ServerPathsConfiguration { Webhooks = NormalizePrefix(prefix, webhooksNode.Path) };

            pathsMap.ReportUnknownKeys();
        }

        map.ReportUnknownKeys();
        return new ServerConfiguration { Paths = paths };
    }

    private string NormalizePrefix(string prefix, ConfigurationNodePath path)
    {
        if (!prefix.StartsWith('/'))
        {
            errors.Add(path, "must start with '/'");
            return ServerPathsConfiguration.DefaultWebhooks;
        }

        if (prefix.Contains('{') || prefix.Contains('}'))
        {
            errors.Add(path, "can't contain route parameters");
            return ServerPathsConfiguration.DefaultWebhooks;
        }

        return prefix.Length > 1 ? prefix.TrimEnd('/') : prefix;
    }

    private Dictionary<string, T> BindMap<T>(ConfigurationNode node, Func<string, ConfigurationNode, T?> bind) where T : class
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        if (node is not ConfigurationMapping mapping)
        {
            errors.Add(node.Path, "expected a mapping keyed by ID");
            return result;
        }

        foreach (var (id, itemNode) in mapping.Entries)
        {
            if (!IdPattern().IsMatch(id))
            {
                errors.Add(itemNode.Path, "IDs may use only letters, digits, '.', '_', and '-'");
                continue;
            }

            if (result.Keys.FirstOrDefault(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase))
                is { } duplicate)
            {
                errors.Add(itemNode.Path, $"duplicate ID (IDs ignore case, and '{duplicate}' is already defined)");
                continue;
            }

            if (bind(id, itemNode) is { } item) result[id] = item;
        }

        return result;
    }

    private BrokerConfiguration? BindBroker(string id, ConfigurationNode node)
    {
        if (string.Equals(id, NoTargets, StringComparison.OrdinalIgnoreCase))
            errors.Add(node.Path, $"'{NoTargets}' is reserved and can't be a broker ID");

        if (_reader.Mapping(node) is not { } map) return null;

        var typeNode = map.Get("type");
        var type = typeNode is not null ? _reader.String(typeNode) : null;
        var settingsNode = map.Get("settings");
        object? settings = null;

        if (type is null)
            errors.Add(map.PathOf("type"), "required");
        else if (!BrokerTypes.All.TryGetValue(type, out var brokerType))
            errors.Add(map.PathOf("type"),
                $"unknown broker type {Show(typeNode!, type)}; known types: {string.Join(", ", BrokerTypes.All.Keys)}");
        else if (brokerType.SettingsType is null && settingsNode is not null)
            errors.Add(settingsNode.Path, $"'{type}' brokers take no settings");
        else if (brokerType.SettingsType is not null)
            settings = SettingsBinder.Bind(brokerType.SettingsType, settingsNode, map.PathOf("settings"), _reader);

        var publish = new PublishConfiguration();
        if (map.Get("publish") is { } publishNode && _reader.Mapping(publishNode) is { } publishMap)
        {
            publish = new PublishConfiguration { Retry = publishMap.Get("retry") is { } retryNode ? BindRetry(retryNode) : null };
            publishMap.ReportUnknownKeys();
        }

        map.ReportUnknownKeys();
        return type is null ? null : new BrokerConfiguration { Type = type, Settings = settings, Publish = publish };
    }

    private RetryConfiguration? BindRetry(ConfigurationNode node)
    {
        if (_reader.Mapping(node) is not { } map) return null;

        var defaults = new RetryConfiguration();
        var retry = new RetryConfiguration
        {
            MaxRetries = map.Get("max_retries") is { } maxRetries ? _reader.Int(maxRetries, 0) ?? 0 : defaults.MaxRetries,
            Delay = map.Get("delay") is { } delay ? _reader.Duration(delay) ?? defaults.Delay : defaults.Delay,
            Backoff = map.Get("backoff") is { } backoff
                ? _reader.Enum<RetryBackoff>(backoff) ?? defaults.Backoff
                : defaults.Backoff,
            Factor = map.Get("factor") is { } factor ? _reader.Double(factor, 1) ?? defaults.Factor : defaults.Factor,
            MaxDelay = map.Get("max_delay") is { } maxDelay
                ? _reader.Duration(maxDelay) ?? defaults.MaxDelay
                : defaults.MaxDelay,
            Jitter = map.Get("jitter") is { } jitter ? _reader.Bool(jitter) ?? defaults.Jitter : defaults.Jitter,
        };

        map.ReportUnknownKeys();
        return retry;
    }

    private WebhookConfiguration? BindWebhook(string id, ConfigurationNode node)
    {
        if (_reader.Mapping(node) is not { } map) return null;

        var path = map.Get("path") is { } pathNode ? NormalizeWebhookPath(pathNode) : id;
        var methods = map.Get("methods") is { } methodsNode ? BindMethods(methodsNode) : ["POST"];
        var validate = map.Get("validate") is { } validateNode ? BindValidators(validateNode) : [];
        var rateLimit = map.Get("rate_limit") is { } rateLimitNode ? BindRateLimit(rateLimitNode) : null;
        var message = map.Get("message") is { } messageNode ? BindMessage(messageNode) : new MessageConfiguration();
        var toNode = map.Get("to");
        var routeNode = map.Get("route");

        if (toNode is null && routeNode is null)
            errors.Add(node.Path, "needs 'to' or 'route' (use 'to: none' to publish nothing)");
        else if (toNode is not null && routeNode is not null)
            errors.Add(node.Path, "has both 'to' and 'route'; use one");

        var to = toNode is not null ? BindTargets(toNode) : null;
        var route = routeNode is not null ? BindRoute(routeNode) : null;

        map.ReportUnknownKeys();
        return new WebhookConfiguration
        {
            Path = path,
            Methods = methods,
            Validate = validate,
            RateLimit = rateLimit,
            Message = message,
            To = to,
            Route = route,
        };
    }

    private string NormalizeWebhookPath(ConfigurationNode node)
    {
        var path = _reader.String(node)?.Trim().TrimStart('/') ?? string.Empty;

        if (path.Length == 0) errors.Add(node.Path, "is empty");
        else if (path.EndsWith('/')) errors.Add(node.Path, "can't end with '/'");

        return path;
    }

    private List<string> BindMethods(ConfigurationNode node)
    {
        var methods = _reader.StringList(node) ?? [];
        if (methods.Count == 0) errors.Add(node.Path, "needs at least one method");

        foreach (var method in methods.Where(method => !MethodPattern().IsMatch(method)))
            errors.Add(node.Path, $"{Show(node, method)} isn't an HTTP method");

        return methods.Select(method => method.ToUpperInvariant()).Distinct().ToList();
    }

    private List<ValidatorConfiguration> BindValidators(ConfigurationNode node)
    {
        var validators = new List<ValidatorConfiguration>();
        if (_reader.Sequence(node) is not { } sequence) return validators;

        foreach (var item in sequence.Items)
            if (BindValidator(item) is { } validator)
                validators.Add(validator);

        return validators;
    }

    private ValidatorConfiguration? BindValidator(ConfigurationNode node)
    {
        if (_reader.Mapping(node) is not { } map) return null;

        var typeNode = map.Get("type");
        var type = typeNode is not null ? _reader.String(typeNode) : null;
        if (type is null)
        {
            errors.Add(map.PathOf("type"), "required");
            return null;
        }

        if (!ValidatorFields.TryGetValue(type, out var fields))
        {
            errors.Add(map.PathOf("type"),
                $"unknown validator type {Show(typeNode!, type)}; known types: {string.Join(", ", ValidatorFactory.KnownTypes)}");
            return null;
        }

        var used = fields.Required.Concat(fields.Optional).ToHashSet();
        string? Field(string key) => used.Contains(key) && map.Get(key) is { } value ? _reader.String(value) : null;

        var validator = new ValidatorConfiguration
        {
            Type = type,
            Header = Field("header"),
            Secret = Field("secret"),
            Token = Field("token"),
            Key = Field("key"),
            Allow = used.Contains("allow") && map.Get("allow") is { } allow
                ? _reader.StringList(allow, commaSeparated: true) ?? []
                : [],
        };

        if (map.Get("allow") is { } allowNode && used.Contains("allow"))
        {
            foreach (var entry in validator.Allow.Where(entry => !IpAllowlistValidator.IsAddressOrRange(entry)))
                errors.Add(allowNode.Path, $"{Show(allowNode, entry)} isn't an IP address or CIDR range");
        }

        foreach (var required in fields.Required)
        {
            if (!map.Has(required))
                errors.Add(map.PathOf(required), $"required by '{type}'");
            else if (required == "allow" ? validator.Allow.Count == 0 : string.IsNullOrEmpty(Field(required)))
                errors.Add(map.PathOf(required), "is empty");
        }

        map.ReportUnknownKeys();
        return validator;
    }

    private RateLimitConfiguration? BindRateLimit(ConfigurationNode node)
    {
        if (_reader.Mapping(node) is not { } map) return null;

        var defaults = new RateLimitConfiguration();
        var rateLimit = new RateLimitConfiguration
        {
            Requests = map.Get("requests") is { } requests ? _reader.Int(requests, 1) ?? 1 : defaults.Requests,
            Window = map.Get("window") is { } window ? _reader.Duration(window) ?? defaults.Window : defaults.Window,
        };

        if (rateLimit.Window <= TimeSpan.Zero) errors.Add(map.PathOf("window"), "must be longer than 0");

        map.ReportUnknownKeys();
        return rateLimit;
    }

    private MessageConfiguration BindMessage(ConfigurationNode node)
    {
        if (_reader.Mapping(node) is not { } map) return new MessageConfiguration();

        var body = map.Get("body") is { } bodyNode ? _reader.Bool(bodyNode) ?? true : true;
        var includeHeaders = false;
        List<string>? headerNames = null;

        if (map.Get("headers") is ConfigurationSequence headersList)
        {
            includeHeaders = true;
            headerNames = _reader.StringList(headersList);
        }
        else if (map.Get("headers") is { } headersNode)
        {
            includeHeaders = _reader.Bool(headersNode) ?? false;
        }

        var metadata = new Dictionary<string, string>();
        if (map.Get("metadata") is { } metadataNode)
        {
            if (metadataNode is ConfigurationMapping metadataMap)
            {
                foreach (var (key, valueNode) in metadataMap.Entries)
                    if (_reader.String(valueNode) is { } value)
                        metadata[key] = value;
            }
            else
            {
                errors.Add(metadataNode.Path, "expected a mapping of keys and values");
            }
        }

        map.ReportUnknownKeys();
        return new MessageConfiguration
        {
            Body = body,
            IncludeHeaders = includeHeaders,
            HeaderNames = headerNames,
            Metadata = metadata,
        };
    }

    private List<RouteRule>? BindRoute(ConfigurationNode node)
    {
        if (_reader.Sequence(node) is not { } sequence) return null;

        if (sequence.Items.Count == 0)
        {
            errors.Add(node.Path, "needs at least one rule");
            return [];
        }

        var rules = new List<RouteRule>();
        for (var index = 0; index < sequence.Items.Count; index++)
        {
            var item = sequence.Items[index];
            if (_reader.Mapping(item) is not { } map) continue;

            var whenNode = map.Get("when");
            var when = whenNode is not null ? BindCondition(whenNode, depth: 1) : null;
            var isLast = index == sequence.Items.Count - 1;

            if (whenNode is null && !isLast)
                errors.Add(item.Path, "only the last rule may leave out 'when'; the rules after it could never match");
            else if (whenNode is not null && isLast)
                errors.Add(map.PathOf("when"), "the last rule can't have 'when'; add a final rule with only 'to'");

            var toNode = map.Get("to");
            if (toNode is null) errors.Add(map.PathOf("to"), "required (use 'to: none' to publish nothing)");

            map.ReportUnknownKeys();
            rules.Add(new RouteRule { When = when, To = toNode is not null ? BindTargets(toNode) : [] });
        }

        return rules;
    }

    private List<Target> BindTargets(ConfigurationNode node)
    {
        switch (node)
        {
            case ConfigurationScalar { Value: NoTargets }:
                return [];

            case ConfigurationSequence sequence:
                var targets = new List<Target>();
                foreach (var item in sequence.Items)
                {
                    if (item is ConfigurationScalar { Value: NoTargets })
                        errors.Add(item.Path, $"'{NoTargets}' can't be combined with other targets");
                    else if (BindTarget(item) is { } target)
                        targets.Add(target);
                }

                return targets;

            default:
                return BindTarget(node) is { } single ? [single] : [];
        }
    }

    private Target? BindTarget(ConfigurationNode node)
    {
        if (node is ConfigurationMapping)
        {
            var map = _reader.Mapping(node)!;
            var broker = map.Get("broker") is { } brokerNode ? _reader.String(brokerNode) : null;
            var topic = map.Get("topic") is { } topicNode ? _reader.String(topicNode) : null;

            if (map.Get("partition_key") is { } partitionKey)
                errors.Add(partitionKey.Path,
                    "needs runax-messaging's partition key support (prerequisite P2), which isn't released yet");

            map.ReportUnknownKeys();

            if (broker is null)
            {
                errors.Add(map.PathOf("broker"), "required");
                return null;
            }

            return CheckTopic(new Target(broker, topic), map.PathOf("topic"));
        }

        if (_reader.String(node) is not { } text) return null;

        var separator = text.IndexOf(':');
        var target = separator < 0 ? new Target(text, null) : new Target(text[..separator], text[(separator + 1)..]);

        if (target.Broker.Length == 0)
        {
            errors.Add(node.Path, $"{Show(node, text)} names no broker; write 'broker:topic'");
            return null;
        }

        if (node is ConfigurationScalar { IsSecret: true })
        {
            errors.Add(node.Path, "targets can't be interpolated with '${…}'");
            return null;
        }

        return CheckTopic(target, node.Path);
    }

    private Target? CheckTopic(Target target, ConfigurationNodePath path)
    {
        if (target.Topic is null) return target;

        if (target.Topic.Length == 0)
        {
            errors.Add(path, $"target '{target.Broker}:' has an empty topic");
            return null;
        }

        if (target.Topic.Contains('{') || target.Topic.Contains('}'))
        {
            errors.Add(path, $"topic '{target.Topic}': templates in topics aren't supported yet");
            return null;
        }

        return target;
    }

    private Condition? BindCondition(ConfigurationNode node, int depth)
    {
        if (depth > Condition.MaxDepth)
        {
            errors.Add(node.Path, $"conditions nest deeper than {Condition.MaxDepth} levels");
            return null;
        }

        if (node is ConfigurationSequence sequence)
            return BindConditionList(sequence, depth, conditions => new AllCondition(conditions));

        if (_reader.Mapping(node) is not { } map) return null;

        var combinators = CombinatorKeys.Where(map.Has).ToList();
        if (combinators.Count > 0) return BindCombinator(node, map, combinators, depth);

        return BindComparison(node, map);
    }

    private Condition? BindCombinator(ConfigurationNode node, MappingReader map, List<string> combinators, int depth)
    {
        var keyCount = ((ConfigurationMapping)node).Entries.Count;
        if (combinators.Count > 1 || keyCount > 1)
        {
            errors.Add(node.Path, "'all', 'any', and 'not' must be the only key in their condition");
            return null;
        }

        var key = combinators[0];
        var child = map.Get(key)!;
        map.ReportUnknownKeys();

        if (key == "not")
            return BindCondition(child, depth + 1) is { } negated ? new NotCondition(negated) : null;

        if (_reader.Sequence(child) is not { } list) return null;

        return key == "all"
            ? BindConditionList(list, depth + 1, conditions => new AllCondition(conditions))
            : BindConditionList(list, depth + 1, conditions => new AnyCondition(conditions));
    }

    private Condition? BindConditionList(
        ConfigurationSequence list,
        int depth,
        Func<IReadOnlyList<Condition>, Condition> create)
    {
        if (list.Items.Count == 0)
        {
            errors.Add(list.Path, "needs at least one condition");
            return null;
        }

        var conditions = list.Items.Select(item => BindCondition(item, depth + 1)).ToList();
        return conditions.All(condition => condition is not null) ? create(conditions!) : null;
    }

    private CompareCondition? BindComparison(ConfigurationNode node, MappingReader map)
    {
        var sources = SourceKeys.Where(map.Has).ToList();
        var operators = OperatorKeys.Keys.Where(map.Has).ToList();

        if (sources.Count != 1)
            errors.Add(node.Path, sources.Count == 0
                ? "needs one of 'jsonpath', 'header', or 'path' (or 'all', 'any', 'not')"
                : $"has more than one source ({string.Join(", ", sources)}); use 'all' to combine them");

        if (operators.Count != 1)
            errors.Add(node.Path, operators.Count == 0
                ? "needs an operator such as 'eq', 'in', 'gte', 'contains', 'regex', or 'exists'"
                : "has more than one operator; use 'all' to combine them");

        if (sources.Count != 1 || operators.Count != 1)
        {
            foreach (var key in SourceKeys.Concat(OperatorKeys.Keys)) map.Get(key);
            map.ReportUnknownKeys();
            return null;
        }

        var sourceNode = map.Get(sources[0])!;
        var operatorNode = map.Get(operators[0])!;
        var @operator = OperatorKeys[operators[0]];
        map.ReportUnknownKeys();

        var selector = _reader.String(sourceNode);
        var operands = @operator is ConditionOperator.In or ConditionOperator.NotIn
            ? BindOperandList(operatorNode)
            : _reader.Operand(operatorNode) is { } operand ? [operand] : null;

        if (selector is null || operands is null) return null;

        try
        {
            return new CompareCondition(Enum.Parse<ConditionSource>(sources[0], ignoreCase: true), selector, @operator,
                operands);
        }
        catch (ArgumentException ex)
        {
            var errorNode = ex.Message.Contains("JSONPath", StringComparison.Ordinal) ? sourceNode : operatorNode;
            errors.Add(errorNode.Path,
                IsSecret(sourceNode) || IsSecret(operatorNode) ? "invalid interpolated value" : ex.Message);
            return null;
        }
    }

    private List<ConditionValue>? BindOperandList(ConfigurationNode node)
    {
        if (node is not ConfigurationSequence sequence)
        {
            errors.Add(node.Path, "expected a list of values, such as [paid, refunded]");
            return null;
        }

        var operands = sequence.Items.Select(_reader.Operand).ToList();
        return operands.All(operand => operand is not null) ? operands.Select(operand => operand!.Value).ToList() : null;
    }

    private HookpipeConfiguration CheckReferences(HookpipeConfiguration configuration, ConfigurationNodePath webhooksPath)
    {
        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolvedWebhooks = new Dictionary<string, WebhookConfiguration>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, webhook) in configuration.Webhooks)
        {
            var path = webhooksPath.Child(id);
            var parameters = CheckRoute(id, webhook, configuration.Server.Paths.Webhooks, path, routes);

            foreach (var (rule, index) in (webhook.Route ?? []).Select((rule, index) => (rule, index)))
                foreach (var parameter in rule.When?.PathParameters ?? [])
                    if (!parameters.Contains(parameter))
                        errors.Add(path.Child("route").Item(index).Child("when"),
                            $"path parameter '{parameter}' isn't in the webhook's path '{webhook.Path}'");

            resolvedWebhooks[id] = new WebhookConfiguration
            {
                Path = webhook.Path,
                Methods = webhook.Methods,
                Validate = webhook.Validate,
                RateLimit = webhook.RateLimit,
                Message = webhook.Message,
                To = webhook.To is null ? null : ResolveTargets(webhook.To, configuration.Brokers, path.Child("to")),
                Route = webhook.Route?.Select((rule, index) => new RouteRule
                {
                    When = rule.When,
                    To = ResolveTargets(rule.To, configuration.Brokers, path.Child("route").Item(index).Child("to")),
                }).ToList(),
            };
        }

        return new HookpipeConfiguration { Server = configuration.Server, Brokers = configuration.Brokers, Webhooks = resolvedWebhooks };
    }

    private HashSet<string> CheckRoute(
        string id,
        WebhookConfiguration webhook,
        string prefix,
        ConfigurationNodePath path,
        Dictionary<string, string> routes)
    {
        var parameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var route = HookpipeRoutes.Combine(prefix, webhook.Path);
        RoutePattern pattern;

        try
        {
            pattern = RoutePatternFactory.Parse(route);
        }
        catch (RoutePatternException ex)
        {
            errors.Add(path.Child("path"), $"route '{route}' isn't valid: {ex.Message}");
            return parameters;
        }

        foreach (var parameter in pattern.Parameters)
        {
            if (parameter.IsCatchAll || parameter.IsOptional || parameter.Default is not null ||
                parameter.ParameterPolicies.Count > 0 || !ParameterPattern().IsMatch(parameter.Name))
                errors.Add(path.Child("path"),
                    $"path parameter '{parameter.Name}' must be a plain name such as {{source}}");
            else if (ReservedPathParameters.Contains(parameter.Name))
                errors.Add(path.Child("path"), $"path parameter '{parameter.Name}' uses a reserved name");

            parameters.Add(parameter.Name);
        }

        if (HookpipeRoutes.IsReserved(route))
            errors.Add(path.Child("path"), $"route '{route}' would shadow the service's /health or /metrics routes");

        var key = HookpipeRoutes.Shape(pattern);
        if (!routes.TryAdd(key, id))
            errors.Add(path.Child("path"), $"route '{route}' is the same as webhook '{routes[key]}'s route");

        return parameters;
    }

    private List<Target> ResolveTargets(
        IReadOnlyList<Target> targets,
        IReadOnlyDictionary<string, BrokerConfiguration> brokers,
        ConfigurationNodePath path)
    {
        var resolved = new List<Target>();
        foreach (var target in targets)
        {
            var brokerId = brokers.Keys.FirstOrDefault(id => string.Equals(id, target.Broker, StringComparison.OrdinalIgnoreCase));
            if (brokerId is null)
            {
                errors.Add(path, $"target '{target}' names unknown broker '{target.Broker}'");
                continue;
            }

            if (BrokerTypes.All.TryGetValue(brokers[brokerId].Type, out var type))
            {
                if (type.Topics == TopicUse.Required && target.Topic is null)
                    errors.Add(path, $"target '{target}' needs a topic, such as '{target.Broker}:<topic>'");
                else if (type.Topics == TopicUse.None && target.Topic is not null)
                    errors.Add(path, $"target '{target}': '{type.Name}' brokers take no topic; write just '{target.Broker}'");
            }

            if (target.Topic is not null && brokers[brokerId].Settings is IKnownTopics known && !known.HasTopic(target.Topic))
                errors.Add(path, $"target '{target}': topic '{target.Topic}' isn't in brokers.{brokerId}.settings.{known.TopicsKey}");

            resolved.Add(target with { Broker = brokerId });
        }

        return resolved;
    }

    private static string Show(ConfigurationNode node, string value) => IsSecret(node) ? "the interpolated value" : $"'{value}'";

    private static bool IsSecret(ConfigurationNode node) =>
        node is ConfigurationScalar { IsSecret: true } ||
        node is ConfigurationSequence sequence && sequence.Items.Any(IsSecret);

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z]+$")]
    private static partial Regex MethodPattern();

    [GeneratedRegex(@"^\w+$")]
    private static partial Regex ParameterPattern();
}
