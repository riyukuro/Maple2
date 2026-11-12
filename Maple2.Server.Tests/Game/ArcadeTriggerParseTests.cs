using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The triggers of the arcade maps 51000001-51000006 as the ingest stores them (KR data).
public class ArcadeTriggerParseTests {
    // 51000001_dg tutorial, whole: it ends the round with user_detected box_ids="!100".
    private const string Tutorial = "<ms2><state name=\"\uB300\uAE30\"><condition name=\"user_detected\" box_ids=\"100\"><transition state=\"\uBAAC\uC2A4\uD130\uC18C\uD658\" /></condition></state><state name=\"\uBAAC\uC2A4\uD130\uC18C\uD658\"><onEnter><action name=\"spawn_monster\" spawn_ids=\"1000\" auto_target=\"0\" /></onEnter><condition name=\"monster_dead\" spawn_ids=\"1000\"><transition state=\"\uB300\uAE30\uC2DC\uAC04\" /></condition><condition name=\"user_detected\" box_ids=\"!100\"><transition state=\"\uC885\uB8CC\" /></condition></state><state name=\"\uB300\uAE30\uC2DC\uAC04\"><condition name=\"wait_tick\" wait_tick=\"5000\"><transition state=\"\uB300\uAE30\" /></condition><condition name=\"user_detected\" box_ids=\"!100\"><transition state=\"\uC885\uB8CC\" /></condition></state><state name=\"\uC885\uB8CC\"></state></ms2>";

