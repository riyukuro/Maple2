using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Widget;
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

    // The box 1 field with portal 5 at (50, 60) on a flat navmesh.
    private void StartWithPortal() {
        StartWithBox();
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        typeof(FieldManager).GetField("fieldPortals", any)!.SetValue(Field, new ConcurrentDictionary<int, FieldPortal>());
        Field.SpawnPortal(new Portal(5, 0, 0, PortalType.Field, PortalActionType.Touch, new Vector3(50, 60, 0), Vector3.Zero,
            new Vector3(100, 100, 100), 0, 0, false, false, false));
        typeof(FieldManager).GetField("<Navigation>k__BackingField", any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", any)!.SetValue(test.Session.Player, Field);
    }

    // move_user to the field's own map (client: the portal's place on the same map): the players in the box get the
    // by-portal move to the portal's position; players outside the box do not.
    [Test]
    public void MoveUser_OwnMapMovesTheBoxToThePortal() {
        StartWithPortal();
        Action($"""<action name="move_user" map_id="{Field.MapId}" portal_id="5" box_id="2" />""");
        Assert.That(Sent(SendOp.UserMoveByPortal), Is.Empty);

        Action($"""<action name="move_user" map_id="{Field.MapId}" portal_id="5" box_id="1" />""");
        ByteReader reader = Sent(SendOp.UserMoveByPortal).Single();
        int id = reader.ReadInt();
        Vector3 at = reader.Read<Vector3>();
        // The height comes from the navmesh floor.
        Assert.That((id, at.X, at.Y), Is.EqualTo((test.Session.Player.ObjectId, 50f, 60f)));
    }

    // move_random_user with map 0 (or the field's own map) and a portal: the picked players of the box get the
    // by-portal move inside the field, no field change; players in another box are not picked.
    [TestCase(0)]
    [TestCase(-1)]
    public void MoveRandomUser_MapZeroOrOwnMovesToThePortal(int mapId) {
        StartWithPortal();
        string map = mapId == 0 ? "0" : Field.MapId.ToString();

        Action($"""<action name="move_random_user" map_id="{map}" portal_id="5" box_id="2" count="3" />""");
        Assert.That(test.TakeSentOps(), Is.Empty);

        Action($"""<action name="move_random_user" map_id="{map}" portal_id="5" box_id="1" count="3" />""");
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.UserMoveByPortal }));
    }

    // create_widget: the script's widget type picks the widget class the later widget actions find under that name;
    // a type the server has no class for gets a plain widget, so the script's actions on it do not fail.
    [TestCase("OxQuiz", typeof(OxQuizWidget))]
    [TestCase("OxQuizUGC", typeof(OxQuizUgcWidget))]
    [TestCase("ReverseRaidPortal", typeof(ReverseRaidPortalWidget))]
    [TestCase("SceneMovie", typeof(SceneMovieWidget))]
    [TestCase("Guide", typeof(GuideWidget))]
    [TestCase("SurvivalContents", typeof(SurvivalContentsWidget))]
    [TestCase("TypingGame", typeof(TypingGameWidget))]
    [TestCase("Round", typeof(RoundWidget))]
    [TestCase("RainbowMonster", typeof(RainbowMonsterWidget))]
    [TestCase("SomethingElse", typeof(Widget))]
    public void CreateWidget_ByType(string type, System.Type expected) {
        Start();
        typeof(FieldManager).GetField(nameof(FieldManager.Widgets))!.SetValue(Field, new System.Collections.Generic.Dictionary<string, Widget>());

        Action($"""<action name="create_widget" type="{type}" />""");

        Assert.That(Field.Widgets[type].GetType(), Is.EqualTo(expected));
    }
}
