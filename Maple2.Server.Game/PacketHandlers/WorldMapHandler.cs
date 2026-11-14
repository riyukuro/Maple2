using Grpc.Core;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Game.PacketHandlers;

public class WorldMapHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestWorldmap;

    // The client asks for the crowd view on every world button click (uiworldmapdialog.cpp:496-502), so the world's
    // answer is kept for a few seconds and the call has a deadline; a failed call is kept as no answer for as long.
    internal static readonly TimeSpan PopulationLifetime = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan PopulationDeadline = TimeSpan.FromSeconds(2);
    internal Func<DateTime> Now = () => DateTime.UtcNow;
    private readonly object populationLock = new();
    private MapPopulationResponse? population;
    private DateTime populationExpiry = DateTime.MinValue;

    private enum Command : byte {
        Load = 0,
        Population = 1,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();

        switch (command) {
            case Command.Load:
                HandleLoad(session, packet);
                return;
            case Command.Population:
                HandlePopulation(session, packet);
                return;
        }
    }

    private static void HandleLoad(GameSession session, IByteReader packet) {
        packet.ReadByte();

        // 102 = Victoria, 103 = Karkar, 105 = Kritias
        int mapCode = packet.ReadInt();

        TimeEventResponse bossResponse = session.World.TimeEvent(new TimeEventRequest {
            GetActiveWorldBosses = new TimeEventRequest.Types.GetActiveWorldBosses(),
        });

        var bossGroups = new List<ICollection<MapWorldBoss>>();
        foreach (TimeEventResponse.Types.ActiveWorldBoss active in bossResponse.ActiveWorldBosses) {
            if (!session.ServerTableMetadata.TimeEventTable.WorldBoss.TryGetValue(active.MetadataId, out WorldBossMetadata? metadata)) {
                continue;
            }
            int npcId = metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0;
            int mapId = metadata.TargetMapIds.Length > 0 ? metadata.TargetMapIds[0] : 0;
            bossGroups.Add(active.AliveChannels.Select(ch => new MapWorldBoss(npcId, mapId, (short) ch, active.SpawnTimestamp)).ToList());
        }

        session.Send(WorldMapPacket.Load(bossGroups, []));
    }

    // The crowd view: the asked world's three busiest fields, ranked 1 to 3 (WorldMapPopulationRules). The world's
    // counts are of its online players; without an answer from the world nothing is ranked.
    private void HandlePopulation(GameSession session, IByteReader packet) {
        // 102 = Victoria, 103 = Karkar, 105 = Kritias
        int mapCode = packet.ReadInt();

        var populations = new List<MapPopulation>();
        MapPopulationResponse? response = Population(session);
        if (response != null) {
            HashSet<int> tiles = session.TableMetadata.WorldMapTable.Entries.Select(entry => entry.Code).ToHashSet();
            bool OnWorld(int mapId) => tiles.Contains(mapId) && session.MapMetadata.TryGet(mapId, out MapMetadata? map)
                                       && (int) map.Property.Continent == mapCode;
            IEnumerable<(int, short, int)> counts = response.Entries.Select(entry => (entry.MapId, (short) entry.Channel, entry.Count));
            foreach ((int mapId, int rank, short channel) in WorldMapPopulationRules.Rank(counts, OnWorld)) {
                populations.Add(new MapPopulation { MapId = mapId, Population = rank, Channel = channel });
            }
        }
        session.Send(WorldMapPacket.Population(populations));
    }

    private MapPopulationResponse? Population(GameSession session) {
        lock (populationLock) {
            DateTime now = Now();
            if (now < populationExpiry) {
                return population;
            }
            try {
                population = session.World.MapPopulation(new MapPopulationRequest(), deadline: now + PopulationDeadline);
            } catch (RpcException) {
                population = null;
            }
            populationExpiry = Now() + PopulationLifetime;
            return population;
        }
    }
}
