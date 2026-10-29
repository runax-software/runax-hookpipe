using Microsoft.AspNetCore.Routing.Patterns;

namespace Runax.Hookpipe.Configuration;

/// <summary>
/// Builds and compares the full routes webhooks are served at.
/// </summary>
internal static class HookpipeRoutes
{
    /// <summary>
    /// Joins a prefix and a webhook path: <c>/webhooks</c> and <c>github</c> give <c>/webhooks/github</c>;
    /// <c>/</c> and <c>github</c> give <c>/github</c>.
    /// </summary>
    public static string Combine(string prefix, string path) =>
        prefix == "/" ? $"/{path}" : $"{prefix}/{path}";

    /// <summary>
    /// Whether a route is one the service serves itself: <c>/health</c>, anything under it, or <c>/metrics</c>.
    /// </summary>
    public static bool IsReserved(string route) =>
        route.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
        route.StartsWith("/health/", StringComparison.OrdinalIgnoreCase) ||
        route.Equals("/metrics", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A route's shape for comparing routes: literals in lower case, every parameter as <c>{}</c>, so
    /// <c>/ingest/{source}</c> and <c>/Ingest/{name}</c> compare equal.
    /// </summary>
    public static string Shape(RoutePattern pattern) =>
        "/" + string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part =>
            part switch
            {
                RoutePatternLiteralPart literal => literal.Content.ToLowerInvariant(),
                RoutePatternParameterPart => "{}",
                _ => part.ToString(),
            }))));
}
