using Grpc.Core;
using Maple2.Server.World.Containers;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Maple2.Server.World.Service;

public partial class WorldService : World.WorldBase {
    private readonly ChannelClientLookup channelClients;
    private readonly WorldServer worldServer;
    private readonly PlayerInfoLookup playerLookup;
    private readonly GuildLookup guildLookup;
    private readonly PartyLookup partyLookup;
    private readonly ClubLookup clubLookup;
    private readonly PartySearchLookup partySearchLookup;
    private readonly GroupChatLookup groupChatLookup;
    private readonly BlackMarketLookup blackMarketLookup;
    private readonly GlobalPortalLookup globalPortalLookup;
    private readonly WorldBossLookup worldBossLookup;
    private readonly FieldWarLookup fieldWarLookup;
    private readonly PlayerConfigLookUp playerConfigLookUp;
    private readonly PvpLookup pvpLookup;
    private readonly GuildVsGameLookup guildVsGameLookup;
    private readonly GuildPvpLookup guildPvpLookup;
    private readonly WorldChampionLookup worldChampionLookup;
    private readonly ILogger logger = Log.Logger.ForContext<WorldService>();

    public WorldService(
        IMemoryCache tokenCache, WorldServer worldServer, ChannelClientLookup channelClients,
        PlayerInfoLookup playerLookup, GuildLookup guildLookup, PartyLookup partyLookup,
        PartySearchLookup partySearchLookup, GroupChatLookup groupChatLookup, BlackMarketLookup blackMarketLookup,
        ClubLookup clubLookup, GlobalPortalLookup globalPortalLookup, WorldBossLookup worldBossLookup, PlayerConfigLookUp playerConfigLookUp,
        PvpLookup pvpLookup, FieldWarLookup fieldWarLookup, GuildVsGameLookup guildVsGameLookup,
        GuildPvpLookup guildPvpLookup, WorldChampionLookup worldChampionLookup
        ) {
        this.worldChampionLookup = worldChampionLookup;
        this.tokenCache = tokenCache;
        this.worldServer = worldServer;
        this.channelClients = channelClients;
        this.playerLookup = playerLookup;
        this.guildLookup = guildLookup;
        this.partyLookup = partyLookup;
        this.partySearchLookup = partySearchLookup;
        this.groupChatLookup = groupChatLookup;
        this.clubLookup = clubLookup;
        this.blackMarketLookup = blackMarketLookup;
        this.globalPortalLookup = globalPortalLookup;
        this.worldBossLookup = worldBossLookup;
        this.playerConfigLookUp = playerConfigLookUp;
        this.pvpLookup = pvpLookup;
        this.fieldWarLookup = fieldWarLookup;
        this.guildVsGameLookup = guildVsGameLookup;
        this.guildPvpLookup = guildPvpLookup;
    }

    public override Task<ChannelsResponse> Channels(ChannelsRequest request, ServerCallContext context) {
        var response = new ChannelsResponse();
        var channels = channelClients.Keys.ToHashSet();
        response.Channels.AddRange(channels);
        response.Online = OnlineOn(playerLookup.GetOnlinePlayerInfos().Select(player => player.Channel), channels);
        return Task.FromResult(response);
    }

    // The traffic is the online characters over the listed channels' capacity, so only the characters on a listed
    // channel count: one on an instanced-content or inactive channel, which is not listed, does not.
    internal static int OnlineOn(IEnumerable<short> playerChannels, IReadOnlySet<int> channels) {
        return playerChannels.Count(channel => channels.Contains(channel));
    }
}
