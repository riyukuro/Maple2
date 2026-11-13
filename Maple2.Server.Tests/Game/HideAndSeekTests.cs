using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maple2.Server.Tests.Game;

// The Hide and Seek game's rules (61000023_main's steps and constants) and its HideAndSeek 0x12B packets.
public class HideAndSeekTests {
    private sealed class ListSink : ILogEventSink {
        public readonly List<LogEvent> Events = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    // 61000023_main's times.
    private static HideAndSeekState Game(long tick = 0, ILogger? logger = null) {
        var state = new HideAndSeekState(tick, logger);
        (string, string)[] constants = [
            ("RequireUserCount", "2"), ("EnterableSeconds", "60"), ("GameRuleNoticeTick", "10000"), ("ShortOfUserTick", "10000"),
            ("DivideTeamsSeconds", "5"), ("MoveGameAreaTick", "2000"), ("BeInHidingTeamsSeconds", "30"),
            ("LookingForATeamsSeconds", "150"), ("TeamMatchResultTick", "15000"), ("GameExitNoticeTick", "10000"),
            ("PortalGameAreaID", "10"), ("SeekTeamSkillSetID", "99930041"), ("SeekTeamInvincibleTick", "20000"),
        ];
        foreach ((string key, string value) in constants) {
            Assert.That(state.SetConstant(key, value, "ALL", "KR"), Is.True, key);
        }
        return state;
    }

    [Test]
    public void Steps_FollowTheScriptTimes() {
        HideAndSeekState state = Game();
        state.Split([1, 2, 3], new Random(1));
        var seen = new List<(long, HideAndSeekStep)>();
        for (long tick = 0; tick <= 300_000; tick += 100) {
            HideAndSeekStep? next = state.Advance(tick, 3);
            if (next != null) {
                seen.Add((tick, next.Value));
            }
        }
        Assert.That(seen, Is.EqualTo(new List<(long, HideAndSeekStep)> {
            (60_000, HideAndSeekStep.GameRuleNotice),
            (70_000, HideAndSeekStep.DivideIntoTeams),
            (75_000, HideAndSeekStep.MoveGameArea),
            (77_000, HideAndSeekStep.BeInHidingTeams),
            (107_000, HideAndSeekStep.LookingForATeams),
            (257_000, HideAndSeekStep.TeamMatchResult),
            (272_000, HideAndSeekStep.GameExitNotice),
            (282_000, HideAndSeekStep.End),
        }));
        Assert.That(state.Winner, Is.EqualTo(HideAndSeekState.HideTeam));
    }

    [Test]
    public void TooFewPlayers_ShortOfUserThenEnd() {
        HideAndSeekState state = Game();
        Assert.That(state.Advance(59_999, 1), Is.Null);
        Assert.That(state.Advance(60_000, 1), Is.EqualTo(HideAndSeekStep.ShortOfUser));
        Assert.That(state.Advance(69_999, 1), Is.Null);
        Assert.That(state.Advance(70_000, 1), Is.EqualTo(HideAndSeekStep.End));
        Assert.That(state.Advance(1_000_000, 1), Is.Null);
    }

    [Test]
    public void PlayersLeftWhileDividing_ShortOfUser() {
        HideAndSeekState state = Game();
        state.Advance(60_000, 2);
        state.Advance(70_000, 2);
        Assert.That(state.Advance(75_000, 1), Is.EqualTo(HideAndSeekStep.ShortOfUser));
    }

    [TestCase(1, 1)]
    [TestCase(2, 1)]
    [TestCase(3, 1)]
    [TestCase(5, 1)]
    [TestCase(6, 2)]
    [TestCase(10, 3)]
    [TestCase(30, 10)]
    public void SeekerCount_AThirdAtLeastOne(int players, int seekers) {
        Assert.That(HideAndSeekState.SeekerCount(players), Is.EqualTo(seekers));
    }

