using Maple2.Server.Core.Helpers;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Maple2.Server.Game.Util;

// Runs the WorldLink check for as long as the Game server runs.
public class WorldLinkService : BackgroundService {
    private readonly WorldLink link;
    private readonly ILogger logger = Log.ForContext<WorldLinkService>();

    public WorldLinkService(WorldLink link) {
        this.link = link;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay(ChannelRegisterRules.RegisterIntervalMs, stoppingToken);
            } catch (OperationCanceledException) {
                break;
            }

            try {
                WorldLink.Step before = link.Last;
                WorldLink.Step step = link.Check();
                if (step == before && step != WorldLink.Step.Registered) {
                    continue;
                }
                switch (step) {
                    case WorldLink.Step.Down:
                        logger.Warning("The World server does not answer");
                        break;
                    case WorldLink.Step.Registered:
                        logger.Information("Registered this channel with the World server again");
                        break;
                    case WorldLink.Step.Refused:
                        logger.Error("The World server gave this channel's id to another Game server");
                        break;
                    case WorldLink.Step.Known:
                        logger.Information("The World server answers again");
                        break;
                }
            } catch (Exception ex) {
                logger.Warning(ex, "World registration check failed");
            }
        }
    }
}
