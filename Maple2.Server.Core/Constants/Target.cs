using System.Net;
using Maple2.Tools;

namespace Maple2.Server.Core.Constants;

public static class Target {
    public const string SERVER_NAME = "Paperwood";
    // The same LOCALE setting the ingest filtered the data by; an unknown value stops the server.
    public static readonly string LOCALE = Locale.Read();

    public static readonly IPAddress LoginIp = IPAddress.Loopback;
    public static readonly ushort LoginPort = 20001;

    public static readonly ushort GrpcLoginPort = 21000;

    public static readonly IPAddress GameIp = IPAddress.Loopback;
    public static readonly ushort BaseGamePort = 20002;

    public static readonly string GrpcGameIp = IPAddress.Loopback.ToString();

    public static readonly Uri WebUri = new("http://localhost");

    public static readonly IPAddress GrpcWorldIp = IPAddress.Loopback;
    public static readonly ushort GrpcWorldPort = 21001;
    public static readonly Uri GrpcWorldUri = new($"http://{GrpcWorldIp}:{GrpcWorldPort}");

    public static readonly ushort BaseGrpcChannelPort = 21002;

    public static readonly bool InstancedContent = false;

    // Players a game channel is meant to hold, for the server list's traffic. Neither the client nor the KR data
    // names a number, so CHANNEL_CAPACITY sets it.
    public static readonly int ChannelCapacity = 200;

    // Address every listener binds to. MS2_BIND_IP unset or not an IP address = loopback only; "0.0.0.0" = all interfaces.
    public static readonly IPAddress BindAddress =
        IPAddress.TryParse(Environment.GetEnvironmentVariable("MS2_BIND_IP"), out IPAddress? bindAddress) ? bindAddress : IPAddress.Loopback;

    static Target() {
        if (IPAddress.TryParse(Environment.GetEnvironmentVariable("LOGIN_IP"), out IPAddress? loginIpAddress)) {
            LoginIp = loginIpAddress;
        }
        if (ushort.TryParse(Environment.GetEnvironmentVariable("LOGIN_PORT"), out ushort loginPortOverride)) {
            LoginPort = loginPortOverride;
        }
        if (ushort.TryParse(Environment.GetEnvironmentVariable("GRPC_LOGIN_PORT"), out ushort grpcLoginPortOverride)) {
            GrpcLoginPort = grpcLoginPortOverride;
        }
        if (ushort.TryParse(Environment.GetEnvironmentVariable("GAME_PORT_BASE"), out ushort gamePortBaseOverride)) {
            BaseGamePort = gamePortBaseOverride;
        }
        if (ushort.TryParse(Environment.GetEnvironmentVariable("GRPC_CHANNEL_PORT_BASE"), out ushort grpcChannelPortBaseOverride)) {
            BaseGrpcChannelPort = grpcChannelPortBaseOverride;
        }

        if (IPAddress.TryParse(Environment.GetEnvironmentVariable("GAME_IP"), out IPAddress? gameIpAddress)) {
            GameIp = gameIpAddress;
        }

        string? grpcGameIpEnv = Environment.GetEnvironmentVariable("GRPC_GAME_IP");
        if (IPAddress.TryParse(grpcGameIpEnv, out IPAddress? grpcGameIpAddress)) {
            GrpcGameIp = grpcGameIpAddress.ToString();
        }

        string? grpcWorldIpEnv = Environment.GetEnvironmentVariable("GRPC_WORLD_IP");
        if (IPAddress.TryParse(grpcWorldIpEnv, out IPAddress? grpcWorldIpOverride)) {
            GrpcWorldIp = grpcWorldIpOverride;
        }

        if (ushort.TryParse(Environment.GetEnvironmentVariable("GRPC_WORLD_PORT"), out ushort grpcWorldPortOverride)) {
            GrpcWorldPort = grpcWorldPortOverride;
        }

        GrpcWorldUri = new Uri($"http://{GrpcWorldIp}:{GrpcWorldPort}");

        bool isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        if (isDocker) {
            grpcWorldIpEnv ??= IPAddress.Loopback.ToString();
            GrpcWorldUri = new Uri($"http://{grpcWorldIpEnv}:{GrpcWorldPort}");
            if (!string.IsNullOrEmpty(grpcGameIpEnv)) {
                GrpcGameIp = grpcGameIpEnv;
            }
        }

        string webIP = Environment.GetEnvironmentVariable("WEB_IP") ?? "localhost";
        string webPort = Environment.GetEnvironmentVariable("WEB_PORT") ?? "4000";

        if (Uri.TryCreate($"http://{webIP}:{webPort}", UriKind.Absolute, out Uri? webUriOverride)) {
            WebUri = webUriOverride;
        }

        if (bool.TryParse(Environment.GetEnvironmentVariable("INSTANCED_CONTENT"), out bool instancedOverride)) {
            InstancedContent = instancedOverride;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("CHANNEL_CAPACITY"), out int channelCapacity) && channelCapacity > 0) {
            ChannelCapacity = channelCapacity;
        }
    }
}