    [Test]
    public void Split_EveryPlayerHasATeam() {
        HideAndSeekState state = Game();
        state.Split(Enumerable.Range(1, 9).Select(id => (long) id), new Random(7));
        Assert.Multiple(() => {
            Assert.That(state.Teams, Has.Count.EqualTo(9));
            Assert.That(state.Teams.Values.Count(team => team == HideAndSeekState.SeekTeam), Is.EqualTo(3));
            Assert.That(state.Teams.Values.Count(team => team == HideAndSeekState.HideTeam), Is.EqualTo(6));
            Assert.That(state.TeamOf(100), Is.EqualTo(0));
        });
    }

    private static HideAndSeekState Hiding() {
        HideAndSeekState state = Game();
        state.Split([1, 2, 3], new Random(3));
        state.Advance(60_000, 3);
        state.Advance(70_000, 3);
        state.Advance(75_000, 3);
        state.Advance(77_000, 3);
        Assert.That(state.Step, Is.EqualTo(HideAndSeekStep.BeInHidingTeams));
        return state;
    }

    [Test]
    public void AllHidersLeave_SeekersWin() {
        HideAndSeekState state = Hiding();
        long[] hiders = state.Teams.Where(entry => entry.Value == HideAndSeekState.HideTeam).Select(entry => entry.Key).ToArray();
        Assert.That(state.Remove(hiders[0], 80_000), Is.Null);
        Assert.That(state.Remove(hiders[1], 81_000), Is.EqualTo(HideAndSeekStep.TeamMatchResult));
        Assert.That(state.Winner, Is.EqualTo(HideAndSeekState.SeekTeam));
        Assert.That(state.Advance(95_999, 1), Is.Null);
        Assert.That(state.Advance(96_000, 1), Is.EqualTo(HideAndSeekStep.GameExitNotice));
    }

    [Test]
    public void SeekerLeaves_HidersWin() {
        HideAndSeekState state = Hiding();
        long seeker = state.Teams.Single(entry => entry.Value == HideAndSeekState.SeekTeam).Key;
        Assert.That(state.Remove(seeker, 80_000), Is.EqualTo(HideAndSeekStep.TeamMatchResult));
        Assert.That(state.Winner, Is.EqualTo(HideAndSeekState.HideTeam));
        Assert.That(state.Remove(12345, 80_000), Is.Null);
    }

    [TestCase("ALL", "KR", true)]
    [TestCase("KR", "KR", true)]
    [TestCase("NA", "KR", false)]
    [TestCase("NA", "NA", true)]
    [TestCase("CN", "NA", false)]
    public void Constant_OnlyThisRegion(string locale, string serverLocale, bool kept) {
        var state = new HideAndSeekState(0);
        Assert.That(state.SetConstant("WinnerRewardItemID", "30001442", locale, serverLocale), Is.EqualTo(kept));
        Assert.That(state.Constant("WinnerRewardItemID"), Is.EqualTo(kept ? "30001442" : null));
    }

    [Test]
    public void UnknownConstant_IgnoredWithOneDebugLine() {
        var sink = new ListSink();
        ILogger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        HideAndSeekState state = Game(logger: logger);
        Assert.DoesNotThrow(() => Assert.That(state.SetConstant("SeekTeamUserCount", "3", "ALL", "KR"), Is.False));
        Assert.Multiple(() => {
            Assert.That(sink.Events, Has.Count.EqualTo(1));
            Assert.That(sink.Events[0].Level, Is.EqualTo(LogEventLevel.Debug));
            Assert.That(state.Constant("SeekTeamUserCount"), Is.Null);
            Assert.That(state.Duration(HideAndSeekStep.WaitForEnterUser), Is.EqualTo(60_000));
        });
    }

    [Test]
    public void MissingTime_MovesOnAtOnce() {
        var state = new HideAndSeekState(0);
        Assert.That(state.Advance(0, 2), Is.EqualTo(HideAndSeekStep.GameRuleNotice));
    }