    // Every action and condition of the arcade triggers, one per element name and value form (digits,
    // lists, ranges, "!" and text), with the shipped values.
    private static readonly Dictionary<int, string[]> Elements = new() {
        [51000001] = [
            "<action name=\"set_interact_object\" trigger_ids=\"11000013, 11000014, 11000015, 11000016, 11000017\" state=\"2\" />",
            "<condition name=\"user_detected\" box_ids=\"144\" />",
            "<action name=\"set_event_ui_round\" arg1=\"0\" rounds=\"0,0\" />",
            "<action name=\"select_camera\" trigger_id=\"344\" enable=\"1\" />",
            "<condition name=\"wait_tick\" wait_tick=\"2000\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_Ending_Popup_01\" />",
            "<action name=\"arcade_spring_farm_end_game\" type=\"EndGame\" />",
            "<action name=\"set_timer\" timer_id=\"10\" seconds=\"10\" auto_remove=\"0\" display=\"1\" v_offset=\"-30\" type=\"TR\" />",
            "<condition name=\"time_expired\" timer_id=\"10\" />",
            "<action name=\"move_user\" map_id=\"0\" portal_id=\"0\" />",
            "<action name=\"add_buff\" box_ids=\"199\" skill_id=\"49179101\" level=\"1\" is_skill_set=\"0\" />",
            "<action name=\"set_cube\" trigger_ids=\"5000-5003\" is_visible=\"false\" />",
            "<action name=\"set_cinematic_ui\" type=\"1\" />",
            "<action name=\"set_cinematic_ui\" type=\"3\" script=\"$51000001_DG__MAIN__0$\" />",
            "<action name=\"set_skip\" state=\"\uD29C\uD1A0\uB9AC\uC5BC\uC2DC\uC791\" />",
            "<action name=\"set_cube\" trigger_ids=\"5000-5003\" random_count=\"4\" is_visible=\"true\" />",
            "<action name=\"show_guide_summary\" entity_id=\"25100101\" text_id=\"25100101\" duration=\"3000\" />",
            "<action name=\"set_achievement\" trigger_id=\"199\" type=\"trigger\" achieve=\"springfarm_start\" />",
            "<action name=\"set_achievement\" trigger_id=\"199\" type=\"trigger\" achieve=\"arcade_startcheck\" />",
            "<action name=\"hide_guide_summary\" entity_id=\"25100101\" />",
            "<action name=\"arcade_spring_farm_start_game\" type=\"StartGame\" life_count=\"3\" />",
            "<action name=\"arcade_spring_farm_set_interact_score\" type=\"SetInteractScore\" id=\"19000022\" score=\"50\" />",
            "<action name=\"arcade_spring_farm_spawn_monster\" type=\"SpawnMonster\" score=\"5000\" spawn_ids=\"1001,1002,1003\" />",
            "<action name=\"arcade_spring_farm_start_round\" type=\"StartRound\" round=\"1\" ui_duration=\"3000\" time_score_type=\"remain\" time_score_rate=\"500\" round_duration=\"120000\" />",
            "<action name=\"set_event_ui_round\" arg1=\"0\" rounds=\"1,5,1\" v_offset=\"120\" />",
            "<condition name=\"monster_dead\" spawn_ids=\"1001,1002,1003\" />",
            "<action name=\"set_achievement\" trigger_id=\"199\" type=\"trigger\" achieve=\"springfarm_1round\" />",
            "<action name=\"arcade_spring_farm_clear_round\" type=\"ClearRound\" round=\"1\" />",
            "<action name=\"reset_timer\" timer_id=\"100001\" />",
            "<action name=\"move_user\" map_id=\"51000001\" portal_id=\"2\" box_id=\"101\" />",
            "<action name=\"arcade_spring_farm_spawn_monster\" type=\"SpawnMonster\" score=\"5000\" spawn_ids=\"1101,1102,1103,1104\" />",
            "<action name=\"remove_buff\" box_id=\"199\" skill_id=\"70000091\" />",
            "<condition name=\"monster_dead\" spawn_ids=\"1101,1102,1103,1104\" />",
            "<action name=\"arcade_spring_farm_spawn_monster\" type=\"SpawnMonster\" score=\"5000\" spawn_ids=\"1201,1202,1203,1204,1205,1206,1207\" />",
            "<condition name=\"monster_dead\" spawn_ids=\"1201,1202,1203,1204,1205,1206,1207\" />",
            "<action name=\"arcade_spring_farm_spawn_monster\" type=\"SpawnMonster\" score=\"5000\" spawn_ids=\"1301,1302,1303,1304,1305,1306,1307,1308\" />",
            "<condition name=\"monster_dead\" spawn_ids=\"1301,1302,1303,1304,1305,1306,1307,1308\" />",
            "<action name=\"arcade_spring_farm_spawn_monster\" type=\"SpawnMonster\" score=\"5000\" spawn_ids=\"1401,1402,1403,1404,1405\" />",
            "<condition name=\"monster_dead\" spawn_ids=\"1401,1402,1403,1404,1405\" />",
            "<action name=\"arcade_spring_farm_spawn_monster\" type=\"SpawnMonster\" score=\"5000\" spawn_ids=\"1501,1502,1503,1504,1505,1506,1507,1508,1509,1510\" />",
            "<condition name=\"monster_dead\" spawn_ids=\"1501,1502,1503,1504,1505,1506,1507,1508,1509,1510\" />",
            "<action name=\"arcade_spring_farm_spawn_monster\" type=\"SpawnMonster\" score=\"5000\" spawn_ids=\"1701,1702,1703,1704,1705,1706,1707,1708,1709,1710,1711\" />",
            "<condition name=\"monster_dead\" spawn_ids=\"1701,1702,1703,1704,1705,1706,1707,1708,1709,1710,1711\" />",
            "<action name=\"arcade_spring_farm_spawn_monster\" type=\"SpawnMonster\" score=\"5000\" spawn_ids=\"1901,1902,1903,1904,1905,1906,1907,1908,1909,1910,1911,1912,1913,1914,1915\" />",
            "<condition name=\"monster_dead\" spawn_ids=\"1901,1902,1903,1904,1905,1906,1907,1908,1909,1910,1911,1912,1913,1914,1915\" />",
            "<action name=\"arcade_spring_farm_spawn_monster\" type=\"SpawnMonster\" score=\"10000\" spawn_ids=\"2001\" />",
            "<action name=\"set_achievement\" trigger_id=\"199\" type=\"trigger\" achieve=\"springfarm_clear\" />",
            "<action name=\"set_event_ui_script\" type=\"7\" script=\"\" duration=\"2000\" box_ids=\"0\" />",
            "<action name=\"set_mesh\" trigger_ids=\"3101\" visible=\"0\" start_delay=\"0\" interval=\"0\" fade=\"0\" />",
            "<condition name=\"user_detected\" negate=\"true\" box_ids=\"101\" />",
            "<condition name=\"random_condition\" weight=\"33\" />",
            "<action name=\"set_achievement\" trigger_id=\"199\" type=\"trigger\" achieve=\"random_buff_box\" />",
            "<action name=\"add_buff\" box_ids=\"199\" skill_id=\"49179051\" level=\"1\" ignore_player=\"0\" is_skill_set=\"0\" />",
            "<action name=\"spawn_monster\" spawn_ids=\"1000\" auto_target=\"0\" />",
            "<condition name=\"monster_dead\" spawn_ids=\"1000\" />",
            "<condition name=\"user_detected\" box_ids=\"!100\" />",
        ],
        [51000002] = [
            "<action name=\"set_mesh\" trigger_ids=\"301\" visible=\"0\" start_delay=\"0\" interval=\"0\" />",
            "<condition name=\"npc_detected\" box_id=\"102\" spawn_ids=\"99\" />",
            "<action name=\"set_timer\" timer_id=\"90\" seconds=\"90\" />",
            "<condition name=\"time_expired\" timer_id=\"90\" />",
            "<condition name=\"monster_dead\" spawn_ids=\"99\" />",
            "<condition name=\"user_detected\" negate=\"true\" box_ids=\"102\" />",
            "<condition name=\"user_detected\" box_ids=\"701\" />",
            "<action name=\"set_cube\" trigger_ids=\"5001-5020\" is_visible=\"true\" />",
            "<action name=\"arcade_boom_boom_ocean_start_game\" type=\"StartGame\" life_count=\"20\" />",
            "<action name=\"select_camera_path\" path_ids=\"8001\" return_view=\"0\" />",
            "<action name=\"set_portal\" portal_id=\"2\" visible=\"0\" enable=\"0\" minimap_visible=\"0\" />",
            "<action name=\"spawn_monster\" spawn_ids=\"99\" auto_target=\"0\" />",
            "<action name=\"destroy_monster\" spawn_ids=\"99\" />",
        ],
        [51000003] = [
            "<condition name=\"user_value\" key=\"Tutorial\" value=\"1\" />",
            "<condition name=\"user_detected\" box_ids=\"701\" />",
            "<action name=\"add_buff\" box_ids=\"701\" skill_id=\"70000085\" level=\"1\" is_skill_set=\"0\" />",
            "<condition name=\"always\" />",
            "<action name=\"destroy_monster\" spawn_ids=\"111,112,113,114,115,116,117,118\" />",
            "<condition name=\"user_value\" key=\"Round_01\" value=\"1\" />",
            "<action name=\"spawn_monster\" spawn_ids=\"113\" auto_target=\"1\" delay=\"600\" />",
            "<condition name=\"user_value\" key=\"Reset\" value=\"1\" />",
            "<condition name=\"wait_tick\" wait_tick=\"6000\" />",
            "<action name=\"destroy_monster\" spawn_ids=\"101,102,103,104,105,106,107,108,111,112,113,114,115,116,117,118\" />",
            "<condition name=\"user_value\" key=\"Fail\" value=\"1\" />",
            "<condition name=\"count_users\" box_id=\"799\" min_users=\"1\" />",
            "<condition name=\"user_detected\" negate=\"true\" box_ids=\"701\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_Ending_Popup_01\" />",
            "<action name=\"set_timer\" timer_id=\"10\" seconds=\"10\" display=\"1\" />",
            "<action name=\"select_camera_path\" path_ids=\"8800\" return_view=\"0\" />",
            "<action name=\"arcade_boom_boom_ocean_end_game\" type=\"EndGame\" />",
            "<action name=\"set_user_value\" key=\"Reset\" value=\"1\" trigger_id=\"991104\" />",
            "<action name=\"set_user_value\" key=\"Round_01\" value=\"0\" trigger_id=\"991111\" />",
            "<condition name=\"time_expired\" timer_id=\"10\" />",
            "<action name=\"move_user\" map_id=\"0\" portal_id=\"0\" box_id=\"705\" />",
            "<action name=\"set_effect\" trigger_ids=\"7001\" visible=\"0\" />",
            "<action name=\"set_portal\" portal_id=\"99\" visible=\"0\" enable=\"0\" minimap_visible=\"0\" />",
            "<action name=\"set_event_ui_script\" type=\"1\" script=\"$51000003_DG__HIDDEN__0$\" duration=\"4000\" box_ids=\"0\" />",
            "<action name=\"set_event_ui_round\" arg1=\"0\" arg3=\"0\" rounds=\"6,6\" v_offset=\"0\" />",
            "<action name=\"select_camera\" trigger_id=\"8002\" enable=\"1\" />",
            "<action name=\"show_count_ui\" text=\"$61000004_ME__TRIGGER_01__1$\" stage=\"0\" count=\"5\" />",
            "<action name=\"set_achievement\" trigger_id=\"710\" type=\"trigger\" achieve=\"boomboombeach_hidden_start\" />",
            "<action name=\"set_user_value\" key=\"item_710_spawn\" value=\"0\" trigger_id=\"991119\" />",
            "<condition name=\"random_condition\" weight=\"1\" />",
            "<condition name=\"user_value\" key=\"item_710_spawn\" value=\"1\" />",
            "<action name=\"set_achievement\" trigger_id=\"701\" type=\"trigger\" achieve=\"random_buff_box\" />",
            "<action name=\"set_mesh\" trigger_ids=\"6001-6100\" visible=\"0\" start_delay=\"0\" interval=\"0\" fade=\"0\" />",
            "<action name=\"spawn_monster\" spawn_ids=\"501,502,503,504,505,506,507,508,509\" auto_target=\"1\" delay=\"0\" />",
            "<action name=\"set_cinematic_ui\" type=\"1\" />",
            "<action name=\"select_camera_path\" path_ids=\"8100,8101,8104,8106,8103,8105,8107,8108\" return_view=\"0\" />",
            "<action name=\"set_skip\" state=\"Tutorial_02\" />",
            "<action name=\"select_camera_path\" path_ids=\"8005,8001,8002\" return_view=\"0\" />",
            "<action name=\"set_user_value\" key=\"Tutorial\" value=\"0\" trigger_id=\"991109\" />",
            "<action name=\"arcade_boom_boom_ocean_start_game\" type=\"StartGame\" life_count=\"20\" />",
            "<action name=\"show_count_ui\" text=\"$51000003_DG__MAIN__0$\" stage=\"0\" count=\"5\" />",
            "<action name=\"set_user_value\" key=\"Fail\" value=\"1\" trigger_id=\"991103\" />",
            "<action name=\"destroy_monster\" spawn_ids=\"501,502,503,504,505,506,507,508,509\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_Monkey_01\" />",
            "<action name=\"set_achievement\" trigger_id=\"701\" type=\"trigger\" achieve=\"boomocean_start\" />",
            "<action name=\"set_achievement\" trigger_id=\"701\" type=\"trigger\" achieve=\"arcade_startcheck\" />",
            "<action name=\"remove_buff\" box_id=\"49179004\" skill_id=\"0\" />",
            "<action name=\"arcade_boom_boom_ocean_set_skill_score\" type=\"SetSkillScore\" id=\"49210001\" score=\"-500\" />",
            "<action name=\"arcade_boom_boom_ocean_set_skill_score\" type=\"SetSkillScore\" id=\"70000080\" score=\"1000\" />",
            "<action name=\"arcade_boom_boom_ocean_start_round\" type=\"StartRound\" round=\"1\" round_duration=\"1500\" time_score_rate=\"1033\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_Fruit_Throw_Loop_01\" />",
            "<action name=\"arcade_boom_boom_ocean_clear_round\" type=\"ClearRound\" round=\"1\" />",
            "<action name=\"set_achievement\" trigger_id=\"701\" type=\"trigger\" achieve=\"boomocean_1min\" />",
            "<action name=\"select_camera_path\" path_ids=\"8002,8003\" return_view=\"0\" />",
            "<action name=\"set_user_value\" key=\"Treasure_box\" value=\"1\" trigger_id=\"991102\" />",
            "<action name=\"destroy_monster\" spawn_ids=\"301,302,303,304,305,306,307,308,309,310,311,312,351,352,353,354,355,356,357,358,359,360,361,362\" />",
            "<action name=\"destroy_monster\" spawn_ids=\"351,352\" />",
            "<action name=\"set_interact_object\" trigger_ids=\"11000018\" state=\"2\" />",
            "<action name=\"destroy_monster\" spawn_ids=\"201,202,203,204,205,206,207,208,209,210,211,212,213,214,215,298,299\" />",
            "<action name=\"spawn_monster\" spawn_ids=\"201\" auto_target=\"1\" />",
            "<action name=\"set_timer\" timer_id=\"5\" seconds=\"5\" />",
            "<action name=\"destroy_monster\" spawn_ids=\"421,422,423,424,425,426,427,428,429,430\" />",
            "<action name=\"spawn_monster\" spawn_ids=\"405,406\" auto_target=\"1\" delay=\"0\" />",
        ],
        [51000004] = [
            "<action name=\"move_user\" map_id=\"51000004\" portal_id=\"10\" />",
            "<action name=\"spawn_monster\" spawn_ids=\"101\" auto_target=\"0\" />",
            "<condition name=\"user_detected\" box_ids=\"9000\" />",
            "<action name=\"set_scene_skip\" state=\"\uC14B\uB458\uD558\uB098_\uC2A4\uD0B5\uC644\uB8CC\" action=\"nextState\" />",
            "<action name=\"select_camera_path\" path_ids=\"8000\" return_view=\"0\" />",
            "<action name=\"set_cinematic_ui\" type=\"1\" />",
            "<action name=\"set_npc_emotion_loop\" spawn_id=\"101\" sequence_name=\"Walk_A\" duration=\"5000\" />",
            "<condition name=\"wait_tick\" wait_tick=\"500\" />",
            "<action name=\"set_cinematic_ui\" type=\"3\" script=\"$51000004_DG__51000004_MAIN__0$\" />",
            "<action name=\"set_npc_emotion_loop\" spawn_id=\"101\" sequence_name=\"Attack_01_I_Bore\" duration=\"3000\" />",
            "<action name=\"set_scene_skip\" state=\"State\" action=\"\" />",
            "<action name=\"select_camera_path\" path_ids=\"8003,8004\" return_view=\"0\" />",
            "<action name=\"reset_camera\" interpolation_time=\"1.5\" />",
            "<condition name=\"user_detected\" negate=\"true\" box_ids=\"9000\" />",
            "<action name=\"set_achievement\" trigger_id=\"9000\" type=\"trigger\" achieve=\"PinkBeanThreeTwoOne_start\" />",
            "<action name=\"write_log\" log_name=\"PinkBeanThreeTwoOne_log\" trigger_id=\"9000\" event=\"char_event\" sub_event=\"gamestart\" />",
            "<action name=\"arcade_three_two_one_start_game\" type=\"StartGame\" life_count=\"5\" init_score=\"10000\" />",
            "<action name=\"set_user_value\" key=\"Fail\" value=\"1\" trigger_id=\"4001\" />",
            "<action name=\"add_balloon_talk\" msg=\"$51000004_DG__51000004_MAIN__4$\" duration=\"3000\" spawn_id=\"0\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_GetReadyGo_01\" />",
            "<action name=\"show_guide_summary\" entity_id=\"1\" text_id=\"26300736\" duration=\"3000\" />",
            "<action name=\"arcade_three_two_one_start_round\" type=\"StartRound\" round=\"1\" ui_duration=\"4\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_ArrowPopup_01\" />",
            "<condition name=\"random_condition\" weight=\"1\" />",
            "<action name=\"arcade_three_two_one_result_round\" type=\"ResultRound\" result_direction=\"1\" />",
            "<action name=\"set_npc_rotation\" spawn_id=\"101\" rotation=\"270\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Turning_01\" />",
            "<action name=\"init_npc_rotation\" spawn_ids=\"101\" />",
            "<action name=\"set_pc_rotation\" rotation=\"0,0,0\" />",
            "<condition name=\"user_value\" key=\"ThreeTwoOneResult\" value=\"1\" />",
            "<action name=\"set_npc_emotion_loop\" spawn_id=\"101\" sequence_name=\"Failure_A\" duration=\"2700\" />",
            "<action name=\"set_pc_emotion_loop\" sequence_name=\"Emotion_Dance_V\" duration=\"2450\" />",
            "<action name=\"add_balloon_talk\" msg=\"$51000004_DG__51000004_MAIN__7$\" duration=\"3000\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Correct_01\" />",
            "<action name=\"set_npc_emotion_loop\" spawn_id=\"101\" sequence_name=\"Dance_A\" duration=\"2700\" />",
            "<action name=\"set_pc_emotion_sequence\" sequence_names=\"Emotion_Fuss_A\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Wrong_01\" />",
            "<action name=\"arcade_three_two_one_result_round2\" type=\"ResultRound2\" round=\"1\" />",
            "<action name=\"arcade_three_two_one_clear_round\" type=\"ClearRound\" round=\"1\" />",
            "<action name=\"destroy_monster\" spawn_ids=\"101\" />",
            "<action name=\"arcade_three_two_one_end_game\" type=\"EndGame\" />",
            "<action name=\"select_camera\" trigger_id=\"8010\" enable=\"1\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Result_01\" />",
            "<condition name=\"user_value\" key=\"Fail\" value=\"1\" />",
            "<action name=\"write_log\" log_name=\"PinkBeanThreeTwoOne_log\" trigger_id=\"9001\" event=\"char_event\" sub_event=\"gameover\" />",
            "<action name=\"set_timer\" timer_id=\"10\" seconds=\"10\" display=\"1\" />",
            "<condition name=\"time_expired\" timer_id=\"10\" />",
        ],
        [51000005] = [
            "<action name=\"move_user\" map_id=\"51000005\" portal_id=\"10\" />",
            "<action name=\"spawn_monster\" spawn_ids=\"101\" auto_target=\"0\" />",
            "<action name=\"set_effect\" trigger_ids=\"601\" visible=\"0\" />",
            "<condition name=\"user_detected\" box_ids=\"9000\" />",
            "<action name=\"set_scene_skip\" state=\"\uC14B\uB458\uD558\uB098_\uC2A4\uD0B5\uC644\uB8CC\" action=\"nextState\" />",
            "<action name=\"select_camera_path\" path_ids=\"8000\" return_view=\"0\" />",
            "<action name=\"set_cinematic_ui\" type=\"1\" />",
            "<action name=\"set_npc_emotion_loop\" spawn_id=\"101\" sequence_name=\"Talk_A\" duration=\"1000\" />",
            "<condition name=\"wait_tick\" wait_tick=\"500\" />",
            "<action name=\"select_camera_path\" path_ids=\"8003,8004\" return_view=\"0\" />",
            "<action name=\"set_cinematic_ui\" type=\"3\" script=\"$51000005_DG__51000005_MAIN__0$\" />",
            "<action name=\"set_npc_emotion_sequence\" spawn_id=\"101\" sequence_name=\"Bore_B\" duration_tick=\"2500\" />",
            "<action name=\"set_npc_emotion_sequence\" spawn_id=\"101\" sequence_name=\"Emotion_A\" duration_tick=\"2400\" />",
            "<action name=\"set_scene_skip\" state=\"State\" action=\"\" />",
            "<action name=\"set_npc_emotion_sequence\" spawn_id=\"101\" sequence_name=\"Emotion_D\" duration_tick=\"2400\" />",
            "<action name=\"reset_camera\" interpolation_time=\"1.5\" />",
            "<condition name=\"user_detected\" negate=\"true\" box_ids=\"9000\" />",
            "<action name=\"set_achievement\" trigger_id=\"9000\" type=\"trigger\" achieve=\"HyukiThreeTwoOne_start\" />",
            "<action name=\"write_log\" log_name=\"ThreeTwoOne_log\" trigger_id=\"9000\" event=\"char_event\" sub_event=\"HyukiThreeTwoOnegamestart\" />",
            "<action name=\"arcade_three_two_one2_start_game\" type=\"StartGame\" life_count=\"5\" init_score=\"10000\" />",
            "<action name=\"set_user_value\" key=\"Fail\" value=\"1\" trigger_id=\"4001\" />",
            "<action name=\"add_balloon_talk\" msg=\"$51000005_DG__51000005_MAIN__4$\" duration=\"3000\" spawn_id=\"0\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Start_01\" />",
            "<action name=\"show_guide_summary\" entity_id=\"1\" text_id=\"26300736\" duration=\"3000\" />",
            "<action name=\"arcade_three_two_one2_start_round\" type=\"StartRound\" round=\"1\" ui_duration=\"4\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_ArrowPopup_01\" />",
            "<condition name=\"random_condition\" weight=\"1\" />",
            "<action name=\"arcade_three_two_one2_result_round\" type=\"ResultRound\" result_direction=\"1\" />",
            "<action name=\"set_npc_rotation\" spawn_id=\"101\" rotation=\"270\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Turning_01\" />",
            "<action name=\"init_npc_rotation\" spawn_ids=\"101\" />",
            "<action name=\"set_pc_rotation\" rotation=\"0,0,0\" />",
            "<condition name=\"user_value\" key=\"ThreeTwoOneResult\" value=\"1\" />",
            "<action name=\"set_npc_emotion_sequence\" spawn_id=\"101\" sequence_name=\"Emotion_B\" duration_tick=\"4000\" />",
            "<action name=\"set_pc_emotion_loop\" sequence_name=\"Emotion_Dance_X\" duration=\"2450\" />",
            "<action name=\"add_balloon_talk\" msg=\"$51000005_DG__51000005_MAIN__7$\" duration=\"3000\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Correct_01\" />",
            "<action name=\"set_pc_emotion_sequence\" sequence_names=\"Emotion_Fuss_A\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Wrong_01\" />",
            "<action name=\"arcade_three_two_one2_result_round2\" type=\"ResultRound2\" round=\"1\" />",
            "<action name=\"arcade_three_two_one2_clear_round\" type=\"ClearRound\" round=\"1\" />",
            "<action name=\"destroy_monster\" spawn_ids=\"101\" />",
            "<action name=\"arcade_three_two_one2_end_game\" type=\"EndGame\" />",
            "<action name=\"select_camera\" trigger_id=\"8010\" enable=\"1\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Result_01\" />",
            "<condition name=\"user_value\" key=\"Fail\" value=\"1\" />",
            "<action name=\"write_log\" log_name=\"ThreeTwoOne_log\" trigger_id=\"9001\" event=\"char_event\" sub_event=\"HyukiThreeTwoOnegameover\" />",
            "<action name=\"set_timer\" timer_id=\"10\" seconds=\"10\" display=\"1\" />",
            "<action name=\"arcade_three_two_one_end_game\" type=\"EndGame\" />",
            "<condition name=\"time_expired\" timer_id=\"10\" />",
        ],
        [51000006] = [
            "<action name=\"move_user\" map_id=\"51000006\" portal_id=\"10\" />",
            "<action name=\"spawn_monster\" spawn_ids=\"101\" auto_target=\"0\" />",
            "<action name=\"set_effect\" trigger_ids=\"601\" visible=\"0\" />",
            "<action name=\"set_actor\" arg4=\"0\" arg5=\"0\" trigger_id=\"611\" visible=\"0\" initial_sequence=\"0\" />",
            "<condition name=\"user_detected\" box_ids=\"9000\" />",
            "<action name=\"set_scene_skip\" state=\"\uC14B\uB458\uD558\uB098_\uC2A4\uD0B5\uC644\uB8CC\" action=\"nextState\" />",
            "<action name=\"select_camera_path\" path_ids=\"8000\" return_view=\"0\" />",
            "<action name=\"set_cinematic_ui\" type=\"1\" />",
            "<action name=\"set_npc_emotion_loop\" spawn_id=\"101\" sequence_name=\"Talk_A\" duration=\"1000\" />",
            "<condition name=\"wait_tick\" wait_tick=\"500\" />",
            "<action name=\"select_camera_path\" path_ids=\"8003,8004\" return_view=\"0\" />",
            "<action name=\"set_cinematic_ui\" type=\"3\" script=\"$51000006_DG__51000006_MAIN__0$\" />",
            "<action name=\"set_npc_emotion_sequence\" spawn_id=\"101\" sequence_name=\"Walk_A\" duration_tick=\"2500\" />",
            "<action name=\"set_npc_emotion_sequence\" spawn_id=\"101\" sequence_name=\"Event_Bore_A\" duration_tick=\"2900\" />",
            "<action name=\"set_npc_emotion_loop\" spawn_id=\"101\" sequence_name=\"Event_Eat_A\" duration=\"3000\" />",
            "<action name=\"set_scene_skip\" state=\"State\" action=\"\" />",
            "<action name=\"set_npc_emotion_sequence\" spawn_id=\"101\" sequence_name=\"Attack_01_G\" duration_tick=\"3200\" />",
            "<action name=\"reset_camera\" interpolation_time=\"1.5\" />",
            "<condition name=\"user_detected\" negate=\"true\" box_ids=\"9000\" />",
            "<action name=\"set_achievement\" trigger_id=\"9000\" type=\"trigger\" achieve=\"BlackbeanThreeTwoOne_start\" />",
            "<action name=\"write_log\" log_name=\"ThreeTwoOne_log\" trigger_id=\"9000\" event=\"char_event\" sub_event=\"BlackbeanThreeTwoOnegamestart\" />",
            "<action name=\"arcade_three_two_one3_start_game\" type=\"StartGame\" life_count=\"5\" init_score=\"10000\" />",
            "<action name=\"set_user_value\" key=\"Fail\" value=\"1\" trigger_id=\"4001\" />",
            "<action name=\"add_balloon_talk\" msg=\"$51000006_DG__51000006_MAIN__4$\" duration=\"3000\" spawn_id=\"0\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_GetReadyGo_01\" />",
            "<action name=\"set_pc_rotation\" rotation=\"0,0,0\" />",
            "<action name=\"show_guide_summary\" entity_id=\"1\" text_id=\"26300736\" duration=\"3000\" />",
            "<action name=\"arcade_three_two_one3_start_round\" type=\"StartRound\" round=\"1\" ui_duration=\"4\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_ArrowPopup_01\" />",
            "<condition name=\"random_condition\" weight=\"1\" />",
            "<action name=\"arcade_three_two_one3_result_round\" type=\"ResultRound\" result_direction=\"1\" />",
            "<action name=\"set_npc_rotation\" spawn_id=\"101\" rotation=\"270\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Turning_01\" />",
            "<action name=\"init_npc_rotation\" spawn_ids=\"101\" />",
            "<condition name=\"user_value\" key=\"ThreeTwoOneResult\" value=\"1\" />",
            "<action name=\"set_npc_emotion_sequence\" spawn_id=\"101\" sequence_name=\"Dance_C_Ride\" duration_tick=\"3300\" />",
            "<action name=\"set_pc_emotion_loop\" sequence_name=\"Emotion_Dance_E\" duration=\"3300\" />",
            "<action name=\"add_balloon_talk\" msg=\"$51000006_DG__51000006_MAIN__7$\" duration=\"3300\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Correct_01\" />",
            "<action name=\"set_npc_emotion_sequence\" spawn_id=\"101\" sequence_name=\"Event_Happy_A\" duration_tick=\"3300\" />",
            "<action name=\"set_actor\" trigger_id=\"611\" visible=\"1\" initial_sequence=\"Run_A\" />",
            "<action name=\"set_pc_emotion_sequence\" sequence_names=\"Emotion_Fuss_A\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Wrong_01\" />",
            "<action name=\"arcade_three_two_one3_result_round2\" type=\"ResultRound2\" round=\"1\" />",
            "<action name=\"arcade_three_two_one3_clear_round\" type=\"ClearRound\" round=\"1\" />",
            "<action name=\"destroy_monster\" spawn_ids=\"101\" />",
            "<action name=\"arcade_three_two_one3_end_game\" type=\"EndGame\" />",
            "<action name=\"select_camera\" trigger_id=\"8010\" enable=\"1\" />",
            "<action name=\"play_system_sound_in_box\" sound=\"System_PinkBeans_Arcade_Result_01\" />",
            "<condition name=\"user_value\" key=\"Fail\" value=\"1\" />",
            "<action name=\"write_log\" log_name=\"ThreeTwoOne_log\" trigger_id=\"9001\" event=\"char_event\" sub_event=\"BlackbeanThreeTwoOnegameover\" />",
            "<action name=\"set_timer\" timer_id=\"10\" seconds=\"10\" display=\"1\" />",
            "<action name=\"arcade_three_two_one_end_game\" type=\"EndGame\" />",
            "<condition name=\"time_expired\" timer_id=\"10\" />",
        ],
    };

