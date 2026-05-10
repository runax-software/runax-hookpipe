using System.Net;
using Hookpipe.Core.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Validation;

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
    /// Returns false if: config.Auth is null, the env var is not set or empty,
    /// the remote IP cannot be determined, or the IP is not in the allowlist.
    /// Supports both individual IPs and CIDR notation.
    /// </remarks>
    public Task<bool> ValidateAsync(HttpContext context, ValidationConfig config)
    {
        if (config.Auth is null)
        {
            logger.LogDebug("[Hookpipe.Validator:ip-allowlist] Skipped: auth config is null");
            return Task.FromResult(false);
        }

        var allowlistRaw = Environment.GetEnvironmentVariable(config.Auth.TokenEnv);
        if (string.IsNullOrEmpty(allowlistRaw))
        {
            logger.LogDebug("[Hookpipe.Validator:ip-allowlist] Failed: env var '{TokenEnv}' is not set",
                config.Auth.TokenEnv);
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

        var entries = allowlistRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in entries)
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

    private static bool IsInCidr(IPAddress ip, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var network) || !int.TryParse(parts[1], out var
                prefixLength))
            return false;

        var networkBytes = network.GetAddressBytes();
        var ipBytes = ip.GetAddressBytes();
        if (networkBytes.Length != ipBytes.Length)
            return false;

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
