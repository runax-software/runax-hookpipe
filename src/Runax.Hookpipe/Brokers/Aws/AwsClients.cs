using Amazon.Runtime;

namespace Runax.Hookpipe.Brokers.Aws;

/// <summary>
/// Shared AWS client setup for the SQS, SNS, and EventBridge brokers.
/// </summary>
internal static class AwsClients
{
    /// <summary>
    /// Points a client configuration at a custom endpoint (such as LocalStack) or a region. Without either,
    /// the SDK resolves the region itself, for example from <c>AWS_REGION</c>.
    /// </summary>
    public static void Configure(ClientConfig configuration, string? region, string? serviceUrl)
    {
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            configuration.ServiceURL = serviceUrl;
            configuration.AuthenticationRegion = region ?? "us-east-1";
        }
        else if (!string.IsNullOrEmpty(region))
        {
            configuration.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);
        }
    }

    /// <summary>
    /// Credentials for a custom endpoint, from <c>AWS_ACCESS_KEY_ID</c> and <c>AWS_SECRET_ACCESS_KEY</c>
    /// or <c>test</c>. Null without a custom endpoint, so the SDK's default credential chain applies.
    /// </summary>
    public static AWSCredentials? EndpointCredentials(string? serviceUrl) =>
        string.IsNullOrEmpty(serviceUrl)
            ? null
            : new BasicAWSCredentials(
                Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "test",
                Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "test");
}
