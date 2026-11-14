using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
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

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void UserValues() {
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues), Any)!.SetValue(Field, new TriggerUserValues());
    }

    // The test field skips the FieldManager ctor, so its field property table is made here.
    private void FieldProperties() {
        typeof(FieldManager).GetField("fieldProperties", Any)!.SetValue(Field, new ConcurrentDictionary<FieldProperty, IFieldProperty>());
    }

    private FieldPortal Portal(int id, Vector3 position) {
        if (typeof(FieldManager).GetField("fieldPortals", Any)!.GetValue(Field) == null) {
            typeof(FieldManager).GetField("fieldPortals", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldPortal>());
        }
        return Field.SpawnPortal(new Portal(id, 0, 0, PortalType.Field, PortalActionType.Touch, position, Vector3.Zero,
            new Vector3(100, 100, 100), 0, 0, false, false, false));
    }

    private int Moves() => Sent(SendOp.UserMoveByPortal).Count;

    // A move checks the portal against the field's navmesh (the hall tests' flat poly, -2000 to 2000).
    private void Walkable() {
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
    }

    // add_user_value (02000384_bf): the key's value grows by the value; a key the script never set stays unset.
    [Test]
    public void AddUserValue_OnlyToASetKey() {
        Start();
        UserValues();
        Field.UserValues.Set(1, "Score", 3);

        Action("""<action name="add_user_value" key="Score" value="4" />""");
        Action("""<action name="add_user_value" key="Missing" value="4" />""");
        Assert.That((Field.UserValues.Get(1, "Score"), Field.UserValues.Contains(1, "Missing")), Is.EqualTo((7, false)));
    }

    // move_user_to_box: the players out of the box go to the portal; the ones in it stay.
    [Test]
    public void MoveUserToBox_ThePlayersOutOfTheBox() {
        Start(Box(301, Vector3.Zero), Box(302, new Vector3(5000, 0, 0)));
        Walkable();
        Portal(7, new Vector3(1500, 0, 0));
        test.TakeSent();

        Action("""<action name="move_user_to_box" box_id="301" portal_id="7" />""");
        Assert.That(Moves(), Is.EqualTo(0));
        Action("""<action name="move_user_to_box" box_id="302" portal_id="7" />""");
        Assert.That(Moves(), Is.EqualTo(1));
    }

    // kick_music_audience (performance hall): the audience in the box goes to the portal.
    [Test]
    public void KickMusicAudience_TheAudienceInTheBox() {
        Start(Box(301, Vector3.Zero));
        Walkable();
        Portal(7, new Vector3(1500, 0, 0));
        test.TakeSent();
        Action("""<action name="kick_music_audience" box_id="301" portal_id="7" />""");
        Assert.That(Moves(), Is.EqualTo(0));

        typeof(FieldManager).GetField(nameof(FieldManager.PerformanceStage), Any)!.SetValue(Field, new PerformanceStageManager(Field));
        Action("""<action name="kick_music_audience" box_id="301" portal_id="7" />""");
        Assert.That(Moves(), Is.EqualTo(1));
    }

    // set_photo_studio (84000003_wd): the field's photo studio property is sent.
    [Test]
    public void SetPhotoStudio_TheProperty() {
        Start();
        FieldProperties();

        Action("""<action name="set_photo_studio" is_enable="1" />""");
        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(FieldPropertyPacket.Add(new FieldPropertyPhotoStudio { Enabled = true })) }));
    }

    // weather: the field's weather property is sent.
    [Test]
    public void Weather_TheProperty() {
        Start();
        FieldProperties();

        Action("""<action name="weather" weather_type="Rain" />""");
        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(FieldPropertyPacket.Add(new FieldPropertyWeather { WeatherType = (WeatherType) 3 })) }));
    }

    // user_tag_symbol: the two team symbols are sent as the field property.
    [Test]
    public void UserTagSymbol_TheProperty() {
        Start();

        Action("""<action name="user_tag_symbol" symbol1="blue" symbol2="red" />""");
        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(FieldPropertyPacket.Add(new FieldPropertyUserTagSymbol { Symbol1 = "blue", Symbol2 = "red" })) }));
    }

    // notice: the script's text as a mint notice.
    [Test]
    public void Notice_TheMintNotice() {
        Start();

        Action("""<action name="notice" type="1" script="$02000001__MAIN__0$" arg3="0" />""");
        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(NoticePacket.Notice(NoticePacket.Flags.Mint, new InterfaceText("$02000001__MAIN__0$"), 3000)) }));
    }

    // set_pc_rotation: the players' rotation.
    [Test]
    public void SetPcRotation_TheRotation() {
        Start();

        Action("""<action name="set_pc_rotation" rotation="0,0,90" />""");
        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(TriggerPacket.UiPlayerRotation(new Vector3(0, 0, 90))) }));
    }

    // limit_spawn_npc_count: the field's trigger spawn limit; a negative count is no limit.
    [Test]
    public void LimitSpawnNpcCount_TheFieldsLimit() {
        Start();

        Action("""<action name="limit_spawn_npc_count" limit_count="30" desc="" />""");
        Assert.That(Field.TriggerMobLimit, Is.EqualTo(30));
        Action("""<action name="limit_spawn_npc_count" limit_count="-1" desc="" />""");
        Assert.That(Field.TriggerMobLimit, Is.EqualTo(0));
    }

    // dungeon_set_lap_time and dungeon_move_lap_time_to_now: the room clock's lap marks.
    [Test]
    public void DungeonLapTime_TheClocksMarks() {
        Start();
        Field.SetRoomTimer(RoomTimerType.Clock, 900_000, false);
        Field.RoomTimer!.Clock.Start(100_000);

        Action("""<action name="dungeon_set_lap_time" id="1" lap_time="420000" />""");
        Action("""<action name="dungeon_set_lap_time" id="2" lap_time="720000" />""");
        Assert.That(Field.RoomTimer.Clock.Laps, Is.EqualTo(new SortedDictionary<int, int> { [1] = 420000, [2] = 720000 }));
        Action("""<action name="dungeon_move_lap_time_to_now" id="1" />""");
        Assert.That(Field.RoomTimer.Clock.Laps[1], Is.Not.EqualTo(420000));
    }

    // set_quest_complete (52000187_qd tutorial): a started quest of each player is completed.
    [Test]
    public void SetQuestComplete_CompletesTheStartedQuest() {
        Start();
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.Register(TestGameSession.QuestData(90000008));
        Action("""<action name="set_quest_accept" quest_id="90000008" />""");

        Action("""<action name="set_quest_complete" quest_id="90000008" />""");
        Assert.That(test.Session.Quest.TryGetQuest(90000008, out Quest? quest) ? quest!.State : QuestState.None, Is.EqualTo(QuestState.Completed));
    }

    // set_event_ui_countdown: "0" sends the countdown to all, a box id to its players, "!id" to the players out of it;
    // a countdown without both values sends nothing.
    [Test]
    public void SetEventUiCountdown_TheBoxesPlayers() {
        Start(Box(301, Vector3.Zero), Box(302, new Vector3(5000, 0, 0)));
        string packet = Hex(MassiveEventPacket.Countdown("$x$", 2, 30));

        Action("""<action name="set_event_ui_countdown" script="$x$" round_countdown="2,30" box_ids="302" />""");
        Assert.That(SentHex(), Is.Empty);
        Action("""<action name="set_event_ui_countdown" script="$x$" round_countdown="2" box_ids="0" />""");
        Assert.That(SentHex(), Is.Empty);
        Action("""<action name="set_event_ui_countdown" script="$x$" round_countdown="2,30" box_ids="301" />""");
        Assert.That(SentHex(), Is.EqualTo(new[] { packet }));
        Action("""<action name="set_event_ui_countdown" script="$x$" round_countdown="2,30" box_ids="!302" />""");
        Assert.That(SentHex(), Is.EqualTo(new[] { packet }));
        Action("""<action name="set_event_ui_countdown" script="$x$" round_countdown="2,30" box_ids="0" />""");
        Assert.That(SentHex(), Is.EqualTo(new[] { packet }));
    }

    // random_additional_effect: the effect shows over the box's players at once, and the pick waits for the tick.
    [Test]
    public void RandomAdditionalEffect_TheBoxsPlayers() {
        Start(Box(301, Vector3.Zero), Box(302, new Vector3(5000, 0, 0)));

        Action("""<action name="random_additional_effect" target="pc" box_id="302" target_count="1" tick="1000" wait_tick="1000" target_effect="fx" additional_effect_id="0" />""");
        Assert.That(SentHex(), Is.Empty);
        Action("""<action name="random_additional_effect" target="pc" box_id="301" target_count="1" tick="1000" wait_tick="1000" target_effect="fx" additional_effect_id="0" />""");
        Assert.That(SentHex(), Is.EqualTo(new[] { Hex(TriggerPacket.Effect1(new[] { test.Session.Player.ObjectId }, "fx")) }));
        Assert.That(context.Events.Count, Is.EqualTo(1));
    }

    // room_expire (84000003_wd): each player goes back, and the room's map leaves the return maps.
    [Test]
    public void RoomExpire_ReturnsThePlayers() {
        Start();
        var returnMaps = test.Session.Player.Value.Character.ReturnMaps;
        returnMaps.Push(Field.MapId);

        // The field change itself needs the server's field factory (none in the test): only the return map is checked.
        Assert.Throws<NullReferenceException>(() => Action("""<action name="room_expire" />"""));
        Assert.That(returnMaps.Peek(), Is.Not.EqualTo(Field.MapId));
    }

    // set_user_value_from_dungeon_reward_count (Colosseum): the key is the player's reward count of the room.
    [Test]
    public void SetUserValueFromDungeonRewardCount_ThePlayersCount() {
        Start();
        UserValues();
        test.Session.Dungeon.Records[29900001] = new DungeonRecord(29900001) { UnionClears = 5, UnionCooldownTimestamp = long.MaxValue / 2 };

        Action("""<action name="set_user_value_from_dungeon_reward_count" key="ClearRound" dungeon_reward_id="29900001" />""");
        Assert.That(Field.UserValues.Get(1, "ClearRound"), Is.EqualTo(5));
    }

    // dungeon_disable_ranking (Colosseum): the room's run is left off the board.
    [Test]
    public void DungeonDisableRanking_TheRoomsRecord() {
        Start();
        DungeonRoomRecord record = test.EnterDungeon(TestGameSession.DungeonRoom(1, Field.MapId));
        SetTriggers();
        context = NewContext();

        Action("""<action name="dungeon_disable_ranking" />""");
        Assert.That(record.RankingDisabled, Is.True);
    }

    // talk_npc (Colosseum's Nagi): the spawn point's npc starts its talk with each player.
    [Test]
    public void TalkNpc_TheSpawnPointsNpc() {
        Start();
        const int npcId = 11003587;
        test.Register(new ScriptMetadata(npcId, ScriptType.Npc, new Dictionary<int, ScriptState> {
            [0] = new(0, ScriptStateType.Select, false, null, [new CinematicContent("", NpcTalkButton.None, 0, [], [])]),
            [10] = new(10, ScriptStateType.Script, true, null, [new CinematicContent("", NpcTalkButton.None, 0, [], [])]),
        }));
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>()));
        test.SetServerTable(new JobConditionTable(new Dictionary<int, JobConditionMetadata>()));
        var basic = new NpcMetadataBasic(2, 2, 1, 1, 0, 1, 1, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(npcId, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        GC.SuppressFinalize(npc);
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(npc, new Npc(metadata, null));
        typeof(FieldNpc).GetField(nameof(FieldNpc.SpawnAnimation), Any)!.SetValue(npc, "spawn");
        npc.SpawnPointId = 901;
        var npcs = new ConcurrentDictionary<int, FieldNpc>();
        typeof(FieldManager).GetField("<Npcs>k__BackingField", Any)!.SetValue(Field, npcs);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        npcs[9001] = npc;

        Action("""<action name="talk_npc" spawn_id="902" />""");
        Assert.That(test.TakeSentOps(), Does.Not.Contain(SendOp.NpcTalk));
        Action("""<action name="talk_npc" spawn_id="901" />""");
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.NpcTalk));
    }
}
