using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Numerics;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;

namespace Maple2.Server.Tests.Game.Pvp;

// The guild championship zone (GuildPvpZoneField, set_pvp_zone type 4) in a test field with four players, two per
// guild and no World match: the two guilds picked by their players, the round's start, the hostility between the
// guilds' fighters, the kill credit of a hit, the round's end and the forfeit of a guild whose players all left.
public class GuildPvpZoneFieldTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const long GuildA = 7001;
    private const long GuildB = 7002;
    // Stands 102 and 103, areas 112 and 113, portals 10 and 11, the arena portal 1 and 3 wins needed.
    private static readonly int[] Boxes = [102, 103, 112, 113, 10, 11, 1, 3];

    private static void Set(object target, Type type, string field, object? value) {
        (type.GetField(field, Any) ?? throw new MissingFieldException(type.Name, field)).SetValue(target, value);
    }

    private static void JoinGuild(TestGameSession test, long guildId) {
        var guild = new Guild(guildId, guildId == GuildA ? "Blue" : "Red", test.AccountId, test.CharacterId, "Master");
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        GC.SuppressFinalize(manager);
        Set(manager, typeof(GuildManager), "session", test.Session);
        Set(manager, typeof(GuildManager), "logger", new LoggerConfiguration().CreateLogger());
        Set(manager, typeof(GuildManager), "<Guild>k__BackingField", guild);
        test.Session.Guild = manager;
    }

    // Players 0 and 1 in guild A, 2 and 3 in guild B; every box lists every player.
    private static TestGameSession[] Start(out FieldManager field, bool zone = true, GuildPvpFieldMatch? match = null) {
        var first = new TestGameSession();
        first.Start();
        var tests = new List<TestGameSession> { first };
        for (int i = 0; i < 3; i++) {
            var other = new TestGameSession(first.Options);
            other.Start();
            tests.Add(other);
        }
        field = first.Session.Field!;
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc>());
        for (int i = 0; i < tests.Count; i++) {
            if (i > 0) {
                typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(tests[i].Session.Player, i + 1);
                field.Players[i + 1] = tests[i].Session.Player;
                tests[i].Session.Field = field;
            }
            JoinGuild(tests[i], i < 2 ? GuildA : GuildB);
        }
        // The stand portals (10, 11) and the arena portal (1).
        var portals = new ConcurrentDictionary<int, FieldPortal>();
        foreach (int id in new[] { 1, 10, 11 }) {
            var value = new Portal(id, 0, 0, PortalType.Event, PortalActionType.Interact, new Vector3(id * 100, 0, 0),
                Vector3.Zero, new Vector3(10, 10, 10), 0, 0, true, true, true);
            portals[id] = new FieldPortal(field, 0x1000 + id, value) { Position = value.Position };
        }
        Set(field, typeof(FieldManager), "fieldPortals", portals);
        if (match != null) {
            field.SetGuildPvp(match);
        }
        if (zone) {
            FieldManager f = field;
            f.StartPvpZone(4, 101, 0, 120, 0, Boxes, _ => f.Players.Values);
        }
        return tests.ToArray();
    }

    private static List<SendOp> Ops(TestGameSession test) => test.TakeSent().Select(TestGameSession.Op).ToList();

    [Test]
    public void Round_PicksTheGuildsAndFightersAreHostile() {
        TestGameSession[] tests = Start(out FieldManager field);
        IPvpZoneField zone = field.PvpZone!;
        foreach (TestGameSession test in tests) {
            test.TakeSent();
        }
        long now = Environment.TickCount64;
        zone.Update(now + 10);

        // The two guilds' team dialog and the round, to everyone.
        List<SendOp> ops = Ops(tests[0]);
        Assert.That(ops, Does.Contain(SendOp.Pvp));
        Assert.That(ops.Count(op => op == SendOp.TeamPvp), Is.GreaterThanOrEqualTo(1));
        FieldPlayer a0 = tests[0].Session.Player;
        FieldPlayer a1 = tests[1].Session.Player;
        FieldPlayer b0 = tests[2].Session.Player;
        Assert.That(zone.IsParticipant(a0), Is.True);
        Assert.That(zone.Hostile(a0, b0), Is.True);
        Assert.That(zone.Hostile(a0, a1), Is.False);

        // A hit of the other guild, then the death: the kill notice, the victim is out of the round.
        foreach (TestGameSession test in tests) {
            test.TakeSent();
        }
        zone.Damage(a0, b0, 40);
        zone.Death(b0);
        Assert.That(Ops(tests[1]), Does.Contain(SendOp.PlayerKillNotice));
        Assert.That(zone.IsParticipant(b0), Is.False);
        Assert.That(zone.Hostile(a0, b0), Is.False);
    }

    [Test]
    public void Round_EndsWhenAGuildHasNoFightersLeft() {
        TestGameSession[] tests = Start(out FieldManager field);
        IPvpZoneField zone = field.PvpZone!;
        long now = Environment.TickCount64;
        zone.Update(now + 10);
        foreach (TestGameSession test in tests) {
            test.TakeSent();
        }
        zone.Death(tests[2].Session.Player);
        zone.Death(tests[3].Session.Player);

        // The round went to guild A: the dialog shows its end (round packet, end tick 0).
        Assert.That(Ops(tests[0]), Does.Contain(SendOp.TeamPvp));
        Assert.That(zone.Ended, Is.False);
        Assert.That(zone.IsParticipant(tests[0].Session.Player), Is.False);
    }

    [Test]
    public void Leave_TheLastPlayerOfAGuildForfeitsTheMatch() {
        TestGameSession[] tests = Start(out FieldManager field);
        IPvpZoneField zone = field.PvpZone!;
        long now = Environment.TickCount64;
        zone.Update(now + 10);
        foreach (TestGameSession test in tests) {
            test.TakeSent();
        }
        // Guild B's players go away from the field: the last one forfeits and guild A wins; the winners get the pose.
        field.Players.TryRemove(3, out _);
        field.Players.TryRemove(4, out _);
        Set(field, typeof(FieldManager), "<FieldTick>k__BackingField", now + 20);
        zone.Leave(tests[3].Session.Player);

        Assert.That(zone.Ended, Is.True);
        List<SendOp> ops = Ops(tests[0]);
        Assert.That(ops.Count(op => op == SendOp.TeamPvp), Is.GreaterThanOrEqualTo(2));
        Assert.That(ops, Does.Contain(SendOp.Pvp));
    }

    [Test]
    public void Update_NoSecondGuildEndsTheZone() {
        TestGameSession[] tests = Start(out FieldManager field);
        IPvpZoneField zone = field.PvpZone!;
        field.Players.TryRemove(3, out _);
        field.Players.TryRemove(4, out _);
        zone.Update(Environment.TickCount64 + 10);
        Assert.That(zone.Ended, Is.True);
        // An ended zone ignores its updates, hits and arrivals.
        zone.Update(Environment.TickCount64 + 20);
        zone.Enter(tests[0].Session.Player);
        Assert.That(zone.IsParticipant(tests[0].Session.Player), Is.False);
    }

    [Test]
    public void Enter_AnArrivalSeesTheTeamsAndTheRound() {
        TestGameSession[] tests = Start(out FieldManager field);
        IPvpZoneField zone = field.PvpZone!;
        // Before the guilds are picked an arrival gets nothing.
        tests[0].TakeSent();
        zone.Enter(tests[0].Session.Player);
        Assert.That(Ops(tests[0]), Is.Empty);

        zone.Update(Environment.TickCount64 + 10);
        tests[1].TakeSent();
        zone.Enter(tests[1].Session.Player);
        List<SendOp> ops = Ops(tests[1]);
        Assert.That(ops.Count(op => op == SendOp.TeamPvp), Is.GreaterThanOrEqualTo(1));
    }

    // A World match: the forfeit ends it, the World gets the rounds and fighters, and its scores come back as each
    // player's result window (Pvp 'u'), through the field's scheduler.
    [Test]
    public void Match_EndIsSentToTheWorldAndTheResultReachesThePlayers() {
        var sent = new TaskCompletionSource<GuildRequest.Types.PvpEnd>();
        var match = new GuildPvpFieldMatch(55, GuildA, GuildB, end => {
            sent.TrySetResult(end);
            var response = new GuildResponse();
            response.PvpScores.Add(new GuildResponse.Types.PvpScore { GuildId = GuildA, Name = "Blue", Score = 1020, Delta = 20 });
            response.PvpScores.Add(new GuildResponse.Types.PvpScore { GuildId = GuildB, Name = "Red", Score = 980, Delta = -20 });
            return response;
        });
        TestGameSession[] tests = Start(out FieldManager field, match: match);
        IPvpZoneField zone = field.PvpZone!;
        Dictionary<int, SeasonDataTable.Entry> none = [];
        tests[0].SetTable(new SeasonDataTable(none, none, none, none, none, none, none, none, none));
        tests[0].SetServerTable(new ConstantsTable());
        long now = Environment.TickCount64;
        zone.Update(now + 10);
        field.Players.TryRemove(3, out _);
        field.Players.TryRemove(4, out _);
        Set(field, typeof(FieldManager), "<FieldTick>k__BackingField", now + 20);
        zone.Leave(tests[3].Session.Player);
        Assert.That(zone.Ended, Is.True);

        Assert.That(sent.Task.Wait(TimeSpan.FromSeconds(10)), Is.True);
        GuildRequest.Types.PvpEnd end = sent.Task.Result;
        Assert.That((end.MatchId, end.WinnerGuildId), Is.EqualTo((55, GuildA)));
        Assert.That(end.Rounds.Single().Fought, Is.False);

        tests[0].TakeSent();
        field.Scheduler.Start();
        for (int i = 0; i < 100 && field.Scheduler.Queued == 0; i++) {
            Thread.Sleep(20);
        }
        field.Scheduler.InvokeAll();
        Assert.That(Ops(tests[0]), Does.Contain(SendOp.Pvp));
    }
}
