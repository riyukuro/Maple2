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
            PvpFFARewardItem: 90000006));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>>(), new Dictionary<int, long>()));
        test.Register(TestGameSession.Metadata(90000006), TestGameSession.Metadata(ArenaItem, slotMax: 9999));
    }

    private static Arena Start() {
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
        // As the Crimson Tear Mine's script: box 101, 0 s to prepare, 1 s to fight, no effect.
        field.StartPvpZone(101, 0, 1, 0, () => field.Players.Values);
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
}
