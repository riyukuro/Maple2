using System.Net;
using Grpc.Net.ClientFactory;
using Maple2.Server.Core.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Maple2.Server.Core.Modules;

public class WorldClientModule : GrpcClientModule {
    public override void Configure(IServiceCollection services) {
        ConfigureWorld(services.AddGrpcClient<World.Service.World.WorldClient>(Options));
        services.AddGrpcClient<Global.Service.Global.GlobalClient>(Options);
    }

    // A server's own settings of its World client (the Game server's default call deadlines).
    protected virtual void ConfigureWorld(IHttpClientBuilder world) { }

    protected override void Options(GrpcClientFactoryOptions options) {
        string worldService = Environment.GetEnvironmentVariable("GRPC_WORLD_IP") ?? IPAddress.Loopback.ToString();
        options.Address = new Uri($"http://{worldService}:{Target.GrpcWorldPort}");
    }
}
