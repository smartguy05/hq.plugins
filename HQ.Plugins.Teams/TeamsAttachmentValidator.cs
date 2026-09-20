using System.Net;
using System.Net.Sockets;

namespace HQ.Plugins.Teams;

/// <summary>
/// WP6B-8: TeamsBot.OnMessageActivityAsync used to download any inbound attachment's
/// `contentUrl` with a bare HttpClient -- no scheme check, no host allow-list, and no
/// protection against a literal internal/loopback/cloud-metadata IP address or a hostname whose
/// DNS record resolves to one -- then base64'd the body straight into the agent prompt. This is
/// SSRF from the HQ host into the internal network / cloud metadata, unauthenticated when
/// combined with a reachable listener (WP6B-1).
///
/// Deny-by-default: only http/https; blocks loopback, link-local, RFC1918, carrier-grade NAT
/// (100.64.0.0/10), IPv6 unique-local/link-local, the unspecified addresses, IPv4-mapped-IPv6,
/// and 'localhost'/'*.localhost'. <see cref="Dns.GetHostAddresses(string)"/> is wired in as the
/// default host resolver so a hostname is checked by the address it would actually connect to,
/// not just by a literal-IP contentUrl. <see cref="IsRedirectAllowed"/> re-applies the same
/// checks to a `Location` header so a first-hop-clean URL cannot redirect into a blocked address
/// (TeamsBot must call it on every hop instead of letting HttpClient auto-follow redirects).
///
/// This intentionally has no dependency on HQ.Models -- it is a self-contained copy of the same
/// deny-by-default logic HQ.Models.Safety.UrlGuard implements host-side (round-2 SSRF review,
/// WP3-3), kept in-plugin so this cluster's fix doesn't require bumping the shared HQ.Models
/// package reference (that bump is out of this cluster's owned files -- see review follow-up).
/// </summary>
public static class TeamsAttachmentValidator
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https" };

    /// <summary>Test seam: pass a custom resolver instead of doing real DNS I/O.</summary>
    public static bool IsAttachmentUrlAllowed(string contentUrl, out string reason,
        Func<string, IEnumerable<IPAddress>> resolveHost = null)
    {
        if (string.IsNullOrWhiteSpace(contentUrl))
        {
            reason = "URL is required.";
            return false;
        }

        if (!Uri.TryCreate(contentUrl, UriKind.Absolute, out var uri))
        {
            reason = "URL is not a valid absolute URI.";
            return false;
        }

        return IsUriAllowed(uri, out reason, resolveHost ?? Dns.GetHostAddresses);
    }

    /// <summary>
    /// Re-validates a redirect `Location` header (absolute or relative to
    /// <paramref name="currentUri"/>) with the same rules as the original request, so a
    /// first-hop-clean contentUrl cannot redirect into a blocked address. Callers must disable
    /// automatic redirect-following and call this on every hop before following it.
    /// </summary>
    public static bool IsRedirectAllowed(Uri currentUri, string location, out Uri target, out string reason,
        Func<string, IEnumerable<IPAddress>> resolveHost = null)
    {
        target = null;

        if (string.IsNullOrWhiteSpace(location))
        {
            reason = "Redirect location is empty.";
            return false;
        }

        if (currentUri == null || !Uri.TryCreate(currentUri, location, out var resolved))
        {
            reason = "Redirect location is not a valid URI.";
            return false;
        }

        if (!IsUriAllowed(resolved, out reason, resolveHost ?? Dns.GetHostAddresses))
            return false;

        target = resolved;
        reason = null;
        return true;
    }

    private static bool IsUriAllowed(Uri uri, out string reason, Func<string, IEnumerable<IPAddress>> resolveHost)
    {
        if (!AllowedSchemes.Contains(uri.Scheme))
        {
            reason = $"URL scheme '{uri.Scheme}' is not allowed. Only http and https are permitted.";
            return false;
        }

        var host = uri.DnsSafeHost;

        if (IsLocalhostName(host))
        {
            reason = $"host '{host}' is blocked (localhost).";
            return false;
        }

        // .NET's Uri parser already canonicalizes decimal/octal/hex/short-form IPv4 literals
        // into dotted-quad form in DnsSafeHost, and bracketed IPv6 literals into their
        // unbracketed form -- so a plain IPAddress.TryParse here catches every literal-address
        // bypass, not just the obvious one.
        if (IPAddress.TryParse(host, out var literalIp))
            return IsAddressAllowed(literalIp, out reason);

        try
        {
            var resolved = resolveHost(host) ?? Array.Empty<IPAddress>();
            foreach (var address in resolved)
            {
                if (!IsAddressAllowed(address, out reason))
                    return false;
            }
        }
        catch (Exception ex)
        {
            reason = $"failed to resolve host '{host}': {ex.Message}";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool IsAddressAllowed(IPAddress address, out string reason)
    {
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IPAddress.IsLoopback(ip))
        {
            reason = "address is a loopback address.";
            return false;
        }

        if (ip.Equals(IPAddress.Any))
        {
            reason = "address is 0.0.0.0.";
            return false;
        }

        if (ip.Equals(IPAddress.IPv6Any))
        {
            reason = "address is the IPv6 unspecified address [::].";
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0)
            {
                reason = "address is in 0.0.0.0/8.";
                return false;
            }
            if (b[0] == 169 && b[1] == 254)
            {
                reason = "address is link-local (169.254.0.0/16).";
                return false;
            }
            if (b[0] == 10)
            {
                reason = "address is in the private range 10.0.0.0/8.";
                return false;
            }
            if (b[0] == 172 && b[1] is >= 16 and <= 31)
            {
                reason = "address is in the private range 172.16.0.0/12.";
                return false;
            }
            if (b[0] == 192 && b[1] == 168)
            {
                reason = "address is in the private range 192.168.0.0/16.";
                return false;
            }
            if (b[0] == 100 && b[1] is >= 64 and <= 127)
            {
                reason = "address is in the carrier-grade NAT range 100.64.0.0/10.";
                return false;
            }
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal)
            {
                reason = "address is IPv6 link-local (fe80::/10).";
                return false;
            }

            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC)
            {
                reason = "address is IPv6 unique-local (fc00::/7).";
                return false;
            }
        }

        reason = null;
        return true;
    }

    private static bool IsLocalhostName(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
}
