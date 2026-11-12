using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Tests.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.Pvp;

// The free-for-all PvP zone (PvpZoneField) of an individual arena field, in a test field with three players: the
// points and rows when the fight starts, a hit's damage row, a kill's rows and kill log, the end with ranks, poses,
// rewards (the constants' tokens and the arena row's item) and points back to 0, and the world told.
public class PvpZoneFieldTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ArenaItem = 40100023;

    private sealed class Arena {
        public required TestGameSession[] Tests;
        public required FieldManager Field;
        public readonly List<int> Ended = [];
        public FieldPlayer Player(int index) => Tests[index].Session.Player;
    }

    private static void Tables(TestGameSession test) {
        test.SetServerTable(new ConstantsTable(HonorTokenMax: 30000, ModePvPScoreKill: 100, ModePvPScoreDead: -50,
            PvpFFAReward1Count: 30, PvpFFAReward2Count: 25, PvpFFAReward3Count: 20, PvpFFAReward4Count: 15, PvpFFAReward5Count: 15,
            PvpFFAReward6Count: 15, PvpFFAReward7Count: 15, PvpFFAReward8Count: 10, PvpFFAReward9Count: 10, PvpFFAReward10Count: 10,
            PvpFFARewardItem: 90000006, pvpBtiRewardItem: 90000006, pvpBtiRewardWinnerCount: 30, pvpBtiRewardLoserCount: 10,
            pvpFFALongComboTick: 10000, pvpFFASlayerCount: 5, pvpFFARulerCount: 10));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>>(), new Dictionary<int, long>()));
        test.Register(TestGameSession.Metadata(90000006), TestGameSession.Metadata(ArenaItem, slotMax: 9999));
    }

    private static Arena Start() => Start(field => field.StartPvpZone(2, 101, 0, 1, 0, [], _ => field.Players.Values));

    private static Arena Start(Action<FieldManager> zone) {
        var first = new TestGameSession();
        first.Start();
        var tests = new List<TestGameSession> { first };
        for (int i = 0; i < 2; i++) {
            var other = new TestGameSession(first.Options);
            other.Start();
            tests.Add(other);
        }
        foreach (TestGameSession test in tests) {
            Tables(test);
        }
        FieldManager field = first.Session.Field!;
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc>());
        for (int i = 1; i < tests.Count; i++) {
            typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(tests[i].Session.Player, i + 1);
            field.Players[i + 1] = tests[i].Session.Player;
            tests[i].Session.Field = field;
        }
        var arena = new Arena { Tests = tests.ToArray(), Field = field };
        field.StartArena(4, PvpHandlerTests.Arenas.Entries[2], tests.Select(test => test.CharacterId).ToList(), arena.Ended.Add);
        // As the arena's script: box, 0 s to prepare, a short fight, no effect.
        zone(field);
        return arena;
    }

    private static List<string> Pvp(TestGameSession test) {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) is SendOp.Pvp or SendOp.PlayerKillNotice).Select(Convert.ToHexString).ToList();
    }

    private static string Int(int value) => Convert.ToHexString(BitConverter.GetBytes(value));
    private static string Long(long value) => Convert.ToHexString(BitConverter.GetBytes(value));

    [Test]
    public void FreeForAll_StartHitsKillEnd() {
        Arena arena = Start();
        foreach (TestGameSession test in arena.Tests) {
            test.TakeSent();
        }
        long now = Environment.TickCount64;
        arena.Field.PvpZone!.Update(now + 10);

        // The start: the ranking cleared, then each participant's point (101 up, team 0, mode 2) and its empty row.
        List<string> start = Pvp(arena.Tests[0]);
        Assert.That(start[0], Is.EqualTo("B600" + "66"));
        Assert.That(start.Where(packet => packet.StartsWith("B60064")).Count(), Is.EqualTo(3));
        Assert.That(start, Does.Contain("B600" + "64" + Int(arena.Player(1).ObjectId) + "66000000" + "00" + "02"));
        Assert.That(start, Does.Contain("B600" + "67" + Long(arena.Tests[2].CharacterId) + "00000000" + "00000000" + "00000000" + "00000000"));
        Assert.That(arena.Field.PlayerHostile(arena.Player(0), arena.Player(1)), Is.True);

        arena.Tests[1].TakeSent();
        arena.Tests[2].TakeSent();
        // Player 0 hits player 1 for 40, then player 1 dies: player 0 gets the kill (+100), player 1 the death (-50).
        arena.Field.PvpZone.Damage(arena.Player(0), arena.Player(1), 40);
        Assert.That(Pvp(arena.Tests[2]), Is.EqualTo(new[] { "B600" + "6A" + Long(arena.Tests[0].CharacterId) + "28000000" }));
        arena.Field.PvpZone.Death(arena.Player(1));
        List<string> kill = Pvp(arena.Tests[2]);
        Assert.That(kill[0], Is.EqualTo("B600" + "67" + Long(arena.Tests[0].CharacterId) + "64000000" + "01000000" + "00000000" + "00000000"));
        Assert.That(kill[1], Is.EqualTo("B600" + "67" + Long(arena.Tests[1].CharacterId) + "CEFFFFFF" + "00000000" + "01000000" + "00000000"));
        Assert.That(kill[2], Does.StartWith("A300" + "00"));

        // The end: ranks 1 (the killer), 2 and 3; the first gets the win pose, 30 tokens and the arena's winner item.
        long tokens = arena.Tests[0].Session.Currency[CurrencyType.ValorToken];
        arena.Tests[0].TakeSent();
        arena.Tests[1].TakeSent();
        arena.Field.PvpZone.Update(now + 2000);
        List<string> end = Pvp(arena.Tests[0]);
        Assert.That(end.Take(2), Is.EqualTo(new[] { "B600" + "68" + "01000000", "B600" + "65" + "01" }));
        Assert.That(end.Where(packet => packet.StartsWith("B60064")).All(packet => packet.EndsWith("00000000" + "0000")), Is.True);
        Assert.That(arena.Tests[0].Session.Currency[CurrencyType.ValorToken], Is.EqualTo(tokens + 30));
        Assert.That(arena.Tests[0].Session.Item.Inventory.Find(ArenaItem).Sum(item => item.Amount), Is.EqualTo(100));
        Assert.That(arena.Tests[2].Session.Item.Inventory.Find(ArenaItem).Sum(item => item.Amount), Is.EqualTo(10));
        Assert.That(Pvp(arena.Tests[1]).Take(2), Is.EqualTo(new[] { "B600" + "68" + "03000000", "B600" + "65" + "00" }));
        Assert.That(arena.Field.PvpZone.Ended, Is.True);
        Assert.That(arena.Field.PlayerHostile(arena.Player(0), arena.Player(1)), Is.False);
        System.Threading.SpinWait.SpinUntil(() => arena.Ended.Count > 0, 2000);
        Assert.That(arena.Ended, Is.EqualTo(new[] { 4 }));
    }

    // Treasure Island: the players are dealt to red (201) and blue (202) with mode 3, their rows, the score, the gauge
    // and the team result's reset; at the end the result with the reward count, the pose and the tokens; the world told.
    [Test]
    public void Teams_StartAndEnd() {
        Arena arena = Start(field => field.StartPvpZone(3, 104, 0, 1, 0, [1, 2, 101, 102, 103], _ => field.Players.Values));
        foreach (TestGameSession test in arena.Tests) {
            test.TakeSent();
        }
        long now = Environment.TickCount64;
        arena.Field.PvpZone!.Update(now + 10);
        List<string> start = Pvp(arena.Tests[0]);
        Assert.That(start.Count(packet => packet.StartsWith("B60064") && packet.EndsWith("C9000000" + "00" + "03")), Is.EqualTo(2));
        Assert.That(start.Count(packet => packet.StartsWith("B60064") && packet.EndsWith("CA000000" + "00" + "03")), Is.EqualTo(1));
        Assert.That(start.Count(packet => packet.StartsWith("B6006D")), Is.EqualTo(3));
        Assert.That(start.TakeLast(3), Is.EqualTo(new[] { "B600" + "6C" + "00000000" + "00000000", "B600" + "6F" + "00000000" + "F4010000", "B600" + "6B" }));
        int teamOf0 = start.Any(packet => packet == "B600" + "64" + Int(arena.Player(0).ObjectId) + "C9000000" + "00" + "03") ? 0xC9 : 0xCA;
        Assert.That(arena.Field.PlayerHostile(arena.Player(0), arena.Player(1)) || arena.Field.PlayerHostile(arena.Player(0), arena.Player(2)), Is.True);

        long tokens = arena.Tests[0].Session.Currency[CurrencyType.ValorToken];
        arena.Tests[0].TakeSent();
        arena.Field.PvpZone.Update(now + 2000);
        List<string> end = Pvp(arena.Tests[0]);
        // No score: a draw, team 0, the loser's count.
        Assert.That(end.Take(2), Is.EqualTo(new[] { "B600" + "6E" + "00000000" + "0A000000", "B600" + "65" + "00" }));
        Assert.That(arena.Tests[0].Session.Currency[CurrencyType.ValorToken], Is.EqualTo(tokens + 10));
        Assert.That(teamOf0, Is.AnyOf(0xC9, 0xCA));
        Assert.That(arena.Field.PvpZone.Ended, Is.True);
        System.Threading.SpinWait.SpinUntil(() => arena.Ended.Count > 0, 2000);
        Assert.That(arena.Ended, Is.EqualTo(new[] { 4 }));
    }

    // The Red Arena: the prepare and start texts (MassiveEvent text of the PvP string kind 3, ids 10 and 11) and the two
    // fighters' points in mode 1; a fighter's death makes the other the winner (s_dualwin1); with no challenger left
    // the zone ends with the winner's s_dualwin9 and the arena row's winner item to the champion only.
    [Test]
    public void RedArena_FaceOffAndChampion() {
        Arena arena = Start(field => field.StartPvpZone(1, 102, 0, 120, 0, [], _ => field.Players.Values.Take(2)));
        foreach (TestGameSession test in arena.Tests) {
            test.TakeSent();
        }
        long now = Environment.TickCount64;
        arena.Field.PvpZone!.Update(now + 10);
        string prepare = Convert.ToHexString(arena.Tests[2].TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.MassiveEvent));
        Assert.That(prepare, Does.EndWith("01" + "03000000" + "0A000000" + "00000000" + "B80B0000"));
        arena.Field.PvpZone.Update(now + 10 + Maple2.Model.Game.Pvp.RedArenaState.PrepareMs);
        Assert.That(arena.Field.PlayerHostile(arena.Player(0), arena.Player(1)), Is.True);
        Assert.That(arena.Field.PlayerHostile(arena.Player(0), arena.Player(2)), Is.False);
        Assert.That(Pvp(arena.Tests[2]).Count(packet => packet.StartsWith("B60064") && packet.EndsWith("00" + "01")), Is.EqualTo(2));

        arena.Field.PvpZone.Death(arena.Player(1));
        Assert.That(arena.Field.PvpZone.Ended, Is.True);
        List<string> texts = arena.Tests[2].TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.MassiveEvent).Select(Convert.ToHexString).ToList();
        Assert.That(texts[0], Does.Contain("03000000" + "01000000" + "01000000"));
        Assert.That(texts[1], Does.Contain("03000000" + "09000000" + "01000000"));
        Assert.That(arena.Tests[0].Session.Item.Inventory.Find(ArenaItem).Sum(item => item.Amount), Is.EqualTo(100));
        Assert.That(arena.Tests[1].Session.Item.Inventory.Find(ArenaItem).Sum(item => item.Amount), Is.EqualTo(10));
        Assert.That(arena.Tests[2].Session.Item.Inventory.Find(ArenaItem).Any(), Is.False);
    }

    // An arena field whose match characters all left before its zone ended tells the world the match is over.
    [Test]
    public void Arena_AllLeftBeforeTheEnd_EndsTheMatch() {
        Arena arena = Start(field => { });
        foreach (FieldPlayer player in arena.Tests.Select(test => test.Session.Player).ToList()) {
            arena.Field.Players.TryRemove(player.ObjectId, out _);
            arena.Field.Arena!.Left(arena.Field);
        }
        System.Threading.SpinWait.SpinUntil(() => arena.Ended.Count > 0, 2000);
        Assert.That(arena.Ended, Is.EqualTo(new[] { 4 }));
        arena.Field.Arena!.Close();
        System.Threading.Thread.Sleep(50);
        Assert.That(arena.Ended, Is.EqualTo(new[] { 4 }));
    }

    // Dead players count in no box: with only one living player in every box, its team takes the flags alone and it
    // plunders the other team's treasure once; the dead of the other team neither contest nor plunder.
    [Test]
    public void Teams_DeadPlayersCountInNoBox() {
        Arena arena = Start(field => field.StartPvpZone(3, 104, 0, 600, 0, [1, 2, 101, 102, 103], _ => field.Players.Values));
        long now = Environment.TickCount64;
        arena.Field.PvpZone!.Update(now + 10);
        foreach (int index in new[] { 1, 2 }) {
            typeof(FieldPlayer).GetField("isDead", Any)!.SetValue(arena.Player(index), true);
        }
        foreach (TestGameSession test in arena.Tests) {
            test.TakeSent();
        }
        arena.Field.PvpZone.Update(now + 1_000);
        arena.Field.PvpZone.Update(now + 1_000 + Maple2.Model.Game.Pvp.TeamZoneState.CaptureMs);
        List<string> sent = Pvp(arena.Tests[0]);
        Assert.That(sent.Count(packet => packet.StartsWith("B60070")), Is.EqualTo(3));
        Assert.That(sent.Count(packet => packet.StartsWith("B60071")), Is.EqualTo(1));
    }
}
