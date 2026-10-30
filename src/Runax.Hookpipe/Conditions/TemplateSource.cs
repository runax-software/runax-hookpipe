namespace Runax.Hookpipe.Conditions;

/// <summary>
/// Where a template placeholder takes its value from.
/// </summary>
public enum TemplateSource
{
    /// <summary>A path parameter of the webhook's route, written <c>{source}</c>.</summary>
    PathParameter,

    /// <summary>The webhook's ID, written <c>{webhook_id}</c>.</summary>
    WebhookId,

    /// <summary>
    /// The target's topic, written <c>{topic}</c>. Reserved for ordering keys; topics and metadata
    /// can't use it.
    /// </summary>
    Topic,

    /// <summary>A <c>message.metadata</c> value, written <c>{metadata.key}</c>.</summary>
    Metadata,

    /// <summary>A request header, written <c>{header.X-Name}</c>; the name is case-insensitive.</summary>
    Header,

    /// <summary>One scalar from the JSON body, written <c>{$.customer.id}</c>.</summary>
    JsonPath,
}
