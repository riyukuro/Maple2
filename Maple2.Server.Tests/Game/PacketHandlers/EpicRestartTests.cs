using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Quest 17 (the epic restart of event type EpicRestart, uigamemenudialog.cpp FUN_14193c000): once per event the
// character's epic quests are dropped, except those of the event's table, the chapter points are taken back and the
// character is sent to the event's map. Made-up quest, event and map ids.
public class EpicRestartTests {
    private const int EventId = 320002;
    private const int Kept = 60001000;
    private const int InRange = 70000005;
    private const int Dropped = 10000001;
    private const int World = 10000002;

    private TestGameSession test = null!;

    private static EpicRestart Restart(int barred = 52000065) {
        return new EpicRestart("exceptEpicRestart_2.xml", "2000062,13", barred.ToString(), 2000062, 13, [barred], [Kept],
            [new EpicRestart.Range(70000001, 79999999)]);
    }

    private GameSession Login(EpicRestart? restart = null) {
        test = new TestGameSession();
        test.AddEvent(EventId, GameEventType.EpicRestart, restart ?? Restart());
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        // Chapter 1 ends with a dropped quest: done again later, so its points go back. Chapter 2 ends with a kept
        // quest: it stays done and keeps its points.
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry> {
            [1] = new(1, Kept, Dropped, [new ChapterBookTable.Entry.SkillPoint(2, 0)], 3, []),
            [2] = new(2, Dropped, Kept, [new ChapterBookTable.Entry.SkillPoint(1, 1)], 4, []),
        }));
        GameSession session = test.Start();
        // The event's map is one the played client does not have, so the move stops in PrepareField.
        test.AddMap(TestGameSession.Map(2000062, notInPlayedClient: true) with {
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], false, false, false, false, false, false, false),
        });
        ProgressionOrderTests.LoadConfig(test, session);
        typeof(ConfigManager).GetField("skillPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, new SkillPoint());
        foreach (int id in new[] { Kept, InRange, Dropped }) {
            test.StartQuest(TestGameSession.QuestData(id)).State = QuestState.Completed;
        }
        test.StartQuest(TestGameSession.QuestData(World, QuestType.WorldQuest));
        session.Config.AddSkillPoint(SkillPointSource.Chapter, 2);
        session.Config.AddSkillPoint(SkillPointSource.Chapter, 1, 1);
        session.Config.AddStatPoint(AttributePointSource.Quest, 7);
        test.TakeSent();
        return session;
    }

    private void Request(GameSession session) {
        var handler = new QuestHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata };
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(17)));
    }

    private static int[] Quests(GameSession session) {
        return new[] { Kept, InRange, Dropped, World }.Where(id => session.Quest.TryGetQuest(id, out _)).ToArray();
    }

    [Test]
    public void Restart_DropsTheEpicQuestsAndTheChapterPoints() {
        GameSession session = Login();
        Request(session);

        Assert.That(Quests(session), Is.EqualTo(new[] { Kept, InRange, World }));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Any(packet => TestGameSession.Op(packet) == SendOp.Quest && packet[2] == 5
            && BitConverter.ToInt32(packet, 3) == Dropped), Is.True);
        Assert.That(sent.Select(TestGameSession.Op), Does.Contain(SendOp.RequestFieldEnter));
        Assert.That(session.Config.ExtraSkillPoints(), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(session.Config.StatPoints(AttributePointSource.Quest), Is.EqualTo(4));
        Assert.That(test.StoredEventValues(EventId)[GameEventUserValueType.EpicRestartDone], Is.EqualTo("1"));
    }

    [Test]
    public void SecondRequest_DoesNothing() {
        GameSession session = Login();
        Request(session);
        session.Config.AddSkillPoint(SkillPointSource.Chapter, 2);
        test.StartQuest(TestGameSession.QuestData(Dropped));
        test.TakeSent();

        Request(session);
        Assert.That(Quests(session), Does.Contain(Dropped));
        Assert.That(session.Config.ExtraSkillPoints()[0], Is.EqualTo(2));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void BarredMap_DoesNothing() {
        var probe = new TestGameSession();
        int mapId = probe.Start().Field!.MapId;
        GameSession session = Login(Restart(barred: mapId));
        Request(session);

        Assert.That(Quests(session), Is.EqualTo(new[] { Kept, InRange, Dropped, World }));
        Assert.That(session.Config.StatPoints(AttributePointSource.Quest), Is.EqualTo(7));
        Assert.That(test.StoredEventValues(EventId), Does.Not.ContainKey(GameEventUserValueType.EpicRestartDone));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Record_KeepsTheTableQuests() {
        EpicRestart restart = Restart();
        Assert.That(restart.Kept(Kept) && restart.Kept(70000001) && restart.Kept(79999999), Is.True);
        Assert.That(restart.Kept(Dropped) || restart.Kept(80000000), Is.False);
        Assert.That(EpicRestart.Done(null) || EpicRestart.Done("") || EpicRestart.Done("0"), Is.False);
        Assert.That(EpicRestart.Done("1"), Is.True);
    }

    [Test]
    public void Ingest_ReadsTheExceptTable() {
        var document = new XmlDocument();
        document.LoadXml("<ms2><quest questID=\"70000001-79999999\" /><quest questID=\"60001000\" /></ms2>");
        EpicRestart? restart = ServerTableMapper.MapEpicRestart("exceptEpicRestart_2.xml", "2000062,13", "52000065,52000066", document);

        Assert.That(restart, Is.Not.Null);
        Assert.That((restart!.MapId, restart.PortalId), Is.EqualTo((2000062, 13)));
        Assert.That(restart.BarredMapIds, Is.EqualTo(new[] { 52000065, 52000066 }));
        Assert.That(restart.ExceptQuestIds, Is.EqualTo(new[] { 60001000 }));
        Assert.That(restart.ExceptRanges, Is.EqualTo(new[] { new EpicRestart.Range(70000001, 79999999) }));
        Assert.That(ServerTableMapper.MapEpicRestart("x.xml", "", "", document), Is.Null);
    }

    // GameEventEpicRestart::vfunc_4 reads the id, the end time and the three values as wide strings (input stream
    // slot 6, ReadUnicodeStringA: decoded wide, then narrowed).
    [Test]
    public void Packet_IdEndTimeAndValues() {
        var writer = new ByteWriter();
        var gameEvent = new GameEvent(new GameEventMetadata(EventId, GameEventType.EpicRestart, DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddDays(1), TimeSpan.Zero, TimeSpan.Zero, [], Restart(), "", "", "", ""));
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("EpicRestart"));
        Assert.That(reader.ReadInt(), Is.EqualTo(EventId));
        Assert.That(reader.ReadLong(), Is.EqualTo(gameEvent.EndTime));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("exceptEpicRestart_2.xml"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("2000062,13"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("52000065"));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // The live row (event 320002, 49 barred fields) in the login Load, walked as the client's GameEventManagerCli
    // Packet0 reads it: the count, then per event the wide type name and the event's own read. With the values written
    // narrow, the client read the table name wide and then a length from the field list's digits, failing the whole Load
    // (DecodeStr at client offset 1174).
    [Test]
    public void Load_OfTheLiveRow_IsReadToTheEnd() {
        const string barred = "52000065,52000100,52000101,52000102,52000104,52000105,52000106,52000107,52000108," +
            "52000109,52000111,52000112,52000113,52000128,52000129,52000130,52000131,52000139,52000140,52000141,52000142," +
            "52000143,52000144,52000145,52000146,52000147,52000148,52000015,52000016,63000006,63000011,63000012,63000013," +
            "63000015,63000016,63000017,63000018,63000019,63000020,63000021,63000022,63000023,63000024,63000025,63000026," +
            "63000027,63000028,63000029,63000030";
        Assert.That(barred, Has.Length.EqualTo(440));
        int[] barredIds = barred.Split(',').Select(int.Parse).ToArray();
        var restart = new EpicRestart("exceptEpicRestart_2.xml", "2000062,13", barred, 2000062, 13, barredIds, [Kept], []);
        DateTime end = DateTimeOffset.FromUnixTimeSeconds(1796475600).UtcDateTime;
        var gameEvent = new GameEvent(new GameEventMetadata(EventId, GameEventType.EpicRestart, DateTime.UnixEpoch, end,
            TimeSpan.Zero, TimeSpan.Zero, [], restart, "", "", "", ""));

        byte[] packet = Maple2.Server.Core.Packets.GameEventPacket.Load(gameEvent).ToArray();

        var reader = new ByteReader(packet);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.GameEvent));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("EpicRestart"));
        Assert.That(reader.ReadInt(), Is.EqualTo(EventId));
        Assert.That(reader.ReadLong(), Is.EqualTo(1796475600));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("exceptEpicRestart_2.xml"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("2000062,13"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo(barred));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
