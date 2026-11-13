using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// SkillDamage 7: when an npc that holds a player in its grab (state 43) dies, the field is told to let the player go.
public class GrabReleaseTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Grabber = 4000;
    private TestGameSession test = null!;
    private FieldPlayer player => test.Session.Player;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        test.TakeSent();
    }

    private void Hold(ActorState state, int actorId) {
        typeof(FieldPlayer).GetField("state", Any)!.SetValue(player, state);
        typeof(FieldPlayer).GetField("<StateActorId>k__BackingField", Any)!.SetValue(player, actorId);
    }

    private List<byte[]> SkillDamages() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.SkillDamage).ToList();

    [Test]
    public void HeldPlayerIsReleased() {
        Hold(ActorState.GrabTarget, Grabber);
        GrabReleaseRules.OnGrabberDeath(test.Session.Field!, Grabber);

        List<byte[]> sent = SkillDamages();
        Assert.That(sent, Has.Count.EqualTo(1));
        // op, mode 7, grabber, count, player.
        Assert.That(sent[0].Length, Is.EqualTo(2 + 1 + 4 + 4 + 4));
        Assert.That(sent[0][2], Is.EqualTo(7));
        Assert.That(BitConverter.ToInt32(sent[0], 3), Is.EqualTo(Grabber));
        Assert.That(BitConverter.ToInt32(sent[0], 7), Is.EqualTo(1));
        Assert.That(BitConverter.ToInt32(sent[0], 11), Is.EqualTo(player.ObjectId));
        Assert.That(sent[0], Is.EqualTo(SkillDamagePacket.GrabRelease(Grabber, [player.ObjectId]).ToArray()));
    }

    // Held by another npc, or in another state that names this npc (a hold): nothing is sent.
    [TestCase(ActorState.GrabTarget, Grabber + 1)]
    [TestCase(ActorState.Hold, Grabber)]
    public void OthersAreLeftAlone(ActorState state, int actorId) {
        Hold(state, actorId);
        GrabReleaseRules.OnGrabberDeath(test.Session.Field!, Grabber);
        Assert.That(SkillDamages(), Is.Empty);
    }
}
