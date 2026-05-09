namespace Hookpipe.Core.Helpers;

/// <summary>
/// Utility methods for safe logging.
/// </summary>
public static class LogHelper
{
    /// <summary>
    /// Masks credentials in a URI string for safe logging.
    /// Replaces "user:password" with "***@".
    /// Returns the original string if it's not a valid URI.
    /// </summary>
    public static string MaskUri(string uri)
    {
        try
        {
            var parsed = new Uri(uri);
            return string.IsNullOrEmpty(parsed.UserInfo) ? uri : uri.Replace(parsed.UserInfo + "@", "***@");
        }
        catch
        {
            return "***";
        }
    }
}