    [Test]
    public void Tutorial_Parses_NotInBoxEndsTheRound() {
        var document = new XmlDocument();
        document.LoadXml(Tutorial);
        Trigger trigger = TriggerCache.ParseTrigger("51000001_dg", "tutorial", document);

        Trigger.UserDetected[] detected = trigger.States.SelectMany(state => state.Conditions).OfType<Trigger.UserDetected>().ToArray();
        Assert.Multiple(() => {
            Assert.That(trigger.States, Has.Count.EqualTo(4));
            Assert.That(detected, Has.Length.EqualTo(3));
            Assert.That(detected.Select(condition => condition.BoxIds), Has.All.EqualTo(new[] { 100 }));
            Assert.That(detected.Select(condition => condition.Negate), Is.EqualTo(new[] { false, true, true }));
        });
    }

    [TestCase("100", false, new[] { 100 }, false)]
    [TestCase("!100", false, new[] { 100 }, true)]
    [TestCase("!100", true, new[] { 100 }, false)]
    [TestCase("101,102", false, new[] { 101, 102 }, false)]
    public void UserDetected_NotPrefix(string boxIds, bool negate, int[] expectedBoxes, bool expectedNegate) {
        var condition = (Trigger.UserDetected) Condition($"<condition name=\"user_detected\" box_ids=\"{boxIds}\" negate=\"{(negate ? 1 : 0)}\" />");
        Assert.Multiple(() => {
            Assert.That(condition.BoxIds, Is.EqualTo(expectedBoxes));
            Assert.That(condition.Negate, Is.EqualTo(expectedNegate));
        });
    }

    [TestCase(51000001)]
    [TestCase(51000002)]
    [TestCase(51000003)]
    [TestCase(51000004)]
    [TestCase(51000005)]
    [TestCase(51000006)]
    public void ArcadeMap_EveryElementParses(int mapId) {
        Assert.Multiple(() => {
            foreach (string xml in Elements[mapId]) {
                XmlElement element = Element(xml);
                string name = element.GetAttribute("name");
                if (element.Name == "action") {
                    Assert.That(TriggerFunctionMapping.ActionMap.TryGetValue(name, out Func<XmlAttributeCollection?, IAction>? map), Is.True, xml);
                    Assert.DoesNotThrow(() => map!(element.Attributes), xml);
                } else {
                    Assert.That(TriggerFunctionMapping.ConditionMap.TryGetValue(name, out Func<XmlAttributeCollection?, ICondition>? map), Is.True, xml);
                    Assert.DoesNotThrow(() => map!(element.Attributes), xml);
                }
            }
        });
    }

    private static ICondition Condition(string xml) {
        XmlElement element = Element(xml);
        return TriggerFunctionMapping.ConditionMap[element.GetAttribute("name")](element.Attributes);
    }

    private static XmlElement Element(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document.DocumentElement!;
    }
}