    // The seekers look (107 s in): a seeker and two hiders turned into 5000-health objects.
    private static (HideAndSeekState State, long Seeker, long[] Hiders) Seeking() {
        HideAndSeekState state = Hiding();
        long seeker = state.Teams.Single(entry => entry.Value == HideAndSeekState.SeekTeam).Key;
        long[] hiders = state.Teams.Where(entry => entry.Value == HideAndSeekState.HideTeam).Select(entry => entry.Key).ToArray();
        state.Hide(hiders[0], 99920001, 3, 5000);
        state.Hide(hiders[1], 99920002, 0, 5000);
        state.Advance(107_000, 3);
        Assert.That(state.Step, Is.EqualTo(HideAndSeekStep.LookingForATeams));
        return (state, seeker, hiders);
    }

    [Test]
    public void Hit_TwoSeekHitsCatch_LastCatchSeekersWin() {
        (HideAndSeekState state, long seeker, long[] hiders) = Seeking();
        Assert.Multiple(() => {
            Assert.That(state.Hostile(seeker, hiders[0]), Is.True);
            Assert.That(state.Hostile(hiders[0], seeker), Is.False);
            Assert.That(state.Hostile(hiders[0], hiders[1]), Is.False);
            Assert.That(state.Hit(seeker, hiders[0], 2500, 108_000), Is.EqualTo(new HideAndSeekState.HitResult(2500, 5000, false)));
            Assert.That(state.Hit(seeker, hiders[0], 2500, 109_000), Is.EqualTo(new HideAndSeekState.HitResult(0, 5000, true)));
            Assert.That(state.IsCaught(hiders[0]), Is.True);
            Assert.That(state.Kills(seeker), Is.EqualTo(1));
            Assert.That(state.Hostile(seeker, hiders[0]), Is.False);
            Assert.That(state.Hit(seeker, hiders[0], 2500, 110_000), Is.Null);
            Assert.That(state.Watching(hiders[0]), Is.True);
            Assert.That(state.Watching(hiders[1]), Is.False);
            Assert.That(state.Step, Is.EqualTo(HideAndSeekStep.LookingForATeams));
        });
        state.Hit(seeker, hiders[1], 5000, 111_000);
        Assert.Multiple(() => {
            Assert.That(state.Step, Is.EqualTo(HideAndSeekStep.TeamMatchResult));
            Assert.That(state.Winner, Is.EqualTo(HideAndSeekState.SeekTeam));
            Assert.That(state.Kills(seeker), Is.EqualTo(2));
            Assert.That(state.Watching(hiders[0]), Is.False);
        });
    }

    [Test]
    public void Hit_OnlyWhileSeeking() {
        HideAndSeekState state = Hiding();
        long seeker = state.Teams.Single(entry => entry.Value == HideAndSeekState.SeekTeam).Key;
        long hider = state.Teams.First(entry => entry.Value == HideAndSeekState.HideTeam).Key;
        state.Hide(hider, 99920001, 0, 5000);
        Assert.That(state.Hostile(seeker, hider), Is.False);
        Assert.That(state.Hit(seeker, hider, 2500, 80_000), Is.Null);
    }

    // The seekers are invincible for SeekTeamInvincibleTick; after it each miss costs a life, the last one ends the
    // seeking and the hiders win.
    [Test]
    public void Miss_AfterInvincibleCostsLives_LastLifeHidersWin() {
        (HideAndSeekState state, long seeker, long[] hiders) = Seeking();
        Assert.Multiple(() => {
            Assert.That(state.Lives(seeker), Is.EqualTo(HideAndSeekState.SeekerLives));
            Assert.That(state.Miss(seeker, 126_999), Is.Null);
            Assert.That(state.Miss(hiders[0], 130_000), Is.Null);
            Assert.That(state.Miss(seeker, 127_000), Is.EqualTo(2));
            Assert.That(state.Miss(seeker, 128_000), Is.EqualTo(1));
            Assert.That(state.Step, Is.EqualTo(HideAndSeekStep.LookingForATeams));
            Assert.That(state.Miss(seeker, 129_000), Is.EqualTo(0));
            Assert.That(state.Step, Is.EqualTo(HideAndSeekStep.TeamMatchResult));
            Assert.That(state.Winner, Is.EqualTo(HideAndSeekState.HideTeam));
            Assert.That(state.Miss(seeker, 130_000), Is.Null);
        });
    }

