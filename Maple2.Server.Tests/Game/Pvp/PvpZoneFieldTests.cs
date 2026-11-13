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
    private const int OnePunchItem = 30001533;

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
            pvpFFALongComboTick: 10000, pvpFFASlayerCount: 5, pvpFFARulerCount: 10,
            PvpOnePunchReward1Count: 9, PvpOnePunchReward2Count: 8, PvpOnePunchReward3Count: 7, PvpOnePunchReward4Count: 6,
            PvpOnePunchReward5Count: 5, PvpOnePunchReward6Count: 4, PvpOnePunchReward7Count: 3, PvpOnePunchReward8Count: 3,
            PvpOnePunchReward9Count: 2, PvpOnePunchReward10Count: 2, PvpOnePunchRewardItem: OnePunchItem));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>>(), new Dictionary<int, long>()));
        test.Register(TestGameSession.Metadata(90000006), TestGameSession.Metadata(ArenaItem, slotMax: 9999),
            TestGameSession.Metadata(OnePunchItem, slotMax: 9999));
    }

    private static Arena Start() => Start(field => field.StartPvpZone(2, 101, 0, 1, 0, [], _ => field.Players.Values));

    private static Arena Start(Action<FieldManager> zone, int players = 3, bool arenaMatch = true) {
        var first = new TestGameSession();
        first.Start();
        var tests = new List<TestGameSession> { first };
        for (int i = 0; i < players - 1; i++) {
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
        if (arenaMatch) {
            field.StartArena(4, PvpHandlerTests.Arenas.Entries[2], tests.Select(test => test.CharacterId).ToList(), arena.Ended.Add);
        }
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

    private static int Held(Arena arena, int index, int itemId) =>
        arena.Tests[index].Session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    // 61000035's one punch round (type 5, box 701, 240 s, no effect) in a field that is no arena match: the points carry
    // mode 5, and at the end each participant still here gets PvpOnePunchReward<rank>Count of PvpOnePunchRewardItem
    // with no kill addition (UIPvPResultDialog::FUN_141344210) and no blood mine exp.
    // A match character entering the arena match field gets Pvp 10 (the client's arena state back to 0, so the window
    // shows Register after the match); a character not of the match gets nothing.
    [Test]
    public void Enter_MatchCharacterStateBackToZero() {
        Arena arena = Start(_ => { }, players: 2, arenaMatch: false);
        arena.Field.StartArena(4, PvpHandlerTests.Arenas.Entries[2], [arena.Tests[0].CharacterId], arena.Ended.Add);
        foreach (TestGameSession test in arena.Tests) {
            test.TakeSent();
        }

        arena.Field.Arena!.Enter(arena.Player(0));
        arena.Field.Arena!.Enter(arena.Player(1));

        Assert.That(Pvp(arena.Tests[0]), Is.EqualTo(new[] { "B600" + "0A" }));
        Assert.That(Pvp(arena.Tests[1]), Is.Empty);
    }

    [Test]
    public void OnePunch_ModeAndRankTokens() {
        Arena arena = Start(field => field.StartPvpZone(5, 701, 0, 1, 0, [], _ => field.Players.Values), players: 4, arenaMatch: false);
        foreach (TestGameSession test in arena.Tests) {
            test.TakeSent();
        }
        long now = Environment.TickCount64;
        arena.Field.PvpZone!.Update(now + 10);
        List<string> start = Pvp(arena.Tests[0]);
        Assert.That(start.Count(packet => packet.StartsWith("B60064") && packet.EndsWith("00" + "05")), Is.EqualTo(4));

        // Player 0 kills player 1: rank 1 with a kill, still only the rank's 9.
        arena.Field.PvpZone.Damage(arena.Player(0), arena.Player(1), 40);
        arena.Field.PvpZone.Death(arena.Player(1));
        arena.Field.PvpZone.Update(now + 2000);
        Assert.That(arena.Field.PvpZone.Ended, Is.True);
        Assert.That(Held(arena, 0, OnePunchItem), Is.EqualTo(9));
        Assert.That(Held(arena, 1, OnePunchItem), Is.EqualTo(6));
        Assert.That(new[] { Held(arena, 2, OnePunchItem), Held(arena, 3, OnePunchItem) }.Order(), Is.EqualTo(new[] { 7, 8 }));
        Assert.That(Held(arena, 0, 90000006) + Held(arena, 0, ArenaItem), Is.Zero);
        Assert.That(arena.Ended, Is.Empty);
    }

    // The farm guard: a round with fewer participants than the script's minimum (4) pays no tokens, and a participant
    // that left before the end gets none.
    [Test]
    public void OnePunch_TokensNeedTheMinimumAndPresence() {
        Arena few = Start(field => field.StartPvpZone(5, 701, 0, 1, 0, [], _ => field.Players.Values), players: 3, arenaMatch: false);
        long now = Environment.TickCount64;
        few.Field.PvpZone!.Update(now + 10);
        few.Field.PvpZone.Update(now + 2000);
        Assert.That(few.Field.PvpZone.Ended, Is.True);
        Assert.That(Enumerable.Range(0, 3).Sum(index => Held(few, index, OnePunchItem)), Is.Zero);

        Arena left = Start(field => field.StartPvpZone(5, 701, 0, 1, 0, [], _ => field.Players.Values), players: 4, arenaMatch: false);
        now = Environment.TickCount64;
        left.Field.PvpZone!.Update(now + 10);
        left.Field.Players.TryRemove(left.Player(3).ObjectId, out _);
        left.Field.PvpZone.Leave(left.Player(3));
        left.Field.PvpZone.Update(now + 2000);
        Assert.That(Held(left, 3, OnePunchItem), Is.Zero);
        Assert.That(Enumerable.Range(0, 3).Select(index => Held(left, index, OnePunchItem)).Order(), Is.EqualTo(new[] { 7, 8, 9 }));
    }

    // 61000035_me_item's round (type 6): the same zone in mode 6, with no tokens (the client's dialog shows 0).
    [Test]
    public void ItemRound_ModeSixNoTokens() {
        Arena arena = Start(field => field.StartPvpZone(6, 701, 0, 1, 0, [], _ => field.Players.Values), players: 4, arenaMatch: false);
        foreach (TestGameSession test in arena.Tests) {
            test.TakeSent();
        }
        long now = Environment.TickCount64;
        arena.Field.PvpZone!.Update(now + 10);
        Assert.That(Pvp(arena.Tests[0]).Count(packet => packet.StartsWith("B60064") && packet.EndsWith("00" + "06")), Is.EqualTo(4));
        arena.Field.PvpZone.Update(now + 2000);
        Assert.That(arena.Field.PvpZone.Ended, Is.True);
        Assert.That(Enumerable.Range(0, 4).Sum(index => Held(arena, index, OnePunchItem) + Held(arena, index, 90000006)), Is.Zero);
    }

    // The free-for-all off an arena match's field (the guild and test maps) still pays nothing.
    [Test]
    public void FreeForAll_OffArenaNoTokens() {
        Arena arena = Start(field => field.StartPvpZone(2, 101, 0, 1, 0, [], _ => field.Players.Values), players: 4, arenaMatch: false);
        long tokens = arena.Tests[0].Session.Currency[CurrencyType.ValorToken];
        long now = Environment.TickCount64;
        arena.Field.PvpZone!.Update(now + 10);
        arena.Field.PvpZone.Update(now + 2000);
        Assert.That(arena.Field.PvpZone.Ended, Is.True);
        Assert.That(arena.Tests[0].Session.Currency[CurrencyType.ValorToken], Is.EqualTo(tokens));
        Assert.That(Held(arena, 0, OnePunchItem), Is.Zero);
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
        List<byte[]> fightStart = arena.Tests[2].TakeSent();
        // Points 101 and 102: the client never hits a PC of team 2 and lets one hit anyone (basecollision.cpp).
        Assert.That(fightStart.Select(Convert.ToHexString).Where(packet => packet.StartsWith("B60064")).Select(packet => packet[14..]).Order(),
            Is.EqualTo(new[] { "65000000" + "00" + "01", "66000000" + "00" + "01" }));
        // The spectator gets the fighters' observer dialog (Trigger dialog kind 2) with both fighters.
        string observer = fightStart.Select(Convert.ToHexString).Single(packet => packet.StartsWith("4E000F02"));
        Assert.That(observer, Does.Contain(Long(arena.Tests[0].CharacterId)).And.Contain(Long(arena.Tests[1].CharacterId)));

        arena.Field.PvpZone.Death(arena.Player(1));
        Assert.That(arena.Field.PvpZone.Ended, Is.True);
        List<byte[]> ended = arena.Tests[2].TakeSent();
        // The result closes the spectator's dialog (character ids 0).
        Assert.That(ended.Select(Convert.ToHexString).Any(packet => packet.StartsWith("4E000F02") && packet.EndsWith("01" + new string('0', 34 * 2))), Is.True);
        List<string> texts = ended.Where(packet => TestGameSession.Op(packet) == SendOp.MassiveEvent).Select(Convert.ToHexString).ToList();
        Assert.That(texts[0], Does.Contain("03000000" + "01000000" + "01000000"));
        Assert.That(texts[1], Does.Contain("03000000" + "09000000" + "01000000"));
        Assert.That(arena.Tests[0].Session.Item.Inventory.Find(ArenaItem).Sum(item => item.Amount), Is.EqualTo(100));
        Assert.That(arena.Tests[1].Session.Item.Inventory.Find(ArenaItem).Sum(item => item.Amount), Is.EqualTo(10));
        Assert.That(arena.Tests[2].Session.Item.Inventory.Find(ArenaItem).Any(), Is.False);
    }

    // The Red Arena's last face-off won by a death: the trigger's pvp_zone_ended (65000001 enter.xml, then WINNER to the
    // stage box 102) waits while the beaten fighter lies dead on the stage, and holds once it has revived (moved to the
    // spawn off the stage); the champion's pose, exp and reward still come at once.
    [Test]
    public void RedArena_EndWaitsForTheBeatenFighterToRevive() {
        Arena arena = Start(field => field.StartPvpZone(1, 102, 0, 120, 0, [], _ => field.Players.Values.Take(2)));
        long now = Environment.TickCount64;
        arena.Field.PvpZone!.Update(now + 10);
        arena.Field.PvpZone.Update(now + 10 + Maple2.Model.Game.Pvp.RedArenaState.PrepareMs);
        FieldInfo isDead = typeof(FieldPlayer).GetField("isDead", Any)!;
        isDead.SetValue(arena.Player(1), true);
        arena.Field.PvpZone.Death(arena.Player(1));
        Assert.That(arena.Field.PvpZone.Ended, Is.False);
        Assert.That(arena.Tests[0].Session.Item.Inventory.Find(ArenaItem).Sum(item => item.Amount), Is.EqualTo(100));
        isDead.SetValue(arena.Player(1), false);
        Assert.That(arena.Field.PvpZone.Ended, Is.True);
    }

    // The Red Arena's script (65000001 enter.xml) sets the zone on box 102, the stage, while everyone waits on the entry
    // grate (box 101): the participants are the field's players (box 0), so two players get a face-off; before, the
    // empty stage ended the zone at once with no champion.
    [Test]
    public void RedArena_ParticipantsFromTheField_NotTheStage() {
        Arena arena = Start(field => field.StartPvpZone(1, 102, 0, 120, 0, [],
            box => box == 0 ? field.Players.Values.Take(2) : Enumerable.Empty<FieldPlayer>()));
        long now = Environment.TickCount64;
        arena.Field.PvpZone!.Update(now + 10);
        Assert.That(arena.Field.PvpZone.Ended, Is.False);
        arena.Field.PvpZone.Update(now + 10 + Maple2.Model.Game.Pvp.RedArenaState.PrepareMs);
        Assert.That(arena.Field.PlayerHostile(arena.Player(0), arena.Player(1)), Is.True);
    }

    private static List<string> Moves(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.UserMoveByPortal).Select(Convert.ToHexString).ToList();

    // The Red Arena's fighters are sent onto the stage even where the navmesh finds no floor there (65000001's stage
    // portal: no move and no log in a live run): the move goes as is.
    [Test]
    public void RedArena_FightersMovedOntoTheStage() {
        Arena arena = Start(field => { });
        long now = Environment.TickCount64;
        var zone = new RedArenaZoneField(arena.Field, 102, 0, 120, 0, () => arena.Field.Players.Values.Take(2), null, now,
            stage: (new System.Numerics.Vector3(825, -675, 2875), default));
        typeof(FieldManager).GetProperty(nameof(FieldManager.PvpZone))!.SetValue(arena.Field, zone);
        foreach (TestGameSession test in arena.Tests) {
            test.TakeSent();
        }
        zone.Update(now + 10);
        Assert.That(Moves(arena.Tests[0]).Count + Moves(arena.Tests[1]).Count, Is.EqualTo(2));
    }

    // The stage spot keeps the portal's ground position at the stage box's centre height: 65000001's portal 1 (z 2875)
    // lies under the stage floor (3300); box 102's centre (3350) is just above it.
    [Test]
    public void RedArena_StageSpotAboveTheFloor() {
        var box = new Maple2.Model.Metadata.Ms2TriggerBox(new System.Numerics.Vector3(825, -675, 3350), new System.Numerics.Vector3(1950, 1950, 1800), 102, true);
        Assert.That(FieldManager.StageSpotOf(box, new System.Numerics.Vector3(825, -675, 2875), default).Position,
            Is.EqualTo(new System.Numerics.Vector3(825, -675, 3350)));
        Assert.That(FieldManager.StageSpotOf(box, null, default).Position, Is.EqualTo(box.Position));
    }

    // A free-for-all whose players wait outside the zone (65000002: the platform, box 102, above the mine) sends each
    // participant still there down to the floor around the map's floor portal when the fight starts; one already out
    // stays where it is.
    [Test]
    public void FreeForAll_WaitingPlayersGoDownToTheFloor() {
        Arena arena = Start(field => { });
        var platform = new TriggerBox(new Maple2.Model.Metadata.Ms2TriggerBox(new System.Numerics.Vector3(75, 75, 7350),
            new System.Numerics.Vector3(1200, 1200, 900), 102, true));
        arena.Player(0).Position = new System.Numerics.Vector3(75, 75, 7500);
        arena.Player(1).Position = new System.Numerics.Vector3(2000, 0, 4500);
        long now = Environment.TickCount64;
        var zone = new PvpZoneField(arena.Field, 101, 0, 300, 0, () => arena.Field.Players.Values.Take(2), null, now,
            waiting: (platform, new System.Numerics.Vector3(825, 75, 4500)));
        typeof(FieldManager).GetProperty(nameof(FieldManager.PvpZone))!.SetValue(arena.Field, zone);
        foreach (TestGameSession test in arena.Tests) {
            test.TakeSent();
        }
        zone.Update(now + 10);
        Assert.That(Moves(arena.Tests[0]).Count, Is.EqualTo(1));
        Assert.That(Moves(arena.Tests[1]), Is.Empty);
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
