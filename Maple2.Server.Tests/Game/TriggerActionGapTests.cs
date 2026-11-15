using System.Linq;
using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;

namespace Maple2.Server.Tests.Game;

// Trigger actions whose own lines no other test ran, driven through the script mapping.
public class TriggerActionGapTests : TriggerTestBase {
    private static readonly Ms2TriggerBox Near = new(Vector3.Zero, new Vector3(300, 300, 300), 1, true);

    private void StartWithBox() {
        Start(Near);
        test.Session.Player.Position = Vector3.Zero;
        context.Events.Start();
    }

    // The queue's delayed steps run one stage per call: a step scheduled while one runs waits for the next call.
    private void Run(int stages) {
        for (int i = 0; i < stages; i++) {
            context.Events.InvokeAll();
        }
    }

    // random_additional_effect: the client shows the effect over every candidate (Trigger command 23), then over the
    // chosen ones (24), and the buff lands before the effect stops (25).
    [Test]
    public void RandomAdditionalEffect_CandidatesThenChosenThenStop() {
        StartWithBox();
        WithEffect(70000107);

        Action("""<action name="random_additional_effect" target="player" box_id="1" target_count="1" tick="0" wait_tick="0" target_effect="Eff_Pick" additional_effect_id="70000107" />""");
        System.Collections.Generic.List<ByteReader> first = Sent(SendOp.Trigger);
        Assert.That(first, Has.Count.EqualTo(1));
        Assert.That((first[0].ReadByte(), first[0].ReadInt(), first[0].ReadInt(), first[0].ReadString()),
            Is.EqualTo(((byte) 23, 1, test.Session.Player.ObjectId, "Eff_Pick")));

        Run(1);
        ByteReader chosen = Sent(SendOp.Trigger).Single();
        Assert.That((chosen.ReadByte(), chosen.ReadInt(), chosen.ReadInt(), chosen.ReadString()),
            Is.EqualTo(((byte) 24, 1, test.Session.Player.ObjectId, "Eff_Pick")));

        Run(1);
        Assert.That(Sent(SendOp.Trigger).Select(reader => reader.ReadByte()), Does.Contain((byte) 25));
        Assert.That(test.Session.Player.Buffs.HasBuff(70000107), Is.True);
    }

    // No player in the box, or a target count of 0: nothing is shown or queued.
    [Test]
    public void RandomAdditionalEffect_NoCandidatesSendsNothing() {
        StartWithBox();

        Action("""<action name="random_additional_effect" target="player" box_id="9" target_count="1" tick="0" target_effect="E" />""");
        Action("""<action name="random_additional_effect" target="player" box_id="1" target_count="0" tick="0" target_effect="E" />""");

        Assert.That(Sent(SendOp.Trigger), Is.Empty);
        Assert.That(context.Events.Count, Is.Zero);
    }

    // set_cinematic_ui (client UICutSceneCinematic::OnPacket, uicutscenecinematic.cpp:305): type 2 hides the script
    // (command 2); 3-6 start a cinematic view with the transition as the int id (OnPacketStartCinematic reads an
    // int, a text and an unused text); 9 is the opening (command 11: text, bool).
    [TestCase(2, "", false, "hide")]
    [TestCase(3, "$a$", false, "view3")]
    [TestCase(4, "$b$", false, "view4")]
    [TestCase(5, "$c$", false, "view5")]
    [TestCase(6, "$d$", false, "view6")]
    [TestCase(9, "$e$", true, "opening")]
    public void SetCinematicUi_PacketPerType(int type, string script, bool flag, string expected) {
        Start();

        Action($"""<action name="set_cinematic_ui" type="{type}" script="{script}" arg3="{(flag ? 1 : 0)}" />""");

        ByteReader reader = Sent(SendOp.Cinematic).Single();
        byte command = reader.ReadByte();
        switch (expected) {
            case "hide":
                Assert.That((command, reader.Available), Is.EqualTo(((byte) 2, 0)));
                break;
            case "opening":
                Assert.That((command, reader.ReadUnicodeString(), reader.ReadBool(), reader.Available), Is.EqualTo(((byte) 11, script, true, 0)));
                break;
            default:
                Assert.That((command, reader.ReadInt(), reader.ReadUnicodeString(), reader.ReadUnicodeString(), reader.Available),
                    Is.EqualTo(((byte) 3, type, script, "", 0)));
                break;
        }
    }
}
