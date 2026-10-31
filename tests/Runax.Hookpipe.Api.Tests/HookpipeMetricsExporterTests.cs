using Shouldly;

namespace Runax.Hookpipe.Api.Tests;

public sealed class HookpipeMetricsExporterTests
{
    [Theory]
    [InlineData("hookpipe.requests", null, true, "hookpipe_requests_total")]
    [InlineData("hookpipe.messages.published", null, true, "hookpipe_messages_published_total")]
    [InlineData("hookpipe.publish.errors", null, true, "hookpipe_publish_errors_total")]
    [InlineData("hookpipe.validation.failures", null, true, "hookpipe_validation_failures_total")]
    [InlineData("hookpipe.request.duration", "s", false, "hookpipe_request_duration_seconds")]
    public void PrometheusName_KeepsTheExportedNames(string instrument, string? unit, bool isCounter, string expected) =>
        HookpipeMetricsExporter.PrometheusName(instrument, unit, isCounter).ShouldBe(expected);
}
