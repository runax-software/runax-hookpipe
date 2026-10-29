using System.Net;
using Runax.Hookpipe.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Validation;

/// <summary>
/// Validates requests by checking the remote IP address against an allowlist.
/// The allowlist is read from an environment variable as a comma-separated list
/// of IPs or CIDRs (e.g. "192.168.1.0/24,10.0.0.1").
/// </summary>
public sealed class IpAllowlistValidator(ILogger<IpAllowlistValidator> logger) : IValidator
{
    /// <summary>
    /// The validator type identifier.
    /// </summary>
    public const string TypeName = "ip-allowlist";

    /// <inheritdoc />
    public string Type => TypeName;

    /// <inheritdoc />
    /// <remarks>
    /// Returns false if: <c>allow</c> is empty,
    /// the remote IP cannot be determined, or the IP is not in the allowlist.
    /// Supports both individual IPs and CIDR notation.
    /// </remarks>
    public Task<bool> ValidateAsync(HttpContext context, ValidatorConfiguration configuration)
    {
        if (configuration.Allow.Count == 0)
        {
            logger.LogDebug("[Hookpipe.Validator:ip-allowlist] Failed: 'allow' is empty");
            return Task.FromResult(false);
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            logger.LogDebug("[Hookpipe.Validator:ip-allowlist] Failed: remote IP is unknown");
            return Task.FromResult(false);
        }

        // Normalize IPv6-mapped IPv4 (e.g. ::ffff:192.168.1.1 → 192.168.1.1)
        if (remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();

        foreach (var entry in configuration.Allow)
        {
            if (entry.Contains('/'))
            {
                if (!IsInCidr(remoteIp, entry)) continue;

                logger.LogDebug("[Hookpipe.Validator:ip-allowlist] Passed: {RemoteIp} matches CIDR {Cidr}",
                    remoteIp, entry);
                return Task.FromResult(true);
            }

            if (!IPAddress.TryParse(entry, out var allowedIp) || !remoteIp.Equals(allowedIp)) continue;
            logger.LogDebug("[Hookpipe.Validator:ip-allowlist] Passed: {RemoteIp} matches {AllowedIp}",
                remoteIp, entry);
            return Task.FromResult(true);
        }

        logger.LogDebug("[Hookpipe.Validator:ip-allowlist] Failed: {RemoteIp} not in allowlist", remoteIp);
        return Task.FromResult(false);
    }

    /// <summary>
    /// Whether an allowlist entry is an IP address, or a CIDR range whose prefix fits its address
    /// family (0–32 for IPv4, 0–128 for IPv6).
    /// </summary>
    internal static bool IsAddressOrRange(string entry) =>
        entry.Contains('/') ? TryParseCidr(entry, out _, out _) : IPAddress.TryParse(entry, out _);

    private static bool TryParseCidr(string cidr, out IPAddress network, out int prefixLength)
    {
        network = IPAddress.None;
        prefixLength = 0;

        var parts = cidr.Split('/');
        return parts.Length == 2 && IPAddress.TryParse(parts[0], out network!) &&
               int.TryParse(parts[1], out prefixLength) &&
               prefixLength >= 0 && prefixLength <= network.GetAddressBytes().Length * 8;
    }

    private static bool IsInCidr(IPAddress ip, string cidr)
    {
        if (!TryParseCidr(cidr, out var network, out var prefixLength)) return false;

        var networkBytes = network.GetAddressBytes();
        var ipBytes = ip.GetAddressBytes();
        if (networkBytes.Length != ipBytes.Length) return false;

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
            if (networkBytes[i] != ipBytes[i])
                return false;

        if (remainingBits <= 0 || fullBytes >= networkBytes.Length) return true;
        var mask = (byte)(0xFF << (8 - remainingBits));
        return (networkBytes[fullBytes] & mask) == (ipBytes[fullBytes] & mask);
    }
}
