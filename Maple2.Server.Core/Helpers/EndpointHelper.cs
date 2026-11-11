using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Maple2.Server.Core.Helpers;

public static class EndpointHelper {
    // The address a migration sends the client to; false for a text that is no IP address or a port outside 1-65535.
    public static bool TryEndpoint(string? address, int port, [NotNullWhen(true)] out IPEndPoint? endpoint) {
        endpoint = null;
        if (port is < 1 or > IPEndPoint.MaxPort || !IPAddress.TryParse(address, out IPAddress? ip)) {
            return false;
        }
        endpoint = new IPEndPoint(ip, port);
        return true;
    }
}
