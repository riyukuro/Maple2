using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Channel.Service;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util.Sync;
using Maple2.Tools.Scheduler;
using Serilog;

namespace Maple2.Server.Tests.Game;

// The World's time event notices to this channel (ChannelService.TimeEvent): the global portal announce and close,
// the world boss rows, warning and close, the field war announce and close, and the field lookup. The ids are made up.
public class ChannelTimeEventTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MapId = 2000001;
    private const int PortalId = 80001;
    private const int BossId = 90001;
    private const int NpcId = 23000001;
    private const int BossObjectId = 9001;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private ChannelService channel = null!;

    private static readonly GlobalPortalMetadata Portal = new(PortalId, 100, DateTime.MinValue, DateTime.MaxValue, TimeSpan.FromHours(1),
        TimeSpan.Zero, TimeSpan.FromMinutes(1), "popup", "sound", [new GlobalPortalMetadata.Field("first", 65000001, 1)]);

    private static readonly WorldBossMetadata Boss = new(BossId, 100, DateTime.MinValue, DateTime.MaxValue, TimeSpan.FromHours(1),
        TimeSpan.Zero, TimeSpan.FromMinutes(15), [MapId], [1], [NpcId], 0, false, false, 0, true, true);

    private void Start(MapType type = MapType.None, short level = 70) {
        test = new TestGameSession();
        test.SetServerTable(new TimeEventTable(new Dictionary<int, GlobalPortalMetadata> { [PortalId] = Portal },
            new Dictionary<int, WorldBossMetadata> { [BossId] = Boss }));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { GlobalPortalMinLevel = 10, FieldWarRequireLevel = 70 });
        test.Start();
        session = test.Session;
        session.Player.Value.Character.Level = level;
        MapMetadata map = TestGameSession.Map(MapId);
        test.EnterMap(map with { Property = map.Property with { Type = type } });
        test.ServeField();
        var server = (GameServer) typeof(GameSession).GetField("server", Any)!.GetValue(session)!;
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session });
        channel = new ChannelService(server, new PlayerInfoStorage(test.World), test.Storage, test.ServerTableMetadata, null!, null!);
        test.TakeSent();
    }

    private TimeEventResponse Send(TimeEventRequest request) => channel.TimeEvent(request, null!).GetAwaiter().GetResult();

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // The field's world boss, last hit lastDamageTick (0: never hit).
    private void AddBoss(long lastDamageTick) {
        FieldManager field = session.Field!;
        typeof(FieldManager).GetField("Scheduler", Any)!.SetValue(field, new EventQueue(Log.Logger));
        var boss = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        GC.SuppressFinalize(boss);
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(boss, BossObjectId);
        typeof(FieldNpc).GetField("<LastDamageTick>k__BackingField", Any)!.SetValue(boss, lastDamageTick);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc> { [BossObjectId] = boss });
        typeof(FieldManager).GetField("worldBossObjectId", Any)!.SetValue(field, BossObjectId);
    }

    private int QueuedOnField() {
        var queue = (ConcurrentQueue<Action>?) typeof(FieldManager).GetField("fieldActions", Any)!.GetValue(session.Field);
        return queue?.Count ?? 0;
    }

    [Test]
    public void AnnounceGlobalPortal_ReachesAPlayerInAnOpenField() {
        Start();
        Send(new TimeEventRequest { AnnounceGlobalPortal = new TimeEventRequest.Types.AnnounceGlobalPortal { MetadataId = PortalId, EventId = 7 } });

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(GlobalPortalPacket.Announce(Portal, 7)) }));
        Assert.That(test.Errors, Is.Empty);
    }

    [TestCase(MapType.Dungeon)]
    [TestCase(MapType.Pvp)]
    [TestCase(MapType.FieldWar)]
    public void AnnounceGlobalPortal_NotInAFieldItIsNotAnnouncedIn(MapType type) {
        Start(type);
        Send(new TimeEventRequest { AnnounceGlobalPortal = new TimeEventRequest.Types.AnnounceGlobalPortal { MetadataId = PortalId, EventId = 7 } });
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void AnnounceGlobalPortal_BelowTheLevel_OrAnUnknownPortal_NothingIsSent() {
        Start(level: 9);
        Send(new TimeEventRequest { AnnounceGlobalPortal = new TimeEventRequest.Types.AnnounceGlobalPortal { MetadataId = PortalId, EventId = 7 } });
        Assert.That(test.TakeSent(), Is.Empty);

        Start();
        Send(new TimeEventRequest { AnnounceGlobalPortal = new TimeEventRequest.Types.AnnounceGlobalPortal { MetadataId = PortalId + 1, EventId = 7 } });
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void CloseGlobalPortal_ClosesTheDialogOfEveryPlayer() {
        Start(MapType.Dungeon);
        Send(new TimeEventRequest { CloseGlobalPortal = new TimeEventRequest.Types.CloseGlobalPortal { EventId = 7 } });
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(GlobalPortalPacket.Close()) }));
    }

    private static (int NpcId, int MapId, short Channel, bool Alive) BossRow(byte[] packet) {
        var reader = new ByteReader(packet, 0);
        reader.ReadShort();
        reader.ReadByte();
        int npcId = reader.ReadInt();
        int mapId = reader.ReadInt();
        short channelId = reader.ReadShort();
        reader.ReadLong();
        return (npcId, mapId, channelId, reader.ReadBool());
    }

    // One row per announced channel (sorted, once each), then the next spawn time; no spawn on this channel.
    [Test]
    public void AnnounceWorldBoss_ARowPerChannel_ThenTheNextSpawn() {
        Start();
        Send(new TimeEventRequest {
            AnnounceWorldBoss = new TimeEventRequest.Types.AnnounceWorldBoss {
                MetadataId = BossId, EventId = 3, NextSpawnTimestamp = 1_800_000_000, NoSpawn = true, Channels = { 3, 1, 3 },
            },
        });

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.WorldShareInfo, SendOp.WorldShareInfo, SendOp.LegionBattle }));
        Assert.That(BossRow(sent[0]), Is.EqualTo((NpcId, MapId, (short) 1, true)));
        Assert.That(BossRow(sent[1]), Is.EqualTo((NpcId, MapId, (short) 3, true)));
        Assert.That(Convert.ToHexString(sent[2]), Is.EqualTo(Hex(LegionBattlePacket.Next(BossId, NpcId, 1_800_000_000))));
        Assert.That(QueuedOnField(), Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }

    // No channels named: the row of this channel; the spawn is queued on the target field's thread.
    [Test]
    public void AnnounceWorldBoss_NoChannels_ThisChannelsRow_AndTheSpawnIsQueued() {
        Start();
        Send(new TimeEventRequest {
            AnnounceWorldBoss = new TimeEventRequest.Types.AnnounceWorldBoss { MetadataId = BossId, EventId = 3, NextSpawnTimestamp = 1_800_000_000 },
        });

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.WorldShareInfo, SendOp.LegionBattle }));
        Assert.That(BossRow(sent[0]).Channel, Is.EqualTo((short) GameServer.GetChannel()));
        Assert.That(QueuedOnField(), Is.EqualTo(1));
    }

    [Test]
    public void UnknownBoss_NothingIsSentOrQueued() {
        Start();
        AddBoss(0);
        Send(new TimeEventRequest { AnnounceWorldBoss = new TimeEventRequest.Types.AnnounceWorldBoss { MetadataId = BossId + 1 } });
        Send(new TimeEventRequest { WarnWorldBoss = new TimeEventRequest.Types.WarnWorldBoss { MetadataId = BossId + 1 } });
        Send(new TimeEventRequest { CloseWorldBoss = new TimeEventRequest.Types.CloseWorldBoss { MetadataId = BossId + 1 } });
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(QueuedOnField(), Is.EqualTo(0));
    }

    // An idle boss gets the "about to leave" notice in its field.
    [Test]
    public void WarnWorldBoss_IdleBoss_TheFieldIsWarned() {
        Start();
        AddBoss(0);
        Send(new TimeEventRequest { WarnWorldBoss = new TimeEventRequest.Types.WarnWorldBoss { MetadataId = BossId, EventId = 3 } });

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext1, NpcId.ToString()),
                NoticePacket.Flags.Message | NoticePacket.Flags.Alert)),
        }));
    }

    // A boss hit just now stays without a warning; a field without a boss (dead here) has nothing to warn.
    [Test]
    public void WarnWorldBoss_HitBossOrNoBoss_NoWarning() {
        Start();
        AddBoss(Environment.TickCount64);
        Send(new TimeEventRequest { WarnWorldBoss = new TimeEventRequest.Types.WarnWorldBoss { MetadataId = BossId, EventId = 3 } });
        Assert.That(test.TakeSent(), Is.Empty);

        Start();
        Send(new TimeEventRequest { WarnWorldBoss = new TimeEventRequest.Types.WarnWorldBoss { MetadataId = BossId, EventId = 3 } });
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The close runs on the field's thread: a never-hit boss leaves with the "disappeared" notice.
    [Test]
    public void CloseWorldBoss_IsQueuedOnTheField_AndTheIdleBossLeaves() {
        Start();
        AddBoss(0);
        Send(new TimeEventRequest { CloseWorldBoss = new TimeEventRequest.Types.CloseWorldBoss { MetadataId = BossId, EventId = 3 } });
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(QueuedOnField(), Is.EqualTo(1));

        session.Field!.RunFieldActions();

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(NoticePacket.Message(new InterfaceText(StringCode.s_timeevent_boss_lifetimetext2, NpcId.ToString()),
                NoticePacket.Flags.Message | NoticePacket.Flags.Alert)),
        }));
        Assert.That(session.Field!.GetWorldBossNpc(), Is.Null);
    }

    [Test]
    public void AnnounceFieldWar_ReachesAnEligiblePlayer_CloseClearsOnlyThatWar() {
        Start();
        Send(new TimeEventRequest { AnnounceFieldWar = new TimeEventRequest.Types.AnnounceFieldWar { FieldWarId = 10000001, EventId = 555 } });

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.FieldWar }));
        Assert.That(session.FieldWarUid, Is.EqualTo(555));

        Send(new TimeEventRequest { CloseFieldWar = new TimeEventRequest.Types.CloseFieldWar { EventId = 554 } });
        Assert.That(session.FieldWarUid, Is.EqualTo(555));
        Send(new TimeEventRequest { CloseFieldWar = new TimeEventRequest.Types.CloseFieldWar { EventId = 555 } });
        Assert.That(session.FieldWarUid, Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void AnnounceFieldWar_BelowTheLevel_NothingIsSent() {
        Start(level: 69);
        Send(new TimeEventRequest { AnnounceFieldWar = new TimeEventRequest.Types.AnnounceFieldWar { FieldWarId = 10000001, EventId = 555 } });
        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.FieldWarUid, Is.EqualTo(0));
    }

    [Test]
    public void GetField_TheServedFieldAndItsPlayers() {
        Start();
        TimeEventResponse response = Send(new TimeEventRequest { GetField = new TimeEventRequest.Types.GetField { MapId = MapId } });

        Assert.That(response.Field, Is.Not.Null);
        Assert.That(response.Field.MapId, Is.EqualTo(MapId));
        Assert.That(response.Field.RoomId, Is.EqualTo(session.Field!.RoomId));
        Assert.That(response.Field.OwnerId, Is.EqualTo(0));
        Assert.That(response.Field.PlayerIds, Is.EqualTo(new[] { session.CharacterId }));
    }

    [Test]
    public void EmptyRequest_EmptyResponse() {
        Start();
        TimeEventResponse response = Send(new TimeEventRequest());
        Assert.That(response.Field, Is.Null);
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