    [Test]
    public void Watching_NewcomersWhileTheGameRuns() {
        HideAndSeekState state = Game();
        Assert.That(state.Watching(9), Is.False);
        (HideAndSeekState seeking, long seeker, _) = Seeking();
        Assert.Multiple(() => {
            Assert.That(seeking.Watching(9), Is.True);
            Assert.That(seeking.Watching(seeker), Is.False);
        });
    }

    // A free prop of the ride's model first; once all are hidden, a ride without a prop.
    [Test]
    public void PickRide_FreePropFirst() {
        HideAndSeekState state = Game();
        var field = new HideAndSeekTable.Field([99920001, 99920002], new Dictionary<int, IReadOnlyList<int>> { [99920001] = [4, 5] });
        var random = new Random(2);
        (int ride1, int prop1) = state.PickRide(field, random);
        state.Hide(1, ride1, prop1, 5000);
        (int ride2, int prop2) = state.PickRide(field, random);
        state.Hide(2, ride2, prop2, 5000);
        (int ride3, int prop3) = state.PickRide(field, random);
        Assert.Multiple(() => {
            Assert.That(new[] { prop1, prop2 }, Is.EquivalentTo(new[] { 4, 5 }));
            Assert.That(ride1, Is.EqualTo(99920001));
            Assert.That(ride2, Is.EqualTo(99920001));
            Assert.That(prop3, Is.EqualTo(0));
            Assert.That(new[] { 99920001, 99920002 }, Does.Contain(ride3));
            Assert.That(state.HiddenProps, Is.EquivalentTo(new[] { 4, 5 }));
        });
        Assert.That(state.PickRide(new HideAndSeekTable.Field([], new Dictionary<int, IReadOnlyList<int>>()), random), Is.EqualTo((0, 0)));
    }

