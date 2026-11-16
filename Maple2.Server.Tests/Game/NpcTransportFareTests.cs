using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
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
}
