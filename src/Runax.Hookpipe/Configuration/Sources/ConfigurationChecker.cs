using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing.Patterns;
using Runax.Hookpipe.Brokers;
using Runax.Hookpipe.Conditions;

namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// The load-time checks that run on the bound model rather than on a node tree
/// (<see href="https://github.com/runax-software/runax-hookpipe/blob/main/docs/specs/messaging-and-streaming/configuration.md">§5.9</see>):
/// every target names a known broker, routes are unique and don't shadow the service's own, and
/// every template placeholder refers to a path parameter or metadata key that exists. Targets come
/// back with their broker IDs in the casing the broker was defined with.
/// </summary>
/// <remarks>
/// This works on <see cref="HookpipeConfiguration"/> and uses <see cref="ConfigurationNodePath"/>
/// only to label errors, so it applies to any source: YAML, an <c>IConfiguration</c> section, or
/// definitions built in code, which skip the node tree entirely.
/// </remarks>
internal sealed partial class ConfigurationChecker(ConfigurationErrors errors)
{
    private static readonly HashSet<string> ReservedPathParameters =
        new(["webhook_id", "topic"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks references across the whole model and returns it with targets resolved.
    /// </summary>
    /// <param name="configuration">The bound model.</param>
    /// <param name="webhooksPath">Where the webhooks sit, for error messages.</param>
    /// <returns>The configuration, with every target's broker ID in its defined casing.</returns>
    public HookpipeConfiguration Check(HookpipeConfiguration configuration, ConfigurationNodePath webhooksPath)
    {
        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolvedWebhooks = new Dictionary<string, WebhookConfiguration>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, webhook) in configuration.Webhooks)
        {
            var path = webhooksPath.Child(id);
            var parameters = CheckRoute(id, webhook, configuration.Server.Paths.Webhooks, path, routes);

            foreach (var (rule, index) in (webhook.Route ?? []).Select((rule, index) => (rule, index)))
            {
                foreach (var parameter in (rule.When?.PathParameters ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
                    if (!parameters.Contains(parameter))
                        errors.Add(path.Child("route").Item(index).Child("when"),
                            $"path parameter '{parameter}' isn't in the webhook's path '{webhook.Path}'");

                CheckTemplateReferences(rule.To, webhook, parameters, path.Child("route").Item(index).Child("to"));
            }

            CheckTemplateReferences(webhook.To ?? [], webhook, parameters, path.Child("to"));

            foreach (var (key, template) in webhook.Message.MetadataTemplates)
                CheckPlaceholders(template, webhook, parameters, path.Child("message").Child("metadata").Child(key));

            resolvedWebhooks[id] = new WebhookConfiguration
            {
                Path = webhook.Path,
                Methods = webhook.Methods,
                Validate = webhook.Validate,
                RateLimit = webhook.RateLimit,
                MaxBodySize = webhook.MaxBodySize,
                Message = webhook.Message,
                To = webhook.To is null ? null : ResolveTargets(webhook.To, configuration.Brokers, path.Child("to")),
                Route = webhook.Route?.Select((rule, index) => new RouteRule
                {
                    When = rule.When,
                    To = ResolveTargets(rule.To, configuration.Brokers, path.Child("route").Item(index).Child("to")),
                }).ToList(),
            };
        }

        return new HookpipeConfiguration
        {
            Server = configuration.Server,
            Brokers = configuration.Brokers,
            Webhooks = resolvedWebhooks,
        };
    }

    private void CheckTemplateReferences(
        IReadOnlyList<Target> targets,
        WebhookConfiguration webhook,
        HashSet<string> parameters,
        ConfigurationNodePath path)
    {
        foreach (var target in targets)
            if (target.TopicTemplate is { } template)
                CheckPlaceholders(template, webhook, parameters, path);
    }

    /// <summary>
    /// Reports placeholders that name a path parameter the route doesn't have, or a metadata key the
    /// webhook doesn't set.
    /// </summary>
    private void CheckPlaceholders(
        Template template,
        WebhookConfiguration webhook,
        HashSet<string> parameters,
        ConfigurationNodePath path)
    {
        foreach (var placeholder in template.Placeholders)
        {
            if (placeholder.Source == TemplateSource.PathParameter && !parameters.Contains(placeholder.Name))
                errors.Add(path, $"placeholder '{placeholder.Text}': path parameter '{placeholder.Name}' isn't in " +
                                 $"the webhook's path '{webhook.Path}'");
            else if (placeholder.Source == TemplateSource.Metadata && !webhook.Message.Metadata.ContainsKey(placeholder.Name))
                errors.Add(path, $"placeholder '{placeholder.Text}': metadata key '{placeholder.Name}' isn't in " +
                                 "the webhook's message.metadata");
        }
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

            if (target is { Topic: not null, TopicTemplate: null } && brokers[brokerId].Settings is IKnownTopics known &&
                !known.HasTopic(target.Topic))
                errors.Add(path, $"target '{target}': topic '{target.Topic}' isn't in brokers.{brokerId}.settings.{known.TopicsKey}");

            resolved.Add(target with { Broker = brokerId });
        }

        return resolved;
    }

    /// <summary>
    /// Checks a target's topic and attaches its parsed template when it has placeholders. Shared by
    /// every source: the binder calls it while binding, and code definitions call it from the
    /// builders.
    /// </summary>
    /// <returns>The target, with its template, or null when the topic is rejected.</returns>
    internal static Target? CheckTopic(Target target, ConfigurationNodePath path, ConfigurationErrors errors)
    {
        if (target.Topic is null) return target;

        if (target.Topic.Length == 0)
        {
            errors.Add(path, $"target '{target.Broker}:' has an empty topic");
            return null;
        }

        if (!target.Topic.Contains('{') && !target.Topic.Contains('}')) return target;

        Template template;
        try
        {
            template = Template.Parse(target.Topic);
        }
        catch (ArgumentException ex)
        {
            errors.Add(path, $"topic '{target.Topic}': {ex.Message}");
            return null;
        }

        if (!template.HasPlaceholders)
        {
            errors.Add(path, $"topic '{target.Topic}' can't contain '{{' or '}}'");
            return null;
        }

        var valid = true;
        foreach (var placeholder in template.Placeholders.Where(placeholder => placeholder.Source == TemplateSource.Topic))
        {
            errors.Add(path, $"topic '{target.Topic}': placeholder '{placeholder.Text}' can't be used in a topic");
            valid = false;
        }

        foreach (var literal in template.Literals.Where(literal => literal.Length > 0 && !Target.IsSafeTopic(literal)))
        {
            errors.Add(path, $"topic '{target.Topic}': '{literal}' can't be part of a templated topic; " +
                             "rendered topics may only use letters, digits, '.', '_', and '-'");
            valid = false;
        }

        foreach (var placeholder in template.Placeholders.Where(placeholder =>
                     placeholder.Default is { Length: > 0 } fallback && !Target.IsSafeTopic(fallback)))
        {
            errors.Add(path, $"topic '{target.Topic}': the default in '{placeholder.Text}' may only use " +
                             "letters, digits, '.', '_', and '-'");
            valid = false;
        }

        return valid ? target with { TopicTemplate = template } : null;
    }

    [GeneratedRegex(@"^\w+$")]
    private static partial Regex ParameterPattern();
}
