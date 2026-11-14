using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The quest conditions of an ensemble (PlayInstrument 5 JoinEnsemble, 4 StopScore) of a party of two, each with an
// instrument and a score, with the KR quest rows' conditions as the ingest reads them (ConvertCodes). The second
// member gets ready first; the leader's request starts both. Each test fails when its report is taken out.
public class EnsembleConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int InstrumentItemId = 34000003;
    private const int ScoreItemId = 35000033;
    private const int InstrumentId = 1;
    private const int MapId = 2000001;
    private const int QuestId = 91000001;

    private TestGameSession leaderTest = null!;
    private TestGameSession guestTest = null!;
    private GameSession leader => leaderTest.Session;
    private GameSession guest => guestTest.Session;

    private static ConditionMetadata Condition(ConditionType type, long value, string[] code, int partyCount = 0, int guildPartyCount = 0) =>
        new(type, value, code.ConvertCodes(), new string[0].ConvertCodes(), partyCount, guildPartyCount);

    private static (long Instrument, long Score) Items(TestGameSession test) {
        GameSession session = test.Session;
        Item instrument = session.Item.Inventory.Filter(item => item.Id == InstrumentItemId, InventoryType.FishingMusic).Single();
        Item score = session.Item.Inventory.Filter(item => item.Id == ScoreItemId, InventoryType.FishingMusic).Single();
        instrument.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        score.RemainUses = 5;
        return (instrument.Uid, score.Uid);
    }

    // Two sessions on one character table and one server, in a party of the two (both of the given guild), with the
    // conditions started on each.
    private (Quest[] Leader, Quest[] Guest) Party(long guildId, params ConditionMetadata[] conditions) {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        leaderTest = new TestGameSession(options);
        guestTest = new TestGameSession(options);
        foreach (TestGameSession test in new[] { leaderTest, guestTest }) {
            test.Store(TestGameSession.Metadata(InstrumentItemId, type: 6,
                function: new ItemMetadataFunction(ItemFunction.OpenInstrument, "OpenInstrument", $"{InstrumentId}", false)));
            // A ready-made score of 60 s (every KR one has a playTime; a score without one counts no time).
            test.Store(TestGameSession.Metadata(ScoreItemId, type: 6) with {
                Music = new ItemMetadataMusic(10, 1, 1, false, 0, string.Empty, 60000),
            });
            test.SetTable(new InstrumentTable(new Dictionary<int, InstrumentMetadata> {
                [InstrumentId] = new(InstrumentId, 0, 1, 1, 1, 0),
            }));
            test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> { [ExpType.musicMastery1] = new(1, 1f) }));
            test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
                [1] = Enumerable.Range(1, 99).ToDictionary(level => level, level => level * 10L),
            }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
            test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
            test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
            test.Start();
            test.EnterMap(TestGameSession.Map(MapId));
        }

        // The leader's server finds the guest's session.
        var sessions = new ConcurrentDictionary<long, GameSession> { [leader.CharacterId] = leader, [guest.CharacterId] = guest };
        object server = typeof(GameSession).GetField("server", Any)!.GetValue(leader)!;
        server.GetType().GetField("sessions", Any)!.SetValue(server, sessions);

        leader.Player.Value.Character.GuildId = guildId;
        guest.Player.Value.Character.GuildId = guildId;
        PartyMember Member(GameSession session) => new() {
            PartyId = 5,
            Info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, session.PlayerName, "", "", Gender.Male, Job.Newbie, 20),
                "", new AchievementInfo(), []) { GuildId = guildId },
        };
        var party = new Party(5, Member(leader));
        party.Members[leader.CharacterId] = Member(leader);
        party.Members[guest.CharacterId] = Member(guest);
        var quests = new List<Quest[]>();
        foreach (TestGameSession test in new[] { leaderTest, guestTest }) {
            test.Session.Party = new PartyManager(test.World, test.Session);
            typeof(PartyManager).GetField("<Party>k__BackingField", Any)!.SetValue(test.Session.Party, party);
            quests.Add(conditions.Select((condition, i) =>
                test.StartQuest(TestGameSession.QuestData(QuestId + i) with { Conditions = [condition] })).ToArray());
            test.TakeSent();
        }
        return (quests[0], quests[1]);
    }

    private void Send(TestGameSession test, byte command, params long[] values) {
        new InstrumentHandler { TableMetadata = test.TableMetadata, BanWordStorage = null! }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            foreach (long value in values) {
                writer.WriteLong(value);
            }
        }));
    }

    private void Ensemble() {
        (long guestInstrument, long guestScore) = Items(guestTest);
        (long leaderInstrument, long leaderScore) = Items(leaderTest);
        Send(guestTest, 5, guestInstrument, guestScore);
        Send(leaderTest, 5, leaderInstrument, leaderScore);
        Assume.That(leader.Instrument?.Ensemble, Is.True, "the leader plays");
        Assume.That(guest.Instrument?.Ensemble, Is.True, "the guest plays");
    }

    // KR 60001140: music_play_ensemble, value 1, counts each member the leader's request starts; 73000018 asks for
    // three of the guild (guildPartyCount 3) and a party of two counts nothing.
    [Test]
    public void MusicPlayEnsemble_60001140_EachMember() {
        (Quest[] leaderQuests, Quest[] guestQuests) = Party(0,
            Condition(ConditionType.music_play_ensemble, 1, []),
            Condition(ConditionType.music_play_ensemble, 1, [], guildPartyCount: 3));

        Ensemble();

        Assert.That((leaderQuests[0].Conditions[0].Counter, guestQuests[0].Conditions[0].Counter), Is.EqualTo((1, 1)));
        Assert.That((leaderQuests[1].Conditions[0].Counter, guestQuests[1].Conditions[0].Counter), Is.EqualTo((0, 0)));
    }

    // KR 50000109: play_ensenble_time code 2000001 (the map), value 300, partyCount 2, counts the seconds an ensemble
    // played on that map at its stop, for a party of two of one guild (guildPartyCount 2); 50000108 names 84000001.
    [Test]
    public void PlayEnsembleTime_50000109_TheSecondsOnTheMap() {
        (Quest[] leaderQuests, Quest[] guestQuests) = Party(7,
            Condition(ConditionType.play_ensenble_time, 300, ["2000001"], 2, 2),
            Condition(ConditionType.play_ensenble_time, 300, ["84000001"], 2, 2));

        Ensemble();
        foreach (TestGameSession test in new[] { leaderTest, guestTest }) {
            test.Session.Instrument!.StartTick -= 10_500;
            Send(test, 4);
            Assume.That(test.Session.Instrument, Is.Null, "stopped");
        }

        Assert.That((leaderQuests[0].Conditions[0].Counter, guestQuests[0].Conditions[0].Counter), Is.EqualTo((10, 10)));
        Assert.That((leaderQuests[1].Conditions[0].Counter, guestQuests[1].Conditions[0].Counter), Is.EqualTo((0, 0)));
    }

    // A leader whose party has no other ready member plays the score alone: the start is a solo play (StartScore's
    // ensemble flag off, the client's solo state 0x7f), so it counts the score's music_play_score and no ensemble
    // condition, and its stop no play_ensenble_time. Before, the leader alone counted an ensemble.
    [Test]
    public void LeaderAlone_PlaysSolo() {
        (Quest[] leaderQuests, _) = Party(7,
            Condition(ConditionType.music_play_ensemble, 1, []),
            Condition(ConditionType.music_play_score, 1, []),
            Condition(ConditionType.play_ensenble_time, 300, ["2000001"]));

        Items(guestTest);
        (long leaderInstrument, long leaderScore) = Items(leaderTest);
        Send(leaderTest, 5, leaderInstrument, leaderScore);

        Assert.That(leader.Instrument?.Ensemble, Is.False);
        Assert.That(guest.Instrument, Is.Null);
        Item score = leader.Item.Inventory.Get(leaderScore, InventoryType.FishingMusic)!;
        byte[] start = Maple2.Server.Game.Packets.InstrumentPacket.StartScore(leader.Instrument!, score).ToArray();
        Assert.That(leaderTest.TakeSent().Any(packet => packet.SequenceEqual(start)), Is.True, "the solo start is sent");
        leader.Instrument!.StartTick -= 10_500;
        Send(leaderTest, 4);
        Assert.That(leaderQuests.Select(quest => quest.Conditions[0].Counter), Is.EqualTo(new[] { 0, 1, 0 }));
    }

    // A leader whose own instrument does not start (here it has expired) is not left in the client's ensemble
    // wait: it gets Instrument 6 (cancelled) and is no longer ready; the ready guest still starts, alone, so solo.
    [Test]
    public void LeaderWhoseInstrumentFails_GetsTheCancel() {
        Party(0);
        (long guestInstrument, long guestScore) = Items(guestTest);
        (long leaderInstrument, long leaderScore) = Items(leaderTest);
        leader.Item.Inventory.Get(leaderInstrument, InventoryType.FishingMusic)!.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;
        Send(guestTest, 5, guestInstrument, guestScore);
        guestTest.TakeSent();
        Send(leaderTest, 5, leaderInstrument, leaderScore);

        byte[] cancel = Maple2.Server.Game.Packets.InstrumentPacket.LeaveEnsemble().ToArray();
        Assert.That(leader.Instrument, Is.Null);
        Assert.That(leaderTest.TakeSent().Count(packet => packet.SequenceEqual(cancel)), Is.EqualTo(1));
        Assert.That(leader.EnsembleReady, Is.False);
        Assert.That(guest.Instrument?.Ensemble, Is.False, "the guest plays alone");
        Assert.That(guestTest.TakeSent().Any(packet => packet.SequenceEqual(cancel)), Is.False);
    }

    // A refused request (here a score with no use left) cancels the wait the client entered as it sent 5.
    [Test]
    public void RefusedRequest_GetsTheCancel() {
        Party(0);
        (long guestInstrument, long guestScore) = Items(guestTest);
        guest.Item.Inventory.Get(guestScore, InventoryType.FishingMusic)!.RemainUses = 0;
        Send(guestTest, 5, guestInstrument, guestScore);

        byte[] cancel = Maple2.Server.Game.Packets.InstrumentPacket.LeaveEnsemble().ToArray();
        Assert.That(guestTest.TakeSent().Count(packet => packet.SequenceEqual(cancel)), Is.EqualTo(1));
        Assert.That(guest.EnsembleReady, Is.False);
    }

    // A ready member whom the leader's start skips (here the staged score was used up after the member got ready, or
    // the member is in another room of the map) gets the cancel and is no longer ready; the leader plays alone. Before,
    // the member got nothing and stayed in the client's wait.
    [TestCase(false)]
    [TestCase(true)]
    public void SkippedReadyMember_GetsTheCancel(bool otherRoom) {
        Party(0);
        (long guestInstrument, long guestScore) = Items(guestTest);
        (long leaderInstrument, long leaderScore) = Items(leaderTest);
        Send(guestTest, 5, guestInstrument, guestScore);
        Assume.That(guest.EnsembleReady, Is.True);
        guestTest.TakeSent();
        if (otherRoom) {
            guest.Player.Value.Character.RoomId = leader.Player.Value.Character.RoomId + 1;
        } else {
            guest.Item.Inventory.Get(guestScore, InventoryType.FishingMusic)!.RemainUses = 0;
        }

        Send(leaderTest, 5, leaderInstrument, leaderScore);

        byte[] cancel = Maple2.Server.Game.Packets.InstrumentPacket.LeaveEnsemble().ToArray();
        Assert.That(guest.Instrument, Is.Null);
        Assert.That(guestTest.TakeSent().Count(packet => packet.SequenceEqual(cancel)), Is.EqualTo(1));
        Assert.That(guest.EnsembleReady, Is.False);
        Assert.That(guest.StagedScoreItem, Is.Null);
        Assert.That(leader.Instrument?.Ensemble, Is.False, "the leader plays alone");
        Assert.That(leaderTest.TakeSent().Any(packet => packet.SequenceEqual(cancel)), Is.False);
    }

    // Two ready members are an ensemble: both starts carry the flag and count the ensemble, not the solo score.
    [Test]
    public void TwoReady_PlayAnEnsemble() {
        (Quest[] leaderQuests, Quest[] guestQuests) = Party(0,
            Condition(ConditionType.music_play_ensemble, 1, []),
            Condition(ConditionType.music_play_score, 1, []));

        Ensemble();

        Assert.That(leaderQuests.Select(quest => quest.Conditions[0].Counter), Is.EqualTo(new[] { 1, 0 }));
        Assert.That(guestQuests.Select(quest => quest.Conditions[0].Counter), Is.EqualTo(new[] { 1, 0 }));
    }
}
