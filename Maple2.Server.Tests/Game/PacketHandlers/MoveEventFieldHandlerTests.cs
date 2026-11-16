using System;
using System.Collections.Generic;
using System.Linq;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestMoveEventField (UIWorldMapDialog::MoveByPortal, uiworldmapdialog.cpp:1510: the field id of a time event
// portal picked on the world map), on the KR world boss row 1000: <event id="1000" targetFields="02000084"
// targetSpawnPointIDs="100" npcIDs="23000013" ... />. A field of a boss the World answers alive on a channel is
// entered; any other field answers 0 (s_err_timeevent_move_field), a field of another continent group 1.
public class MoveEventFieldHandlerTests {
    private const int From = 2000062;
    private const int BossField = 2000084;
    private const int BossId = 1000;
    private TestGameSession test = null!;

    private static WorldBossMetadata Boss() => new(BossId, 100, new DateTime(2014, 1, 1, 0, 5, 0), new DateTime(2099, 12, 31, 12, 0, 0),
        TimeSpan.FromHours(1), TimeSpan.Zero, TimeSpan.FromMinutes(5), [BossField], [100], [23000013], 1000, true, false, 1, false, false);

    private static MapMetadata Field(int id, Continent continent = Continent.VictoriaIsland) {
        MapMetadata map = TestGameSession.Map(id);
        return map with {
            Property = map.Property with { Continent = continent },
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], false, false, false, false, false, false, false),
        };
    }

    private void Start(params MapMetadata[] maps) {
        test = new TestGameSession();
        test.SetServerTable(new TimeEventTable(new Dictionary<int, GlobalPortalMetadata>(), new Dictionary<int, WorldBossMetadata> { [BossId] = Boss() }));
        test.Start();
        MapMetadata from = Field(From);
        test.AddMap([from, .. maps]);
        test.EnterMovingOut(from);
        test.TakeSent();
    }

    private void Alive(params int[] channels) {
        var boss = new TimeEventResponse.Types.ActiveWorldBoss { MetadataId = BossId };
        boss.AliveChannels.AddRange(channels);
        test.World.ActiveWorldBosses.Add(boss);
    }

    private List<string> Move(int fieldId) {
        new MoveEventFieldHandler().Handle(test.Session, TestGameSession.Packet(writer => writer.WriteInt(fieldId)));
        return test.TakeSent().Select(Convert.ToHexString).ToList();
    }

    private static string Refuse(byte reason) => Convert.ToHexString(MoveEventFieldPacket.Refuse(reason).ToArray());

    [Test]
    public void AliveBossField_IsEntered() {
        Start(Field(BossField));
        Alive(1);

        Move(BossField);

        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
        Assert.That(test.World.Moves.Single().MapId, Is.EqualTo(BossField));
    }

    // A boss the World lists with no channel alive opens no portal.
    [Test]
    public void BossWithNoChannelAlive_AnswersNoEvent() {
        Start(Field(BossField));
        Alive();

        Assert.That(Move(BossField), Is.EqualTo(new[] { Refuse(MoveEventFieldPacket.NO_EVENT) }));
        Assert.That(test.World.Moves, Is.Empty);
    }

    // A field the map data does not know answers 0 before the World is asked.
    [Test]
    public void UnknownField_AnswersNoEvent_WithoutAWorldCall() {
        Start();
        Alive(1);

        Assert.That(Move(BossField), Is.EqualTo(new[] { Refuse(MoveEventFieldPacket.NO_EVENT) }));
        Assert.That(test.World.TimeEventDeadlines, Is.Empty);
    }

    // Victoria to Kritias is outside the group (the default WarpOpenContinent list).
    [Test]
    public void OtherContinent_AnswersOne() {
        Start(Field(BossField, Continent.Kritias));
        Alive(1);

        Assert.That(Move(BossField), Is.EqualTo(new[] { Refuse(MoveEventFieldPacket.OTHER_CONTINENT) }));
        Assert.That(test.World.Moves, Is.Empty);
        Assert.That(test.World.TimeEventDeadlines, Is.Empty);
    }

    // A request the client does not send (its own field, none) is not answered.
    [TestCase(From)]
    [TestCase(0)]
    public void FieldTheClientRefuses_IsNotAnswered(int fieldId) {
        Start(Field(BossField));
        Alive(1);

        Assert.That(Move(fieldId), Is.Empty);
        Assert.That(test.World.TimeEventDeadlines, Is.Empty);
    }

    // In battle the client refuses the move itself (notice 0xa2) and sends nothing; a request anyway is not answered.
    [Test]
    public void InBattle_IsNotAnswered() {
        Start(Field(BossField));
        Alive(1);
        test.Session.Player.InBattle = true;
        test.TakeSent();

        Assert.That(Move(BossField), Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    // The World's answer is kept for a few seconds: a repeated request makes no second call.
    [Test]
    public void RepeatedRequest_UsesTheKeptAnswer() {
        Start(Field(BossField), Field(2000085));
        Alive(1);

        Assert.That(Move(2000085), Is.EqualTo(new[] { Refuse(MoveEventFieldPacket.NO_EVENT) }));
        Move(BossField);

        Assert.That(test.World.TimeEventDeadlines, Has.Count.EqualTo(1));
        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
    }

    // A World that does not answer: no event, and the failure is not kept (the next request asks again).
    [Test]
    public void WorldDown_AnswersNoEvent_AndAsksAgain() {
        Start(Field(BossField));
        Alive(1);
        test.World.TimeEventFailure = new RpcException(new Status(StatusCode.Unavailable, "no world"));

        Assert.That(Move(BossField), Is.EqualTo(new[] { Refuse(MoveEventFieldPacket.NO_EVENT) }));

        test.World.TimeEventFailure = null;
        Move(BossField);

        Assert.That(test.World.TimeEventDeadlines, Has.Count.EqualTo(2));
        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
    }
}
