using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A ride an npc sells through its job script (jobConditionTable mesos with moveMapId: KR 11000016 for 100 mesos,
// 11001056 for 5000): the fare is kept only when the field change went, as a taxi's. The change leaves as a move to
// another server (TestGameSession.EnterMovingOut) unless a test refuses it. The npc and field ids are made up.
public class NpcTransportFareTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int NpcId = 1001;
    private const int From = 2000917, To = 2000918;
    private const int Fare = 4000;

    public enum Outcome { Went, WorldRefused, RefusedHere }

    private static MapMetadata Field(int id, bool notInPlayedClient = false) {
        return TestGameSession.Map(id, notInPlayedClient: notInPlayedClient) with {
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], false, false, false, false, false, false, false),
        };
    }

    // What the ride took of `meso`.
    private static long Ride(long meso, Outcome outcome) {
        var test = new TestGameSession();
        MapMetadata from = Field(From);
        test.AddMap(from, Field(To, notInPlayedClient: outcome == Outcome.RefusedHere));
        GameSession session = test.Start();
        if (outcome == Outcome.RefusedHere) {
            test.EnterMap(from);
        } else {
            test.EnterMovingOut(from);
            if (outcome == Outcome.WorldRefused) {
                test.World.MoveAddress = "";
            }
        }
        session.Currency.Meso = meso;

        var state = new ScriptState(1, ScriptStateType.Job, false, null, [new CinematicContent("", NpcTalkButton.Close, 1, [], [])]);
        var script = new ScriptMetadata(NpcId, ScriptType.Npc, new Dictionary<int, ScriptState> { [1] = state });
        session.NpcScript = new NpcScriptManager(session, null, script, state, NpcTalkType.Dialog) {
            JobCondition = new JobConditionMetadata(NpcId, 0, 0, 0, JobCode.None, false, 0, 0, 0, 0, Fare, 0, false, false, false, 0, false,
                JobCode.None, 0, To, false, 50),
        };
        typeof(NpcScriptManager).GetMethod("PerformJobScript", Any)!.Invoke(session.NpcScript, null);
        return meso - session.Currency.Meso;
    }

    [Test]
    public void ARideThatWent_TakesTheFare() {
        Assert.That(Ride(10000, Outcome.Went), Is.EqualTo(Fare));
    }

    // A ride whose field change is refused (here: the field is not made; or by the World) gives the fare back. Before, the
    // fare was taken for good ahead of the change.
    [TestCase(Outcome.WorldRefused)]
    [TestCase(Outcome.RefusedHere)]
    public void ARefusedRide_KeepsTheFare(Outcome outcome) {
        Assert.That(Ride(10000, outcome), Is.Zero);
    }

    [Test]
    public void ATalkerShortOfTheFare_DoesNotRide() {
        Assert.That(Ride(Fare - 1, Outcome.Went), Is.Zero);
    }

    // A ride sold by a script function (KR 11000994 script 64: one 20000087 for the move to 2000183, nothing else
    // given): the price, items and mesos, comes back when the field change is refused. Returns the mesos and tickets
    // held after the talk, and the tickets stored.
    private const int Ticket = 30000193;

    private static (long Meso, int Tickets, int StoredTickets) RideByFunction(Outcome outcome) {
        var test = new TestGameSession();
        MapMetadata from = Field(From);
        test.AddMap(from, Field(To, notInPlayedClient: outcome == Outcome.RefusedHere));
        test.Store(TestGameSession.Metadata(Ticket), 1);
        var function = new ScriptFunctionMetadata(NpcId, 64, ScriptType.Npc, 1, false, 0, "", "", "", To, 50, "", "", [],
            [new ItemComponent(Ticket, -1, 1, ItemTag.None)], 0, "", "", "", 0, 1000, 0, 0, false);
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> {
            [NpcId] = new() { [64] = new() { [1] = function } },
        }));
        GameSession session = test.Start();
        if (outcome == Outcome.RefusedHere) {
            test.EnterMap(from);
        } else {
            test.EnterMovingOut(from);
            if (outcome == Outcome.WorldRefused) {
                test.World.MoveAddress = "";
            }
        }
        session.Currency.Meso = 10000;

        var state = new ScriptState(64, ScriptStateType.Script, false, null, [new CinematicContent("", NpcTalkButton.Close, 1, [], [])]);
        var script = new ScriptMetadata(NpcId, ScriptType.Npc, new Dictionary<int, ScriptState> { [64] = state });
        session.NpcScript = new NpcScriptManager(session, null, script, state, NpcTalkType.Talk);
        session.NpcScript.ProcessScriptFunction();

        int tickets = session.Item.Inventory.Find(Ticket).Sum(item => item.Amount);
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        int stored = context.Item.Where(row => row.ItemId == Ticket && row.OwnerId == test.CharacterId).AsEnumerable().Sum(row => row.Amount);
        return (session.Currency.Meso, tickets, stored);
    }

    [Test]
    public void AFunctionsRideThatWent_TakesThePrice() {
        (long meso, int tickets, int stored) = RideByFunction(Outcome.Went);
        Assert.Multiple(() => {
            Assert.That(meso, Is.EqualTo(9000));
            Assert.That(tickets, Is.Zero);
            Assert.That(stored, Is.Zero);
        });
    }

    // Before, the price stayed taken, stored at once, with no ride.
    [TestCase(Outcome.WorldRefused)]
    [TestCase(Outcome.RefusedHere)]
    public void AFunctionsRefusedRide_GivesThePriceBack(Outcome outcome) {
        (long meso, int tickets, int stored) = RideByFunction(outcome);
        Assert.Multiple(() => {
            Assert.That(meso, Is.EqualTo(10000));
            Assert.That(tickets, Is.EqualTo(1));
            Assert.That(stored, Is.EqualTo(1));
        });
    }
}
