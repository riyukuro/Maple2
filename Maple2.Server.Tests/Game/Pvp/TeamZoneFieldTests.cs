using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Tests.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.Pvp;

// Treasure Island's team zone (TeamZoneField, set_pvp_zone type 3) in a test field with four players: the deal into two
// teams, the kill credit of a hit, a plunder of the other team's treasure box, a flag held for the capture time, an
// arrival's catch-up, and the end with the result, the pose, the arena match's reward and the points back to 0.
public class TeamZoneFieldTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int RewardItem = 40100023;
    // Red's treasure box 1, blue's 2, then the flags 101, 102, 103.
    private static readonly int[] Boxes = [1, 2, 101, 102, 103];

    private sealed class Zone {
        public required TestGameSession[] Tests;
        public required FieldManager Field;
        public required TeamZoneField Rules;
        public required TeamZoneState State;
        public required Dictionary<int, List<FieldPlayer>> InBox;
        public required long Start;
        public required List<(IReadOnlyList<long> Ranking, int Winners)> Ended;
        public FieldPlayer Player(int index) => Tests[index].Session.Player;
        public TeamZoneEntry Entry(int index) => State.Entry(Tests[index].CharacterId)!;
        // The first two players of the given team.
        public int[] Team(int team) => Enumerable.Range(0, Tests.Length).Where(i => State.Entry(Tests[i].CharacterId)?.Team == team).ToArray();
    }

    private static Zone Start(bool arena = false, int prepareSeconds = 0, int matchSeconds = 100) {
        var first = new TestGameSession();
        first.Start();
        var tests = new List<TestGameSession> { first };
        for (int i = 0; i < 3; i++) {
            var other = new TestGameSession(first.Options);
            other.Start();
            tests.Add(other);
        }
        foreach (TestGameSession test in tests) {
            test.SetServerTable(new ConstantsTable(pvpBtiRewardItem: RewardItem, pvpBtiRewardWinnerCount: 30, pvpBtiRewardLoserCount: 10));
            test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
            test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>>(), new Dictionary<int, long>()));
            test.Register(TestGameSession.Metadata(RewardItem, slotMax: 9999));
        }
        FieldManager field = first.Session.Field!;
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc>());
        for (int i = 1; i < tests.Count; i++) {
            typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(tests[i].Session.Player, i + 1);
            field.Players[i + 1] = tests[i].Session.Player;
            tests[i].Session.Field = field;
        }
        if (arena) {
            field.StartArena(4, PvpHandlerTests.Arenas.Entries[2], tests.Select(test => test.CharacterId).ToList(), _ => { });
        }
        var inBox = new Dictionary<int, List<FieldPlayer>>();
        long start = Environment.TickCount64;
        var ended = new List<(IReadOnlyList<long> Ranking, int Winners)>();
        var rules = new TeamZoneField(field, 104, prepareSeconds, matchSeconds, 0, Boxes,
            box => box == 104 ? field.Players.Values : inBox.GetValueOrDefault(box) ?? [], (ranking, winners) => ended.Add((ranking, winners)), start,
            new Random(7));
        var state = (TeamZoneState) typeof(TeamZoneField).GetField("state", Any)!.GetValue(rules)!;
        return new Zone { Tests = tests.ToArray(), Field = field, Rules = rules, State = state, InBox = inBox, Start = start, Ended = ended };
    }

    private static List<SendOp> Ops(TestGameSession test) => test.TakeSent().Select(TestGameSession.Op).ToList();

    private static void Clear(Zone zone) {
        foreach (TestGameSession test in zone.Tests) {
            test.TakeSent();
        }
    }

    [Test]
    public void Activate_DealsTwoTeamsAndAnnouncesThem() {
        Zone zone = Start();
        Assert.That(zone.Rules.IsParticipant(zone.Player(0)), Is.False);
        zone.Rules.Update(zone.Start + 1);

        Assert.That(zone.State.Active, Is.True);
        Assert.That(zone.Team(TeamZoneState.Red), Has.Length.EqualTo(2));
        Assert.That(zone.Team(TeamZoneState.Blue), Has.Length.EqualTo(2));
        Assert.That(Ops(zone.Tests[0]).Count(op => op == SendOp.Pvp), Is.GreaterThanOrEqualTo(10));
        int red = zone.Team(TeamZoneState.Red)[0];
        int blue = zone.Team(TeamZoneState.Blue)[0];
        int redMate = zone.Team(TeamZoneState.Red)[1];
        Assert.That(zone.Rules.IsParticipant(zone.Player(red)), Is.True);
        Assert.That(zone.Rules.Hostile(zone.Player(red), zone.Player(blue)), Is.True);
        Assert.That(zone.Rules.Hostile(zone.Player(red), zone.Player(redMate)), Is.False);
    }

    [Test]
    public void Death_ACreditedHitIsAKillAnUnhitDeathOnlyARecord() {
        Zone zone = Start();
        zone.Rules.Update(zone.Start + 1);
        int red = zone.Team(TeamZoneState.Red)[0];
        int blue = zone.Team(TeamZoneState.Blue)[0];
        int blue2 = zone.Team(TeamZoneState.Blue)[1];
        Clear(zone);

        // A hit within the kill credit, then the death: the killer's kill and the team's point, the kill notice.
        zone.Rules.Damage(zone.Player(red), zone.Player(blue), 10);
        zone.Rules.Death(zone.Player(blue));
        Assert.That(zone.Entry(red).Kill, Is.EqualTo(1));
        Assert.That(zone.Entry(blue).Death, Is.EqualTo(1));
        Assert.That(zone.State.RedScore, Is.EqualTo(TeamZoneState.KillScore));
        Assert.That(Ops(zone.Tests[0]), Does.Contain(SendOp.PlayerKillNotice));

        // No hit before the death: a death record and no point; a hit by a team mate is no hit either.
        zone.Rules.Damage(zone.Player(blue2), zone.Player(blue), 10);
        zone.Rules.Death(zone.Player(blue));
        Assert.That(zone.Entry(blue).Death, Is.EqualTo(2));
        Assert.That(zone.State.RedScore, Is.EqualTo(TeamZoneState.KillScore));
        Assert.That(zone.Entry(blue2).Kill, Is.Zero);
    }

    [Test]
    public void Update_PlunderAndFlagScore() {
        Zone zone = Start();
        zone.Rules.Update(zone.Start + 1);
        int red = zone.Team(TeamZoneState.Red)[0];
        int blue = zone.Team(TeamZoneState.Blue)[0];
        Clear(zone);

        // A red player in blue's treasure box plunders it; a red player holding flag 101 alone captures it.
        zone.InBox[2] = [zone.Player(red)];
        zone.InBox[101] = [zone.Player(red)];
        zone.Rules.Update(zone.Start + 1000);
        Assert.That(zone.State.RedScore, Is.EqualTo(TeamZoneState.PlunderScore));
        zone.Rules.Update(zone.Start + 1000 + TeamZoneState.CaptureMs);
        Assert.That(zone.State.FlagOwner(0), Is.EqualTo(TeamZoneState.Red));
        // The packets: score, gauge, the plunder and the flag (all Pvp).
        Assert.That(Ops(zone.Tests[blue]).Count(op => op == SendOp.Pvp), Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void Enter_AnArrivalGetsTheTeamsScoreAndGauge() {
        Zone zone = Start();
        // Before the fight starts an arrival gets nothing.
        Clear(zone);
        zone.Rules.Enter(zone.Player(0));
        Assert.That(Ops(zone.Tests[0]), Is.Empty);

        zone.Rules.Update(zone.Start + 1);
        zone.Rules.Leave(zone.Player(0));
        Assert.That(zone.Entry(0).Here, Is.False);
        Clear(zone);
        zone.Rules.Enter(zone.Player(0));
        Assert.That(zone.Entry(0).Here, Is.True);
        Assert.That(Ops(zone.Tests[0]).Count(op => op == SendOp.Pvp), Is.GreaterThanOrEqualTo(10));
        Assert.That(Ops(zone.Tests[1]), Has.Count.EqualTo(0).Or.Some.EqualTo(SendOp.Pvp));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void End_SendsTheResultAndPaysTheArenaMatchReward(bool arena) {
        Zone zone = Start(arena, matchSeconds: 10);
        zone.Rules.Update(zone.Start + 1);
        int red = zone.Team(TeamZoneState.Red)[0];
        int blue = zone.Team(TeamZoneState.Blue)[0];
        // Red leads with a kill.
        zone.Rules.Damage(zone.Player(red), zone.Player(blue), 10);
        zone.Rules.Death(zone.Player(blue));
        Clear(zone);

        Set(zone.Field, "<FieldTick>k__BackingField", zone.Start + 20_000);
        zone.Rules.Update(zone.Start + 11_000);

        Assert.That(zone.Rules.Ended, Is.True);
        Assert.That(zone.Ended, Has.Count.EqualTo(1));
        Assert.That(zone.Ended[0].Winners, Is.EqualTo(2));
        Assert.That(zone.Ended[0].Ranking.Take(2), Is.EquivalentTo(zone.Team(TeamZoneState.Red).Select(i => zone.Tests[i].CharacterId)));
        Assert.That(Ops(zone.Tests[red]).Count(op => op == SendOp.Pvp), Is.GreaterThanOrEqualTo(3));
        // Only an arena match's field pays: 30 to the winners, 10 to the others.
        Assert.That(zone.Tests[red].Session.Item.Inventory.Find(RewardItem).Sum(item => item.Amount), Is.EqualTo(arena ? 30 : 0));
        Assert.That(zone.Tests[blue].Session.Item.Inventory.Find(RewardItem).Sum(item => item.Amount), Is.EqualTo(arena ? 10 : 0));
    }

    private static void Set(object target, string field, object? value) {
        target.GetType().GetField(field, Any)!.SetValue(target, value);
    }
}
