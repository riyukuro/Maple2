using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The client sends an InteractObject end only when the cast of the row's reactTime it starts with the start packet
// is over (stateinteractdata.cpp:283-293, 378, 470-483); a gather's cast is cut by a running MasteryReduce event
// (interactobject.cpp:1495, gameeventvalue.cpp:644). The server logs an end that comes too soon, and does not refuse it.
public class InteractCastTimeTests {
    private const int RecipeId = 77;
    private const int RewardItemId = 30000001;

    private TestGameSession test = null!;
    private readonly InteractObjectHandler handler = new();
    private readonly List<string> lines = [];

    [SetUp]
    public void Capture() {
        lines.Clear();
        ILogger logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(new Lines(lines)).CreateLogger();
        typeof(PacketHandler<GameSession>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(handler, logger);
    }

    private static InteractObjectMetadata Row(int id, InteractType type, int react, int recipeId = 0) {
        return new InteractObjectMetadata(id, type, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0, WeaponItemId: 0,
            new InteractObjectMetadataItem(0, 0, 0, 0, recipeId), new InteractObjectMetadataTime(0, react, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);
    }

    private void Send(GameSession session, byte command, string name) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteInt(1);
            writer.WriteString(name);
        }));
    }

    private List<string> Early() => lines.Where(line => line.Contains("before its cast")).ToList();

    [Test]
    public void CastTime_IsTheReactTime_CutForAGatherByMasteryReduce() {
        Assert.Multiple(() => {
            Assert.That(InteractRules.CastTime(1000, false, 0), Is.EqualTo(1000));
            Assert.That(InteractRules.CastTime(3000, true, 0), Is.EqualTo(3000));
            Assert.That(InteractRules.CastTime(3000, true, 30000), Is.EqualTo(1000));
            Assert.That(InteractRules.CastTime(3000, false, 30000), Is.EqualTo(3000), "not a gather");
            Assert.That(InteractRules.CastTime(0, true, 30000), Is.EqualTo(0));
        });
    }

    [Test]
    public void EndTiming_SlackAndNoStart() {
        Assert.Multiple(() => {
            Assert.That(InteractRules.EndTiming(null, 5000, 3000), Is.EqualTo(InteractRules.EarlyEnd.NoStart));
            Assert.That(InteractRules.EndTiming(null, 5000, 500), Is.EqualTo(InteractRules.EarlyEnd.None), "a cast of 500 ms or less is not tested");
            Assert.That(InteractRules.EndTiming(5000, 5000, 500), Is.EqualTo(InteractRules.EarlyEnd.None));
            Assert.That(InteractRules.EndTiming(1000, 3000, 3000), Is.EqualTo(InteractRules.EarlyEnd.Soon));
            Assert.That(InteractRules.EndTiming(1000, 3500, 3000), Is.EqualTo(InteractRules.EarlyEnd.None), "within the slack");
            Assert.That(InteractRules.EndTiming(1000, 9000, 3000), Is.EqualTo(InteractRules.EarlyEnd.None));
        });
    }

    // An end right after the start of a 3 s cast is logged once; the end still applies.
    [Test]
    public void AnEndRightAfterTheStart_IsLoggedOnly() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.AddInteract(Row(11, InteractType.Mesh, 3000), new InteractMeshObject("ab", new Ms2InteractMesh(11, Vector3.Zero, Vector3.Zero)));
        Send(session, 11, "ab");
        test.TakeSent();

        Send(session, 12, "ab");

        Assert.That(Early(), Has.Count.EqualTo(1));
        Assert.That(Early()[0], Does.Contain("Soon"));
        Assert.That(test.Errors, Is.Empty);
        Assert.That(test.TakeSent(), Is.Not.Empty, "the end is answered");
    }

    [Test]
    public void AnEndAfterTheCast_IsNotLogged() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.AddInteract(Row(11, InteractType.Mesh, 3000), new InteractMeshObject("ab", new Ms2InteractMesh(11, Vector3.Zero, Vector3.Zero)));
        Send(session, 11, "ab");
        session.InteractStartTick = session.Field!.FieldTick - 4000;

        Send(session, 12, "ab");

        Assert.That(Early(), Is.Empty);
    }

    [Test]
    public void AnEndWithoutAStart_IsLoggedAsNoStart() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.AddInteract(Row(11, InteractType.Mesh, 3000), new InteractMeshObject("ab", new Ms2InteractMesh(11, Vector3.Zero, Vector3.Zero)));

        Send(session, 12, "ab");

        Assert.That(Early(), Has.Count.EqualTo(1));
        Assert.That(Early()[0], Does.Contain("NoStart"));
    }

    // A 3 s gather under a MasteryReduce of 30000 casts 1 s: an end 800 ms after the start is in the slack.
    [TestCase(30000, 0)]
    [TestCase(0, 1)]
    public void AGather_UsesTheMasteryReduceCast(int rate, int logged) {
        GameSession session = StartGather(rate);
        Send(session, 11, "ab");
        session.InteractStartTick = session.Field!.FieldTick - 800;

        Send(session, 12, "ab");

        Assert.That(Early(), Has.Count.EqualTo(logged));
    }

    // A session with a 3 s gathered object "ab" and a MasteryReduce event 520001 of the rate (none for 0).
    private GameSession StartGather(int rate) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(RewardItemId));
        test.SetTable(new MasteryRecipeTable(new Dictionary<int, MasteryRecipeTable.Entry> {
            [RecipeId] = new(RecipeId, MasteryType.Mining, NoRewardExp: false, RequiredMastery: 0, RequiredMeso: 0, RequiredQuests: [], RequiredLevel: 0, RewardExp: 0,
                RewardMastery: 10, HighRateLimitCount: 5, NormalRateLimitCount: 10, RequiredItems: [], HabitatMapId: [],
                RewardItems: [new ItemComponent(RewardItemId, 1, 1, ItemTag.None)]),
        }));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> { [ExpType.gathering] = new(1, 1f) }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, _ => 100L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        test.SetTable(new MasteryDifferentialFactorTable(new Dictionary<int, MasteryDifferentialFactorTable.Entry> { [0] = new(0, 10000) }));
        if (rate > 0) {
            test.AddEvent(520001, GameEventType.MasteryReduce, new EventRate(rate));
        }
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Drop = new MapMetadataDrop(1, 0, []) });
        test.SetGatheringCounts();
        test.AddInteract(Row(13, InteractType.Gathering, 3000, RecipeId), new InteractGatheringObject("ab", new Ms2InteractActor(13, Vector3.Zero, Vector3.Zero)));
        return session;
    }

    // The event is over: the server no longer finds it.
    private static void EndEvent(GameSession session, int id) {
        object server = typeof(GameSession).GetField("server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var cache = (IDictionary<int, GameEvent>) typeof(Maple2.Server.Game.GameServer).GetField("eventCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
        cache.Remove(id);
    }

    // The client takes the MasteryReduce rate as the cast starts: a gather started under a 30000 rate casts 1 s even
    // if the event is over by the end.
    [Test]
    public void AGather_KeepsTheRateOfItsStart() {
        GameSession session = StartGather(rate: 30000);
        Send(session, 11, "ab");
        Assert.That(session.InteractStartMasteryReduce, Is.EqualTo(30000));
        session.InteractStartTick = session.Field!.FieldTick - 800;
        EndEvent(session, 520001);
        Assert.That(session.FindEvent(GameEventType.MasteryReduce), Is.Empty, "the event is over");

        Send(session, 12, "ab");

        Assert.That(Early(), Is.Empty);
    }

    // An object the client did not find usable gets no start, but the client casts on it all the same and sends the
    // end if it turned usable meanwhile: no line for an object that turned reactable after the last start.
    [Test]
    public void AnObjectTurnedReactableAfterTheStart_IsNotLogged() {
        test = new TestGameSession();
        GameSession session = test.Start();
        FieldInteract interact = test.AddInteract(Row(11, InteractType.Mesh, 3000), new InteractMeshObject("ab", new Ms2InteractMesh(11, Vector3.Zero, Vector3.Zero)));
        session.InteractStartTick = session.Field!.FieldTick - 100;
        interact.SetState(InteractState.Normal);
        interact.SetState(InteractState.Reactable);

        Send(session, 12, "ab");

        Assert.That(Early(), Is.Empty);
    }

    // One reactable since before the start is still timed.
    [Test]
    public void AnObjectReactableBeforeTheStart_IsTimed() {
        test = new TestGameSession();
        GameSession session = test.Start();
        FieldInteract interact = test.AddInteract(Row(11, InteractType.Mesh, 3000), new InteractMeshObject("ab", new Ms2InteractMesh(11, Vector3.Zero, Vector3.Zero)));
        interact.SetState(InteractState.Normal);
        interact.SetState(InteractState.Reactable);
        Send(session, 11, "ab");
        session.InteractStartTick = interact.ReactableTick + 1;

        Send(session, 12, "ab");

        Assert.That(Early(), Has.Count.EqualTo(1));
    }

    // Every start sets the tick, even one whose names are all refused; leaving the field clears the tick and the rate.
    [Test]
    public void TheStart_IsKeptForAnyStart_AndClearedOnLeavingTheField() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.AddInteract(Row(11, InteractType.Mesh, 3000), new InteractMeshObject("ab", new Ms2InteractMesh(11, Vector3.Zero, Vector3.Zero)));
        session.Player.Position = new Vector3(100000, 0, 0);

        Send(session, 11, "ab");
        Assert.That(session.InteractStarted, Is.Empty, "out of reach");
        Assert.That(session.InteractStartTick, Is.Not.Null, "a start whose names are all refused");
        session.InteractStartMasteryReduce = 30000;

        // What the leave clears that the test session is made without (FishingCastTests); the field's own removal is not under test.
        typeof(GameSession).GetField(nameof(GameSession.ItemLockStaging))!.SetValue(session, new long[18]);
        FieldInfo dismantle = typeof(GameSession).GetField(nameof(GameSession.DismantleStaging))!;
        dismantle.SetValue(session, System.Array.CreateInstance(dismantle.FieldType.GetElementType()!, 0));
        session.Field = null;
        typeof(GameSession).GetMethod("LeaveField", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, null);

        Assert.Multiple(() => {
            Assert.That(session.InteractStartTick, Is.Null);
            Assert.That(session.InteractStartMasteryReduce, Is.Zero);
        });
    }

    private sealed class Lines(List<string> lines) : ILogEventSink {
        public void Emit(LogEvent logEvent) => lines.Add(logEvent.RenderMessage());
    }
}
