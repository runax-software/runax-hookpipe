using System.Text.Json;

namespace Runax.Hookpipe.Configuration;

/// <summary>
/// Compares a reloaded configuration with the one the process started with.
/// </summary>
/// <param name="RestartRequired">Changes that only take effect after a restart, such as <c>brokers.audit (added)</c>.</param>
/// <param name="Rejections">Reasons the reload can't be applied at all.</param>
internal sealed record ConfigurationReloadCheck(IReadOnlyList<string> RestartRequired, IReadOnlyList<string> Rejections)
{
    /// <summary>
    /// Lists what a reload would change that needs a restart, and what makes it impossible: a target
    /// on a broker that isn't running, or whose running type differs.
    /// </summary>
    public static ConfigurationReloadCheck Compare(HookpipeConfiguration running, HookpipeConfiguration next)
    {
        var restart = new List<string>();
        var rejections = new List<string>();

        if (running.Server.Paths.Webhooks != next.Server.Paths.Webhooks)
            restart.Add("server.paths.webhooks (changed)");

        CompareBrokers(running, next, restart);
        CompareWebhooks(running, next, restart);

        foreach (var (webhookId, webhook) in next.Webhooks)
            foreach (var target in Targets(webhook))
            {
                if (!running.Brokers.TryGetValue(target.Broker, out var runningBroker))
                    rejections.Add($"webhooks.{webhookId}: target '{target}' uses broker '{target.Broker}', which isn't running");
                else if (runningBroker.Type != next.Brokers[target.Broker].Type)
                    rejections.Add($"webhooks.{webhookId}: target '{target}' uses broker '{target.Broker}', " +
                                   $"which is running as type '{runningBroker.Type}'");
            }

        return new ConfigurationReloadCheck(restart, rejections.Distinct().ToList());
    }

    private static void CompareBrokers(HookpipeConfiguration running, HookpipeConfiguration next, List<string> restart)
    {
        foreach (var id in next.Brokers.Keys.Where(id => !running.Brokers.ContainsKey(id)))
            restart.Add($"brokers.{id} (added)");

        foreach (var (id, broker) in running.Brokers)
        {
            if (!next.Brokers.TryGetValue(id, out var nextBroker)) restart.Add($"brokers.{id} (removed)");
            else if (broker.Type != nextBroker.Type) restart.Add($"brokers.{id}.type (changed)");
            else if (SettingsJson(broker) != SettingsJson(nextBroker)) restart.Add($"brokers.{id}.settings (changed)");
        }
    }

    private static void CompareWebhooks(HookpipeConfiguration running, HookpipeConfiguration next, List<string> restart)
    {
        foreach (var id in next.Webhooks.Keys.Where(id => !running.Webhooks.ContainsKey(id)))
            restart.Add($"webhooks.{id} (added)");

        foreach (var (id, webhook) in running.Webhooks)
        {
            if (!next.Webhooks.TryGetValue(id, out var nextWebhook))
            {
                restart.Add($"webhooks.{id} (removed)");
                continue;
            }

            if (!string.Equals(webhook.Path, nextWebhook.Path, StringComparison.OrdinalIgnoreCase))
                restart.Add($"webhooks.{id}.path (changed)");

            if (webhook.RateLimit?.Requests != nextWebhook.RateLimit?.Requests ||
                webhook.RateLimit?.Window != nextWebhook.RateLimit?.Window)
                restart.Add($"webhooks.{id}.rate_limit (changed)");
        }
    }

    private static IEnumerable<Target> Targets(WebhookConfiguration webhook) =>
        (webhook.To ?? []).Concat((webhook.Route ?? []).SelectMany(rule => rule.To));

    private static string SettingsJson(BrokerConfiguration broker) =>
        broker.Settings is null ? string.Empty : JsonSerializer.Serialize(broker.Settings, broker.Settings.GetType());
}
