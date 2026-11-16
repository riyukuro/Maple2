using System.Collections.Generic;
using System.Linq;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maple2.Server.Tests.Game;

// A trigger's emotion sequence on an npc: a name the npc's rig has plays; one it lacks (KR 52000119_qd names "Talk" for
// Ralph 11003187, whose rig has Talk_A) plays nothing and logs at debug level, not as an error.
public class NpcAnimateTests : NpcCastFixture {
    private static readonly string[] Rig = ["Idle_A", "Talk_A"];

    private sealed class Sink : ILogEventSink {
        public readonly List<LogEvent> Events = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private Sink Capture() {
        var sink = new Sink();
        typeof(Maple2.Server.Game.Model.Actor<Maple2.Model.Game.Npc>).GetField("Logger", Any)!.SetValue(npc,
            new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger());
        return sink;
    }

    [Test]
    public void Animate_KnownSequence_Plays() {
        StartCaster(Rig);
        Sink log = Capture();

        npc.Animate("Talk_A");
        npc.TaskState.Update(Field.FieldTick);

        Assert.Multiple(() => {
            Assert.That(npc.Animation.PlayingSequence?.Name, Is.EqualTo("Talk_A"));
            Assert.That(log.Events.Where(e => e.Level >= LogEventLevel.Warning), Is.Empty);
        });
    }

    [Test]
    public void Animate_UnknownSequence_LogsAtDebugOnly() {
        StartCaster(Rig);
        Sink log = Capture();

        npc.Animate("Talk");

        Assert.Multiple(() => {
            Assert.That(log.Events.Where(e => e.Level >= LogEventLevel.Warning), Is.Empty);
            Assert.That(log.Events.Select(e => e.Level), Is.EqualTo(new[] { LogEventLevel.Debug }));
            Assert.That(log.Events.Single().Properties["Sequence"].ToString(), Is.EqualTo("\"Talk\""));
        });
    }

    // A list with an unknown name plays the known ones and notes the missing one at debug level.
    [Test]
    public void AnimateSequence_ListWithAnUnknownName_NotesItAtDebugOnly() {
        StartCaster(Rig);
        Sink log = Capture();

        npc.AnimateSequence("Talk_A,Talk");

        Assert.Multiple(() => {
            Assert.That(log.Events.Where(e => e.Level >= LogEventLevel.Warning), Is.Empty);
            Assert.That(log.Events.Single().Level, Is.EqualTo(LogEventLevel.Debug));
            Assert.That(log.Events.Single().Properties["Sequence"].ToString(), Is.EqualTo("\"Talk\""));
        });
    }
}
