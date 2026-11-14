using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// Actions run from their KR script lines on a field with one player at the origin.
public class TriggerScriptLineTests : TriggerTestBase {
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private string[] SentHex() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    private static Ms2TriggerBox Box(int id, Vector3 position) => new(position, new Vector3(300, 300, 300), id, true);

    // mini_game_camera_direction (61000001_me_001 massive00): the players in the box get the local camera; a box the
    // player is out of sends nothing.
    [Test]
    public void MiniGameCameraDirection_ThePlayersInTheBox() {
        Start(Box(301, Vector3.Zero), Box(302, new Vector3(5000, 0, 0)));

        Action("""<action name="mini_game_camera_direction" box_id="302" camera_id="901" />""");
        Assert.That(SentHex(), Is.Empty);

        Action("""<action name="mini_game_camera_direction" box_id="301" camera_id="901" />""");
        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(CameraPacket.Local(901, true)) }));
    }

    // close_cinematic (02100000_bf main): the cinematic ui is hidden.
    [Test]
    public void CloseCinematic_HidesTheUi() {
        Start();

        Action("""<action name="close_cinematic" />""");
        Assert.That(SentHex(), Does.Contain(Hex(CinematicPacket.HideUi())));
    }

    // set_user_value_from_user_count (66200001_gd 10_banner_thenumberofwaiting): the key is the count of the box's
    // players of the tag; another tag counts none.
    [Test]
    public void SetUserValueFromUserCount_TheTaggedPlayersInTheBox() {
        Start(Box(9021, Vector3.Zero));
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(Field, new TriggerUserValues());
        // A team tag as GuildVsGameField gives it (team 1); the test session does not set one.
        test.Session.Player.TagId = 1;
        Field.UserValues.Set(1, "BannerNumberOfRed", 7);

        Action("""<action name="set_user_value_from_user_count" key="BannerNumberOfBlue" trigger_box_id="9021" user_tag_id="1" />""");
        Action("""<action name="set_user_value_from_user_count" key="BannerNumberOfRed" trigger_box_id="9021" user_tag_id="2" />""");
        Assert.That((Field.UserValues.Get(1, "BannerNumberOfBlue"), Field.UserValues.Get(1, "BannerNumberOfRed")), Is.EqualTo((1, 0)));
    }

    // score_board_set_score (80000014_bonus main): the open board's points become the score and are sent.
    [Test]
    public void ScoreBoardSetScore_TheOpenBoardsPoints() {
        Start();
        Field.ScoreBoardOpen = new ScoreBoard.Board(1, "", 500, 1000);

        Action("""<action name="score_board_set_score" score="0" />""");
        Assert.That((Field.ScoreBoardScore, Field.ScoreBoardOpen.Points), Is.EqualTo((0, 0)));
        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(ShadowExpeditionPacket.SetPoints(0)) }));
    }

    // set_pvp_zone (65000002_bd pvp): the free-for-all zone starts on the field.
    [Test]
    public void SetPvpZone_StartsTheZone() {
        Start(Box(101, Vector3.Zero));

        Action("""<action name="set_pvp_zone" box_id="101" prepare_time="5" match_time="300" additional_effect_id="90001002" type="2" />""");
        Assert.That(Field.PvpZone, Is.TypeOf<PvpZoneField>());
    }

    // set_quest_accept (52000187_qd tutorial): each player of the field has the quest started.
    [Test]
    public void SetQuestAccept_StartsTheQuest() {
        Start();
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.Register(TestGameSession.QuestData(90000008));

        Action("""<action name="set_quest_accept" quest_id="90000008" />""");
        Assert.That(test.Session.Quest.TryGetQuest(90000008, out Quest? quest) ? quest!.State : QuestState.None, Is.EqualTo(QuestState.Started));
    }
}
