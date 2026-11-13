using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Microsoft.EntityFrameworkCore;

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

    // Rewards: a normal end pays the winners the winner reward and the others the loser's.
    [Test]
    public void Reward_NormalEnd_WinnerAndLoser() {
        (HideAndSeekState state, long seeker, long[] hiders) = Seeking();
        state.Hit(seeker, hiders[0], 5000, 108_000);
        state.Hit(seeker, hiders[1], 5000, 109_000);
        Assert.Multiple(() => {
            Assert.That(state.Step, Is.EqualTo(HideAndSeekStep.TeamMatchResult));
            Assert.That(state.EndedByLeave, Is.False);
            Assert.That(state.RewardOf(seeker), Is.EqualTo("Winner"));
            Assert.That(state.RewardOf(hiders[0]), Is.EqualTo("Loser"));
            Assert.That(state.RewardOf(12345), Is.Null);
        });
    }

    // Two characters start a game (RequireUserCount 2) and one leaves: the one left wins, but fewer than the start
    // minimum are still here, so no one is paid.
    [Test]
    public void Reward_TwoPlayersOneLeaves_NoReward() {
        HideAndSeekState state = Game();
        state.Split([1, 2], new Random(3));
        state.Advance(60_000, 2);
        state.Advance(70_000, 2);
        state.Advance(75_000, 2);
        state.Advance(77_000, 2);
        long seeker = state.Teams.Single(entry => entry.Value == HideAndSeekState.SeekTeam).Key;
        long hider = state.Teams.Single(entry => entry.Value == HideAndSeekState.HideTeam).Key;
        Assert.That(state.Remove(seeker, 80_000), Is.EqualTo(HideAndSeekStep.TeamMatchResult));
        Assert.Multiple(() => {
            Assert.That(state.Winner, Is.EqualTo(HideAndSeekState.HideTeam));
            Assert.That(state.EndedByLeave, Is.True);
            Assert.That(state.RewardOf(hider), Is.Null);
        });
    }

    // A team emptied by leaving: the team left behind is enough for the start minimum but won only because the other
    // side left, so it gets the loser's reward.
    [Test]
    public void Reward_OtherTeamLeft_LoserRewardOnly() {
        HideAndSeekState state = Game();
        state.Split([1, 2, 3, 4, 5, 6], new Random(3));
        long[] seekers = state.Teams.Where(entry => entry.Value == HideAndSeekState.SeekTeam).Select(entry => entry.Key).ToArray();
        long hider = state.Teams.First(entry => entry.Value == HideAndSeekState.HideTeam).Key;
        state.Advance(60_000, 6);
        state.Advance(70_000, 6);
        state.Advance(75_000, 6);
        state.Advance(77_000, 6);
        Assert.That(seekers, Has.Length.EqualTo(2));
        Assert.That(state.Remove(seekers[0], 80_000), Is.Null);
        Assert.That(state.Remove(seekers[1], 80_000), Is.EqualTo(HideAndSeekStep.TeamMatchResult));
        Assert.Multiple(() => {
            Assert.That(state.Winner, Is.EqualTo(HideAndSeekState.HideTeam));
            Assert.That(state.EndedByLeave, Is.True);
            Assert.That(state.RewardOf(hider), Is.EqualTo("Loser"));
        });
    }

    // The last hider still hidden leaves while the seekers look: the seekers win by the leave, no winner reward.
    [Test]
    public void Reward_LastHiderLeavesWhileSeeking_NoWinnerReward() {
        (HideAndSeekState state, long seeker, long[] hiders) = Seeking();
        state.Hit(seeker, hiders[0], 5000, 108_000);
        Assert.That(state.Remove(hiders[1], 109_000), Is.EqualTo(HideAndSeekStep.TeamMatchResult));
        Assert.Multiple(() => {
            Assert.That(state.Winner, Is.EqualTo(HideAndSeekState.SeekTeam));
            Assert.That(state.EndedByLeave, Is.True);
            Assert.That(state.RewardOf(seeker), Is.EqualTo("Loser"));
            Assert.That(state.RewardOf(hiders[0]), Is.EqualTo("Loser"));
        });
    }

    private static void SetEvents(HideAndSeekState state) {
        state.SetConstant("EventHideAndSeekStart", "hideandseek_start", "ALL", "KR");
        state.SetConstant("EventDailyQuestStart", "dailyquest_start", "ALL", "KR");
        state.SetConstant("EventHideAndSeekWin", "hideandseek_win", "ALL", "KR");
    }

    // The trigger codes at the result: the start codes for both teams, the win code for the winners, each once per
    // player per game; a new game (Split) raises them again.
    [Test]
    public void Events_OncePerPlayerPerGame() {
        (HideAndSeekState state, long seeker, long[] hiders) = Seeking();
        SetEvents(state);
        state.Hit(seeker, hiders[0], 5000, 108_000);
        state.Hit(seeker, hiders[1], 5000, 109_000);
        Assert.Multiple(() => {
            Assert.That(state.EventsOf(seeker), Is.EqualTo(new[] { "hideandseek_start", "dailyquest_start", "hideandseek_win" }));
            Assert.That(state.EventsOf(hiders[0]), Is.EqualTo(new[] { "hideandseek_start", "dailyquest_start" }));
            Assert.That(state.EventsOf(seeker), Is.Empty);
            Assert.That(state.EventsOf(hiders[0]), Is.Empty);
            Assert.That(state.EventsOf(12345), Is.Empty);
        });
        state.Split([seeker, hiders[0]], new Random(1));
        Assert.That(state.EventsOf(seeker), Is.Not.Empty);
    }

    // A game that ended because a player left raises nothing, for the team left behind as well.
    [Test]
    public void Events_NoneAfterALeave() {
        (HideAndSeekState state, long seeker, long[] hiders) = Seeking();
        SetEvents(state);
        state.Hit(seeker, hiders[0], 5000, 108_000);
        Assert.That(state.Remove(hiders[1], 109_000), Is.EqualTo(HideAndSeekStep.TeamMatchResult));
        Assert.Multiple(() => {
            Assert.That(state.RewardOf(seeker), Is.EqualTo("Loser"));
            Assert.That(state.EventsOf(seeker), Is.Empty);
            Assert.That(state.EventsOf(hiders[0]), Is.Empty);
        });
    }

    // A code the field does not give is not raised.
    [Test]
    public void Events_OnlyTheFieldsCodes() {
        (HideAndSeekState state, long seeker, long[] hiders) = Seeking();
        state.SetConstant("EventHideAndSeekWin", "hideandseek_win", "ALL", "KR");
        state.Hit(seeker, hiders[0], 5000, 108_000);
        state.Hit(seeker, hiders[1], 5000, 109_000);
        Assert.Multiple(() => {
            Assert.That(state.EventsOf(seeker), Is.EqualTo(new[] { "hideandseek_win" }));
            Assert.That(state.EventsOf(hiders[0]), Is.Empty);
        });
    }

    // The client sends where a seek shot landed: farther than the seek attack's range (750) it is pulled back onto
    // the line to the seeker.
    [Test]
    public void ClampLanding_ToTheAttackRange() {
        var seeker = new Vector3(100, 0, 0);
        Assert.Multiple(() => {
            Assert.That(HideAndSeekField.ClampLanding(seeker, new Vector3(400, 0, 0), 750), Is.EqualTo(new Vector3(400, 0, 0)));
            Assert.That(HideAndSeekField.ClampLanding(seeker, new Vector3(5100, 0, 0), 750), Is.EqualTo(new Vector3(850, 0, 0)));
            Assert.That(HideAndSeekField.ClampLanding(seeker, new Vector3(5100, 0, 0), 0), Is.EqualTo(seeker));
        });
    }

    private static SkillMetadataAttack SeekAttack(long damage, bool constDamage = true) {
        return (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Range = (SkillMetadataRange) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataRange)) with { Distance = 750 },
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: constDamage, Value: damage,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };
    }

    private static SkillMetadata Skill(int id, SkillMetadataAttack attack) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Motions = [new SkillMetadataMotion(null!, [attack], new Dictionary<string, byte>())],
        };
        return new SkillMetadata(id, 1, null, property, null!, level);
    }

    // HideAndSeekField.Hit in a field with a seeker and a hider: the seek skill's const 2500 takes the hider's object
    // (5000, two hits) down and sends the hider its bar's hits left; the second hit catches it (mode 3, the result).
    // Another skill of the seeker is the game's but does nothing; the hider's skills are not the game's.
    [Test]
    public void FieldHit_SeekSkillCatchesInTwoHits() {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var a = new TestGameSession();
        a.SetServerTable(new HideAndSeekTable(new Dictionary<int, HideAndSeekTable.Field>()));
        a.Start();
        var b = new TestGameSession(a.Options);
        b.SetServerTable(new HideAndSeekTable(new Dictionary<int, HideAndSeekTable.Field>()));
        b.Start();
        FieldManager field = a.Session.Field!;
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", any)!.SetValue(b.Session.Player, 2);
        field.Players[2] = b.Session.Player;
        b.Session.Field = field;
        a.Session.Ride = new Maple2.Server.Game.Manager.RideManager(a.Session);
        b.Session.Ride = new Maple2.Server.Game.Manager.RideManager(b.Session);
        // Achieves 23400056 (hideandseek_start) and 23400057 (hideandseek_win) as the data has them (trigger, goal 10).
        AchievementMetadata Event(int id, string code) => new(id, $"{id}", false, AchievementCategory.Life, [],
            new Dictionary<int, AchievementMetadataGrade> {
                [1] = new(1, new ConditionMetadata(ConditionType.trigger, 10, new ConditionMetadata.Parameters(Strings: [code]), null), null),
            }, false);
        a.AddAchievement(Event(23400056, "hideandseek_start"), Event(23400057, "hideandseek_win"));
        b.AddAchievement(Event(23400056, "hideandseek_start"), Event(23400057, "hideandseek_win"));
        FieldInfo userValues = typeof(FieldManager).GetField(nameof(FieldManager.UserValues))!;
        if (userValues.GetValue(field) == null) {
            userValues.SetValue(field, new TriggerUserValues());
        }
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", any)!;
        if (portals.GetValue(field) == null) {
            portals.SetValue(field, new System.Collections.Concurrent.ConcurrentDictionary<int, FieldPortal>());
        }
        // The seek skill in the field's skill store, as the ingest has it (level 1, const 2500, range 750).
        SkillMetadataAttack attack = SeekAttack(2500);
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", any)!.GetValue(skills)!;
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(HideAndSeekField.SeekSkillId, (short) 1), Skill(HideAndSeekField.SeekSkillId, attack)]);
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", any)!.SetValue(field, skills);

        field.StartHideAndSeek(1);
        HideAndSeekField game = field.HideAndSeek!;
        game.Constant("RequireUserCount", "2", "ALL");
        game.Constant("EventHideAndSeekStart", "hideandseek_start", "ALL");
        game.Constant("EventHideAndSeekWin", "hideandseek_win", "ALL");
        for (int i = 0; i < 20 && game.Step != HideAndSeekStep.LookingForATeams; i++) {
            game.Update(field.FieldTick);
        }
        Assert.That(game.Step, Is.EqualTo(HideAndSeekStep.LookingForATeams));
        var state = (HideAndSeekState) typeof(HideAndSeekField).GetField("state", any)!.GetValue(game)!;
        bool aSeeks = state.TeamOf(a.CharacterId) == HideAndSeekState.SeekTeam;
        (FieldPlayer seeker, FieldPlayer hider, TestGameSession hiderTest) = aSeeks
            ? (a.Session.Player, b.Session.Player, b)
            : (b.Session.Player, a.Session.Player, a);
        // No hideAndSeek.xml row in the test: the hider gets its object here.
        state.Hide(hider.Value.Character.Id, 99920001, 0, 5000);
        a.TakeSent();
        b.TakeSent();

        DamageRecord Record(int skillId) => new(Skill(skillId, attack), attack) { SkillId = skillId };

        Assert.Multiple(() => {
            Assert.That(game.Hit(hider, seeker, Record(HideAndSeekField.SeekSkillId), attack), Is.False);
            Assert.That(game.Hit(seeker, hider, Record(HideAndSeekField.SeekSkillId + 1), attack), Is.True);
            Assert.That(state.HiderOf(hider.Value.Character.Id)!.Health, Is.EqualTo(5000));
        });

        DamageRecord first = Record(HideAndSeekField.SeekSkillId);
        Assert.That(game.Hit(seeker, hider, first, attack), Is.True);
        byte[] life = HideAndSeekPacket.Life(hider.ObjectId, 1, 2).ToArray();
        Assert.Multiple(() => {
            Assert.That(first.Targets.ContainsKey(hider.ObjectId), Is.True);
            Assert.That(hiderTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.HideAndSeek && packet[2] == 4), Is.EqualTo(new[] { life }));
            Assert.That(state.IsCaught(hider.Value.Character.Id), Is.False);
        });

        Assert.That(game.Hit(seeker, hider, Record(HideAndSeekField.SeekSkillId), attack), Is.True);
        List<byte[]> sent = hiderTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.HideAndSeek).ToList();
        Assert.Multiple(() => {
            Assert.That(state.IsCaught(hider.Value.Character.Id), Is.True);
            Assert.That(state.Kills(seeker.Value.Character.Id), Is.EqualTo(1));
            Assert.That(sent.Any(packet => packet.SequenceEqual(HideAndSeekPacket.Life(hider.ObjectId, 0, 2).ToArray())), Is.True);
            Assert.That(sent.Any(packet => packet.SequenceEqual(HideAndSeekPacket.End(hider.ObjectId).ToArray())), Is.True);
            Assert.That(game.Step, Is.EqualTo(HideAndSeekStep.TeamMatchResult));
            Assert.That(state.RewardOf(seeker.Value.Character.Id), Is.EqualTo("Winner"));
        });
        // The result raised the trigger codes once: both players started a game, the seeker won it.
        TestGameSession seekerTest = aSeeks ? a : b;
        Assert.Multiple(() => {
            Assert.That(seekerTest.TrophyCounter(23400056), Is.EqualTo(1));
            Assert.That(seekerTest.TrophyCounter(23400057), Is.EqualTo(1));
            Assert.That(hiderTest.TrophyCounter(23400056), Is.EqualTo(1));
            Assert.That(hiderTest.TrophyCounter(23400057), Is.Null);
        });
    }
}
