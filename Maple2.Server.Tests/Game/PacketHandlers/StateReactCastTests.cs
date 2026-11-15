using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Metadata;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The client sends StateReact for a reactor with a reactTimeMSec only when the react state's cast of that time is over
// (actionkeyobject.cpp:2909-2934, statereact.cpp:157-168, 302-314), one react at a time. The server logs a react that
// comes sooner than its cast after the last one, and handles it as before.
public class StateReactCastTests {
    private const int ObjectId = 7001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private readonly StateReactHandler handler = new();
    private readonly List<string> lines = [];

    [SetUp]
    public void Capture() {
        lines.Clear();
        ILogger logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(new Lines(lines)).CreateLogger();
        typeof(PacketHandler<GameSession>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(handler, logger);
    }

    // A reactor in its sleep state where the player stands: the react is handled up to the state test and takes nothing.
    private void Start(int reactTimeMs, ReactorStateType type = ReactorStateType.Sleep) {
        test = new TestGameSession();
        test.Start();
        typeof(ConfigManager).GetField(nameof(ConfigManager.ReactLog))!.SetValue(session.Config, new LogGate());
        var metadata = new ReactorTable.Entry(10000000, 155, reactTimeMs, 0, [new ReactorTable.State(101, type, 15, 30, [])], []);
        FieldInfo field = typeof(FieldManager).GetField("fieldReactors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (field.GetValue(session.Field) == null) {
            field.SetValue(session.Field, new ConcurrentDictionary<int, FieldReactor>());
        }
        var reactors = (ConcurrentDictionary<int, FieldReactor>) field.GetValue(session.Field)!;
        reactors[ObjectId] = new FieldReactor(session.Field!, ObjectId, "a", metadata, session.Player.Position, default);
    }

    private void React(int objectId = ObjectId) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(objectId);
        }));
    }

    private List<string> Early() => lines.Where(line => line.Contains("before its cast")).ToList();

    [Test]
    public void ReactTooSoon_TheCastLessTheSlack() {
        Assert.Multiple(() => {
            Assert.That(ReactorRules.ReactTooSoon(null, 1000, 1500), Is.False, "no react before");
            Assert.That(ReactorRules.ReactTooSoon(1000, 1500, 1500), Is.True);
            Assert.That(ReactorRules.ReactTooSoon(1000, 2000, 1500), Is.False, "within the slack");
            Assert.That(ReactorRules.ReactTooSoon(1000, 1000, 500), Is.False, "a cast of 500 ms or less is not tested");
            Assert.That(ReactorRules.ReactTooSoon(1000, 1000, 0), Is.False, "no cast");
        });
    }

    [Test]
    public void TwoReactsAtOnce_LogTheSecond() {
        Start(1500);

        React();
        Assert.That(Early(), Is.Empty, "the first react has nothing to be timed against");
        React();

        Assert.That(Early(), Has.Count.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void AReactAfterItsCast_IsNotLogged() {
        Start(1500);
        React();
        session.LastReactTick = session.Field!.FieldTick - 2000;

        React();

        Assert.That(Early(), Is.Empty);
    }

    // A reactor without a cast sends at once.
    [Test]
    public void AReactorWithoutACast_IsNotTimed() {
        Start(0);

        React();
        React();

        Assert.That(Early(), Is.Empty);
    }

    // A react naming a reactor the field lacks still counts as the last react (the client cast for it).
    [Test]
    public void AReactOnAGoneReactor_StillSetsTheTick() {
        Start(1500);

        React(ObjectId + 1);
        React();

        Assert.That(Early(), Has.Count.EqualTo(1));
    }

    // A react that is logged is handled as before: a connect state still takes the player.
    [Test]
    public void ALoggedReact_IsStillHandled() {
        Start(1500, ReactorStateType.Connect);

        React(ObjectId + 1);
        React();

        Assert.That(Early(), Has.Count.EqualTo(1));
        Assert.That(Reactor().IsConnected(session.CharacterId), Is.True);
    }

    // One line per LogGate.EveryMs; the reacts in between are counted into the next line.
    [Test]
    public void EarlyReacts_AreLoggedOncePerGate() {
        Start(1500);

        React();
        React();
        React();
        React();

        Assert.That(Early(), Has.Count.EqualTo(1));
        Assert.That(session.Config.ReactLog.Count(session.Field!.FieldTick + LogGate.EveryMs), Is.EqualTo(3), "the two held back and this one");
    }

    // A new field starts with no react to time against (GameSession.LeaveField).
    [Test]
    public void LeavingTheField_ClearsTheLastReact() {
        Start(1500);
        React();
        Assert.That(session.LastReactTick, Is.Not.Null);

        typeof(GameSession).GetField(nameof(GameSession.ItemLockStaging))!.SetValue(session, new long[18]);
        FieldInfo dismantle = typeof(GameSession).GetField(nameof(GameSession.DismantleStaging))!;
        dismantle.SetValue(session, System.Array.CreateInstance(dismantle.FieldType.GetElementType()!, 0));
        session.Field = null;
        typeof(GameSession).GetMethod("LeaveField", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, null);

        Assert.That(session.LastReactTick, Is.Null);
    }

    private FieldReactor Reactor() {
        var reactors = (ConcurrentDictionary<int, FieldReactor>) typeof(FieldManager).GetField("fieldReactors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session.Field)!;
        return reactors[ObjectId];
    }

    private sealed class Lines(List<string> lines) : ILogEventSink {
        public void Emit(LogEvent logEvent) => lines.Add(logEvent.RenderMessage());
    }
}
