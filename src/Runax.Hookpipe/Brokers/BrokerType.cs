namespace Runax.Hookpipe.Brokers;

/// <summary>
/// One broker type: its name in configuration, its settings class, and how its targets use topics.
/// </summary>
/// <param name="Name">The <c>type</c> value, such as <c>rabbitmq</c>.</param>
/// <param name="SettingsType">The class <c>settings</c> binds to, or null for types without settings.</param>
/// <param name="Topics">Whether targets on this type name a topic.</param>
internal sealed record BrokerType(string Name, Type? SettingsType, TopicUse Topics);