    [Test]
    public void HidePropsPacket() {
        ByteWriter packet = HideAndSeekPacket.HideProps(new byte[] { 3, 200 });
        var reader = new ByteReader(packet.ToArray());
        Assert.Multiple(() => {
            Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.HideAndSeek));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadByte(), Is.EqualTo(2));
            Assert.That(reader.ReadByte(), Is.EqualTo(3));
            Assert.That(reader.ReadByte(), Is.EqualTo(200));
            Assert.That(packet.Length, Is.EqualTo(6));
        });
    }

    [Test]
    public void LifePackets() {
        ByteWriter bar = HideAndSeekPacket.LifeBar(4, HideAndSeekField.SeekerBarType, 3, 2, 3);
        Assert.That(bar.Length, Is.EqualTo(2 + 1 + 4 + 2 + 12));
        ByteReader reader = Read(bar, 2, 4);
        Assert.Multiple(() => {
            Assert.That(reader.ReadShort(), Is.EqualTo(2));
            Assert.That(reader.ReadInt(), Is.EqualTo(3));
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
            Assert.That(reader.ReadInt(), Is.EqualTo(3));
        });
        ByteWriter life = HideAndSeekPacket.Life(4, 1, 2);
        Assert.That(life.Length, Is.EqualTo(2 + 1 + 4 + 8));
        reader = Read(life, 4, 4);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
        });
    }

    [Test]
    public void WatchPacket() {
        ByteWriter watch = HideAndSeekPacket.Watch(6, 7, true, false);
        Assert.That(watch.Length, Is.EqualTo(2 + 1 + 4 + 6));
        ByteReader reader = Read(watch, 7, 6);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(7));
            Assert.That(reader.ReadBool(), Is.True);
            Assert.That(reader.ReadBool(), Is.False);
        });
    }

    private static ByteReader Read(ByteWriter packet, byte mode, int objectId) {
        var reader = new ByteReader(packet.ToArray());
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.HideAndSeek));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        Assert.That(reader.ReadInt(), Is.EqualTo(objectId));
        return reader;
    }

    [TestCase((byte) 1, 20)]
    [TestCase((byte) 2, 16)]
    [TestCase((byte) 0, 16)]
    public void InfoPacket(byte team, int length) {
        ByteWriter packet = HideAndSeekPacket.Info(77, true, false, 5, 2, team, 9);
        Assert.That(packet.Length, Is.EqualTo(2 + 1 + 4 + length));
        ByteReader reader = Read(packet, 0, 77);
        Assert.Multiple(() => {
            Assert.That(reader.ReadBool(), Is.True);
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(reader.ReadLong(), Is.EqualTo(5));
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
            Assert.That(reader.ReadByte(), Is.EqualTo(team));
            if (team == 1) {
                Assert.That(reader.ReadInt(), Is.EqualTo(9));
            }
        });
    }

    [Test]
    public void SkillSetPackets() {
        ByteReader add = Read(HideAndSeekPacket.AddSkillSet(5, 99930041), 5, 5);
        Assert.That(add.ReadInt(), Is.EqualTo(99930041));
        ByteReader remove = Read(HideAndSeekPacket.RemoveSkillSet(5, 99930041), 6, 5);
        Assert.That(remove.ReadInt(), Is.EqualTo(99930041));
    }

    [Test]
    public void ResultPacket_NoReward() {
        ByteWriter packet = HideAndSeekPacket.Result(8, HideAndSeekField.ResultRound, 0, 0);
        Assert.That(packet.Length, Is.EqualTo(2 + 1 + 4 + 16));
        ByteReader reader = Read(packet, 8, 8);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
        });
    }

    // A seeker who leaves the field while the seekers look gets the seek skill set back (mode 6) on the way out; one
    // who leaves before (while the hiders hide) never had it and gets none.
    [TestCase(HideAndSeekStep.LookingForATeams, true)]
    [TestCase(HideAndSeekStep.BeInHidingTeams, false)]
    public void SeekerLeaves_GetsSkillSetBack(HideAndSeekStep leaveAt, bool expected) {
        var test = new TestGameSession();
        test.SetServerTable(new HideAndSeekTable(new Dictionary<int, HideAndSeekTable.Field>()));
        GameSession session = test.Start();
        FieldManager field = session.Field!;
        // The test field is built without its initializers: the trigger user values each step sets, and the portals
        // the moves look up (none: the players stay).
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo userValues = typeof(FieldManager).GetField(nameof(FieldManager.UserValues))!;
        if (userValues.GetValue(field) == null) {
            userValues.SetValue(field, new TriggerUserValues());
        }
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", any)!;
        if (portals.GetValue(field) == null) {
            portals.SetValue(field, new System.Collections.Concurrent.ConcurrentDictionary<int, FieldPortal>());
        }
        field.StartHideAndSeek(1);
        HideAndSeekField game = field.HideAndSeek!;
        game.Constant("SeekTeamSkillSetID", "99930041", "ALL");
        for (int i = 0; i < 20 && game.Step != leaveAt; i++) {
            game.Update(field.FieldTick);
        }
        Assert.That(game.Step, Is.EqualTo(leaveAt));
        FieldPlayer player = session.Player;
        test.TakeSent();

        game.Leave(player);
        byte[] removeSkillSet = HideAndSeekPacket.RemoveSkillSet(player.ObjectId, 99930041).ToArray();
        List<byte[]> sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.HideAndSeek && packet[2] == 6).ToList();
        Assert.Multiple(() => {
            Assert.That(sent, expected ? Is.EqualTo(new[] { removeSkillSet }) : Is.Empty);
            Assert.That(game.Step, Is.EqualTo(HideAndSeekStep.TeamMatchResult));
        });
    }
}
