namespace IndigoRouterScheduler;

public sealed record ParsedRouterAddress(string Host, int? ExplicitPort);

public static class RouterAddressParser
{
    public static ParsedRouterAddress Parse(string address)
    {
        string value = address.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Router address is required.");

        // Bracketed IPv6 endpoint: [2001:db8::1]:8290
        if (value.StartsWith('['))
        {
            int close = value.IndexOf(']');
            if (close < 0) throw new FormatException("Invalid bracketed IPv6 address.");
            string host = value[1..close];
            if (close + 1 == value.Length) return new ParsedRouterAddress(host, null);
            if (value[close + 1] != ':' || !TryPort(value[(close + 2)..], out int ipv6Port))
                throw new FormatException("Invalid port after the IPv6 address.");
            return new ParsedRouterAddress(host, ipv6Port);
        }

        // IPv4 or hostname with an explicit suffix. Bare IPv6 contains more than one colon.
        int firstColon = value.IndexOf(':');
        int lastColon = value.LastIndexOf(':');
        if (firstColon > 0 && firstColon == lastColon)
        {
            if (!TryPort(value[(lastColon + 1)..], out int port))
                throw new FormatException("The address contains an invalid port.");
            return new ParsedRouterAddress(value[..lastColon], port);
        }

        return new ParsedRouterAddress(value, null);
    }

    private static bool TryPort(string value, out int port)
        => int.TryParse(value, out port) && port is >= 1 and <= 65535;
}
