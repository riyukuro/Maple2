using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;
using ParserTargetState = Maple2.File.Parser.Enum.ConditionTargetState;

namespace Maple2.Server.Tests.Game.Util;

// The AI state condition reads the other actor of a hold or a grab from the target's state sync.
public class AiTargetStateTests {
    private const int Npc = 0x2000_0042;

    [Test]
    public void HoldNamesTheHeldActor() {
        var sync = new StateSync { State = ActorState.Hold, Flags = StateSync.Flag.Flag6, Flag6Unknown1 = Npc, Flag1Unknown = 7 };
        Assert.That(AiTargetState.ActorOf(sync), Is.EqualTo(Npc));
    }

    [Test]
    public void GrabNamesTheGrabber() {
        var sync = new StateSync { State = ActorState.GrabTarget, Flags = StateSync.Flag.Flag1 | StateSync.Flag.Animation, Flag1Unknown = Npc, Flag6Unknown1 = 7 };
        Assert.That(AiTargetState.ActorOf(sync), Is.EqualTo(Npc));
    }

    // A field the flags do not send, or another state, names no actor.
    [Test]
    public void NoActor() {
        Assert.Multiple(() => {
            Assert.That(AiTargetState.ActorOf(new StateSync { State = ActorState.Hold, Flag6Unknown1 = Npc }), Is.Zero);
            Assert.That(AiTargetState.ActorOf(new StateSync { State = ActorState.GrabTarget, Flag1Unknown = Npc }), Is.Zero);
            Assert.That(AiTargetState.ActorOf(new StateSync { State = ActorState.Idle, Flags = StateSync.Flag.Flag1 | StateSync.Flag.Flag6, Flag1Unknown = Npc, Flag6Unknown1 = Npc }), Is.Zero);
        });
    }

    [TestCase(AiConditionTargetState.HoldMe, ActorState.Hold, Npc, true)]
    [TestCase(AiConditionTargetState.HoldMe, ActorState.Hold, Npc + 1, false)]
    [TestCase(AiConditionTargetState.HoldMe, ActorState.GrabTarget, Npc, false)]
    [TestCase(AiConditionTargetState.GrabTarget, ActorState.GrabTarget, Npc, true)]
    [TestCase(AiConditionTargetState.GrabTarget, ActorState.GrabTarget, Npc + 1, false)]
    [TestCase(AiConditionTargetState.GrabTarget, ActorState.Hold, Npc, false)]
    [TestCase(AiConditionTargetState.HoldMe, ActorState.Idle, Npc, false)]
    public void Passes(AiConditionTargetState condition, ActorState state, int stateActor, bool expected) {
        Assert.That(AiTargetState.Passes(condition, state, stateActor, Npc), Is.EqualTo(expected));
    }

    [Test]
    public void NoNpcIdNeverPasses() {
        Assert.That(AiTargetState.Passes(AiConditionTargetState.HoldMe, ActorState.Hold, 0, 0), Is.False);
    }

    // The ingest casts the parser's value; the names must line up.
    [Test]
    public void ParserEnumOrder() {
        Assert.Multiple(() => {
            Assert.That((int) ParserTargetState.holdme, Is.EqualTo((int) AiConditionTargetState.HoldMe));
            Assert.That((int) ParserTargetState.grabTarget, Is.EqualTo((int) AiConditionTargetState.GrabTarget));
        });
    }

    // A hold record as the client writes it (StateSyncObject: field 5 is an int and a string) reads back the held actor.
    [Test]
    public void HoldRecordFromTheWire() {
        var source = new StateSync { State = ActorState.Hold, Flags = StateSync.Flag.Flag6, Flag6Unknown1 = Npc, Flag6Unknown2 = "x" };
        var writer = new ByteWriter();
        source.WriteTo(writer);
        var read = new StateSync();
        read.ReadFrom(new ByteReader(writer.Buffer, 0));
        Assert.That(AiTargetState.ActorOf(read), Is.EqualTo(Npc));
    }
}
