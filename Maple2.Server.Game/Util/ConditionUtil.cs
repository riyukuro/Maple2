using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

public static class ConditionUtil {
    public static bool Check(this ConditionMetadata condition, GameSession session, string targetString = "", long targetLong = 0, string codeString = "", long codeLong = 0) {
        if (condition.PartyCount > 0) {
            if (session.Party.Party == null || session.Party.Party.Members.Count < condition.PartyCount) {
                return false;
            }
        }

        if (condition.GuildPartyCount > 0) {
            if (session.Party.Party == null || session.Party.GuildMemberCount() < condition.GuildPartyCount) {
                return false;
            }
        }
        bool code = condition.Codes == null || condition.Codes.CheckCode(session, condition.Type, codeString, codeLong);
        bool target = condition.Target == null || condition.Target.CheckTarget(session, condition.Type, targetString, targetLong);
        return target && code;
    }

    private static bool CheckCode(this ConditionMetadata.Parameters code, GameSession session, ConditionType conditionType, string stringValue = "", long longValue = 0) {
        switch (conditionType) {
            case ConditionType.emotiontime:
            case ConditionType.emotion:
            case ConditionType.trigger:
            case ConditionType.npc_race:
            case ConditionType.vibrate_object:
            case ConditionType.quest_alliance_by_grade: // code: the mission's rank letter (S, A, B, C or D)
                if (code.Strings != null && code.Strings.Contains(stringValue)) {
                    return true;
                }
                break;
            case ConditionType.trophy_point:
                if (code.Range != null && InRange((ConditionMetadata.Range<int>) code.Range, (int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.interact_object:
            case ConditionType.interact_object_rep:
            case ConditionType.donation_type: // code: the donated item's property type
            case ConditionType.fame_grade: // code: the alliance
                if ((code.Range != null && InRange((ConditionMetadata.Range<int>) code.Range, (int) longValue)) ||
                    (code.Integers != null && code.Integers.Contains((int) longValue))) {
                    return true;
                }
                break;
            case ConditionType.item_collect:
            case ConditionType.item_collect_revise:
                if ((code.Range != null && InRange((ConditionMetadata.Range<int>) code.Range, (int) longValue)) ||
                    (code.Integers != null && code.Integers.Contains((int) longValue))) {
                    if (session.Player.Value.Unlock.CollectedItems.ContainsKey((int) longValue)) {
                        session.Player.Value.Unlock.CollectedItems[(int) longValue]++;
                        return false;
                    }

                    session.Player.Value.Unlock.CollectedItems.Add((int) longValue, 1);
                    return true;
                }
                break;
            case ConditionType.map:
            case ConditionType.useropen_minigame_clear:
            // minigame_clear: the mini-game field (23400007, 23400008, 23400039).
            case ConditionType.minigame_clear:
            // play_rps and its results: the event's play ticket (92000139 and others name item 20302424).
            case ConditionType.play_rps:
            case ConditionType.play_rps_win:
            case ConditionType.play_rps_lose:
            case ConditionType.play_rps_draw:
            case ConditionType.fish:
            case ConditionType.fisher_grade:
            case ConditionType.mastery_grade:
            case ConditionType.set_mastery_grade:
            case ConditionType.item_add:
            case ConditionType.item_destroy:
            case ConditionType.beauty_add:
            case ConditionType.beauty_change_color:
            case ConditionType.beauty_random:
            case ConditionType.beauty_style_add:
            case ConditionType.beauty_style_apply:
            case ConditionType.level:
            case ConditionType.level_up:
            case ConditionType.item_exist:
            case ConditionType.item_pickup:
            case ConditionType.quest:
            case ConditionType.mastery_gathering:
            case ConditionType.mastery_gathering_try:
            case ConditionType.mastery_harvest:
            case ConditionType.mastery_harvest_try:
            case ConditionType.mastery_farming_try:
            case ConditionType.mastery_farming:
            case ConditionType.mastery_harvest_otherhouse:
            case ConditionType.mastery_manufacturing:
            case ConditionType.openStoryBook:
            case ConditionType.quest_accept:
            case ConditionType.quest_clear_by_chapter:
            case ConditionType.quest_clear:
            case ConditionType.quest_revise_achieve:
            case ConditionType.quest_daily:
            case ConditionType.field_mission:
            case ConditionType.quest_alliance:
            case ConditionType.quest_field:
            case ConditionType.pet_catch_id:
            case ConditionType.pet_catch_grade:
            case ConditionType.pet_catch_category:
            case ConditionType.pet_enchant:
            case ConditionType.pet_first_collect: // code: the pet item added to the album
            // nurturing_*: the cube pet's stage-1 npc (92000095 "Play with ... in someone's home": code 50400282).
            case ConditionType.nurturing_play:
            case ConditionType.nurturing_eat:
            case ConditionType.nurturing_growth:
            // pet_evolution_by_rank: the rarity the pet evolved to (23100383 "Evolve normal pets to rare": code 2).
            // pet_evolution_point_by_rank: the rarity of the pet that gained the points (23100387 "with a normal pet": code 1).
            case ConditionType.pet_evolution_by_rank:
            case ConditionType.pet_evolution_point_by_rank:
            case ConditionType.pet_taming:
            case ConditionType.openItemBox:
            case ConditionType.dungeon_clear:
            case ConditionType.dungeon_first_clear:
            case ConditionType.dungeon_clear_group:
            case ConditionType.dungeon_rank_clear:
            case ConditionType.dungeon_rank:
            case ConditionType.item_design:
            case ConditionType.skill_damage_npc:
            // skill_die: the skill of the hit that took the last health (21233011 names 50100394, 50100405, 50100406).
            case ConditionType.skill_die:
            // npc_assist_bonus: the field boss whose assist bonus was given (21230013 names 23000001).
            case ConditionType.npc_assist_bonus:
            case ConditionType.buff:
            case ConditionType.enchant_result:
            case ConditionType.dialogue:
            case ConditionType.talk_in:
            case ConditionType.npc:
            case ConditionType.npc_lasthit:
            // The killing blow's variants: the NPC id as the code (22200343 any monster, 21000001-99999999;
            // 21230022 Alpha Turtle; 21250001-3 a dungeon's boss).
            case ConditionType.npc_lasthit_buff:
            case ConditionType.npc_lasthit_time:
            case ConditionType.npc_timeattack:
            case ConditionType.controller:
            case ConditionType.skill:
            case ConditionType.job:
            case ConditionType.job_change:
            case ConditionType.subjob_change: // code: the advanced job (101 Knight II to 1101)
            case ConditionType.item_move:
            case ConditionType.install_item:
            case ConditionType.rotate_cube:
            case ConditionType.uninstall_item:
            case ConditionType.fall:
            case ConditionType.swim:
            case ConditionType.swimtime:
            case ConditionType.run:
            case ConditionType.crawl:
            case ConditionType.glide:
            case ConditionType.climb:
            case ConditionType.ropetime:
            case ConditionType.laddertime:
            case ConditionType.holdtime:
            case ConditionType.riding:
            case ConditionType.fish_big:
            case ConditionType.music_play_instrument_mastery:
            case ConditionType.music_play_instrument_time:
            case ConditionType.music_play_ensemble_in:
            case ConditionType.play_ensenble_time:
            case ConditionType.music_play_score:
            case ConditionType.explore_continent:
            case ConditionType.continent:
            case ConditionType.explore:
            case ConditionType.revise_achieve_multi_grade:
            case ConditionType.revise_achieve_single_grade:
            case ConditionType.hero_achieve:
            case ConditionType.hero_achieve_grade:
            case ConditionType.stay_map:
            case ConditionType.stay_cube:
            // jump: the map of the jump (quests 70400081 and 70400103 name one map each).
            case ConditionType.jump:
            case ConditionType.spawner:
                if (code.Range != null && InRange((ConditionMetadata.Range<int>) code.Range, (int) longValue)) {
                    return true;
                }

                if (code.Integers != null && code.Integers.Contains((int) longValue)) {
                    return true;
                }
                break;
            // The PvP conditions: the code is the map of the fight (65000001 Red Arena, 65000002 Crimson Tear Mine,
            // 65000003 Treasure Island, 65010001-65010010 the rank duel maps; the trophies 21300003-21300041 name them
            // so). A row without a code (21300014, any arena) counts on every map.
            case ConditionType.pvp_win:
            case ConditionType.pvp_kill:
            case ConditionType.pvp_die:
            case ConditionType.pvp_participation:
            case ConditionType.pvp_win_score:
            case ConditionType.pvp_win_time:
            case ConditionType.pvp_win_with_buff:
            case ConditionType.pvp_win_with_grade:
            case ConditionType.pvp_win_perfect:
                if (code.Range != null && InRange((ConditionMetadata.Range<int>) code.Range, (int) longValue)) {
                    return true;
                }

                if (code.Integers != null && code.Integers.Contains((int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.fish_collect:
            case ConditionType.fish_goldmedal:
                if ((code.Range != null && InRange((ConditionMetadata.Range<int>) code.Range, (int) longValue)) ||
                    (code.Integers != null && code.Integers.Contains((int) longValue))) {
                    return !session.Player.Value.Unlock.FishAlbum.ContainsKey((int) longValue);
                }
                break;
            case ConditionType.meso:
            case ConditionType.taxifind:
            case ConditionType.fall_damage:
            case ConditionType.gemstone_upgrade:
            case ConditionType.gemstone_upgrade_success:
            case ConditionType.gemstone_upgrade_try:
            case ConditionType.socket_unlock_success:
            case ConditionType.socket_unlock_try:
            case ConditionType.socket_unlock:
            case ConditionType.gemstone_puton:
            case ConditionType.gemstone_putoff:
            case ConditionType.fish_fail:
            case ConditionType.music_play_grade:
            case ConditionType.music_play_ensemble:
            case ConditionType.breakable_object:
            case ConditionType.change_profile:
            case ConditionType.install_billboard:
            case ConditionType.buddy_request:
            case ConditionType.fall_survive:
            case ConditionType.fall_die:
            case ConditionType.buy_house:
            case ConditionType.extend_house:
            case ConditionType.exp:
            case ConditionType.item_merge_success:
            case ConditionType.wedding_propose:
            case ConditionType.wedding_propose_decline:
            case ConditionType.wedding_propose_declined:
            case ConditionType.wedding_hall_cancel:
            case ConditionType.item_gear_score:
            case ConditionType.killcount:
            case ConditionType.use_merat:
            case ConditionType.home_goto:
            case ConditionType.revival:
            case ConditionType.home_doctor:
            case ConditionType.resolve_panelty:
            case ConditionType.hit_tombstone:
            case ConditionType.taxiuse:
            case ConditionType.taxifee:
            case ConditionType.chat:
            case ConditionType.send_mail:
            case ConditionType.change_ugc_equip:
            case ConditionType.unlimited_enchant:
            case ConditionType.quest_guide:
            case ConditionType.enter_otherhouse:
            case ConditionType.playtime:
            case ConditionType.shop_buy:
            case ConditionType.shop_buy_karma_token:
            case ConditionType.shop_buy_honor_token:
                return true;
        }
        return false;
    }

    internal static bool CheckTarget(this ConditionMetadata.Parameters target, GameSession session, ConditionType conditionType, string stringValue = "", long longValue = 0) {
        switch (conditionType) {
            case ConditionType.stay_cube:
                if (target.Strings != null && target.Strings.Contains(stringValue)) {
                    return true;
                }
                break;
            case ConditionType.emotiontime:
            case ConditionType.emotion:
                if (target.Range != null && InRange((ConditionMetadata.Range<int>) target.Range, session.Player.Value.Character.MapId)) {
                    return true;
                }

                if (target.Integers != null && target.Integers.Any(i => i == session.Player.Value.Character.MapId)) {
                    return true;
                }

                break;
            case ConditionType.interact_object_rep:
                // The interact type of the object (2 telescope: the binocular event quest 89050108).
                if (target.Integers != null && target.Integers.Contains((int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.quest_alliance_by_grade:
                // The mission's alliance (1 to 5, 6 to 8 for Kritias, 9 for Georg).
                if (target.Integers != null && target.Integers.Contains((int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.lapenshard_upgrade_result:
                // The tier the lapenshard was fused to (21100060-21100064 "Fuse lapenshards to tier 6" to 10).
                if (target.Integers != null && target.Integers.Contains((int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.fame_grade:
                // The alliance grade reached (2 to 5 in the fame grade trophies).
                if (target.Integers != null && target.Integers.Contains((int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.item_exist:
            case ConditionType.spawner:
                // item_exist: the map the item is collected on (quest drops of one map, 20001480 and others).
                // spawner: the map of the spawn, as a region spawn id names a spawn of one map only.
                if (target.Range != null && InRange((ConditionMetadata.Range<int>) target.Range, (int) longValue)) {
                    return true;
                }

                if (target.Integers != null && target.Integers.Contains((int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.fall_damage:
                if (target.Range != null && InRange((ConditionMetadata.Range<int>) target.Range, (int) longValue)) {
                    return true;
                }

                if (target.Integers != null && target.Integers.Any(value => longValue >= value)) {
                    return true;
                }
                break;
            case ConditionType.gemstone_upgrade:
            case ConditionType.pet_collect: // target: the least grade of the pet added (23100276: target 1)
            case ConditionType.socket_unlock:
            case ConditionType.level_up:
            case ConditionType.level:
            case ConditionType.enchant_result:
            case ConditionType.install_billboard:
            case ConditionType.dungeon_rank:
                if (target.Integers != null && target.Integers.Any(value => longValue >= value)) {
                    return true;
                }
                break;
            // pvp_win_score: the winning team's lead in points, at most the target (21300018: a win on Treasure Island by a lead
            // of 50 points at most, target 50). pvp_win_time: the fight's seconds, at most the target (21300019:
            // a win there inside 5 minutes, target 300).
            case ConditionType.pvp_win_score:
            case ConditionType.pvp_win_time:
                if (target.Integers != null && target.Integers.Any(value => longValue <= value)) {
                    return true;
                }
                break;
            // npc_timeattack: the seconds since the dungeon began, at most the target (21250001-3: the boss of an
            // adventure dungeon inside 8 minutes, target 480).
            case ConditionType.npc_timeattack:
                if (target.Integers != null && target.Integers.Any(value => longValue <= value)) {
                    return true;
                }
                break;
            // npc_lasthit_time: the killing blow's local time, HH:mm (21230022: Alpha Turtle at 11:59 PM, target "23:59").
            case ConditionType.npc_lasthit_time:
                if (target.Strings != null && target.Strings.Contains(stringValue)) {
                    return true;
                }
                break;
            // npc_lasthit_buff: a buff the killer holds at the killing blow (22200343: while poisoned by Havi, 49116002).
            case ConditionType.npc_lasthit_buff:
                if (target.Integers != null && target.Integers.Any(buffId => session.Buffs.EnumerateBuffs(buffId).Count > 0)) {
                    return true;
                }
                break;
            // pvp_win_with_grade: the rank duel grade after the win, the target or higher (21300037-21300041 ask for
            // the grades 3 to 7, gold to grand champion, the grade ids of
            // the grade name strings and of the pvprankingduelmode table). Chosen here: a higher grade holds it too.
            case ConditionType.pvp_win_with_grade:
                if (target.Integers != null && target.Integers.Any(value => longValue >= value)) {
                    return true;
                }
                break;
            // pvp_win_with_buff: an effect the winner holds (21300035: a win in rage mode, target 90001010, the PvP
            // rage effect); the caller raises the win once per effect held.
            case ConditionType.pvp_win_with_buff:
                if (target.Integers != null && target.Integers.Contains((int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.item_move:
                // The target is the id of the box the item goes on (902, 905, 906 in one region), not a minimum:
                // the client's CConditionParam::Matches finds the value in the id list.
                if (target.Integers != null && target.Integers.Any(value => longValue == value)) {
                    return true;
                }
                break;
            case ConditionType.npc:
                // The target is the map of the kill; quest 71000065 lists the maps 3000001-3999999 as a range.
                if (target.Range != null && InRange((ConditionMetadata.Range<int>) target.Range, (int) longValue)) {
                    return true;
                }

                // A listed map must be the map of the kill: the client's CConditionParam::Matches
                // (conditionutil.cpp, 0x14029e600) finds the value in the id list, and a target of "0" is no target.
                if (target.Integers != null && target.Integers.Any(value => value == 0 || value == longValue)) {
                    return true;
                }
                break;
            case ConditionType.swim:
            case ConditionType.swimtime:
            case ConditionType.run:
            case ConditionType.crawl:
            case ConditionType.glide:
            case ConditionType.climb:
            case ConditionType.fall:
            case ConditionType.ropetime:
            case ConditionType.laddertime:
            case ConditionType.holdtime:
            case ConditionType.riding:
            case ConditionType.skill:
            // fish, fish_big: the map of the catch; every quest and trophy target of the two is a map id.
            case ConditionType.fish:
            case ConditionType.fish_big:
            case ConditionType.music_play_instrument_time:
            case ConditionType.music_play_score:
            case ConditionType.music_play_ensemble_in:
            case ConditionType.revise_achieve_multi_grade:
            case ConditionType.revise_achieve_single_grade:
            case ConditionType.hero_achieve:
            case ConditionType.chat:
            case ConditionType.character_ability_learn:
                if (target.Range != null && InRange((ConditionMetadata.Range<int>) target.Range, (int) longValue)) {
                    return true;
                }

                if (target.Integers != null && target.Integers.Contains((int) longValue)) {
                    return true;
                }
                break;
            case ConditionType.map:
            case ConditionType.jump:
            case ConditionType.meso:
            case ConditionType.taxifind:
            case ConditionType.trophy_point:
            case ConditionType.interact_object:
            case ConditionType.gemstone_upgrade_success:
            case ConditionType.gemstone_upgrade_try:
            case ConditionType.socket_unlock_success:
            case ConditionType.socket_unlock_try:
            case ConditionType.gemstone_puton:
            case ConditionType.gemstone_putoff:
            case ConditionType.fish_fail:
            case ConditionType.fish_collect:
            case ConditionType.fish_goldmedal:
            case ConditionType.fisher_grade:
            case ConditionType.mastery_grade:
            case ConditionType.set_mastery_grade:
            case ConditionType.music_play_grade:
            case ConditionType.music_play_ensemble:
            case ConditionType.music_play_instrument_mastery:
            case ConditionType.item_add:
            case ConditionType.item_pickup:
            case ConditionType.item_destroy:
            case ConditionType.beauty_add:
            case ConditionType.beauty_change_color:
            case ConditionType.beauty_random:
            case ConditionType.beauty_style_add:
            case ConditionType.beauty_style_apply:
            case ConditionType.trigger:
            case ConditionType.mastery_gathering:
            case ConditionType.mastery_gathering_try:
            case ConditionType.mastery_harvest:
            case ConditionType.mastery_harvest_try:
            case ConditionType.mastery_farming_try:
            case ConditionType.mastery_farming:
            case ConditionType.mastery_harvest_otherhouse:
            case ConditionType.mastery_manufacturing:
            case ConditionType.quest_accept:
            case ConditionType.quest_clear_by_chapter:
            case ConditionType.quest_clear:
            case ConditionType.buff:
            case ConditionType.dialogue:
            case ConditionType.talk_in:
            case ConditionType.change_profile:
            case ConditionType.buddy_request:
            case ConditionType.job:
            case ConditionType.job_change:
            case ConditionType.fall_survive:
            case ConditionType.fall_die:
            case ConditionType.install_item:
            case ConditionType.rotate_cube:
            case ConditionType.uninstall_item:
            case ConditionType.buy_house:
            case ConditionType.extend_house:
            case ConditionType.exp:
            case ConditionType.item_merge_success:
            case ConditionType.wedding_propose:
            case ConditionType.wedding_propose_decline:
            case ConditionType.wedding_propose_declined:
            case ConditionType.wedding_hall_cancel:
            case ConditionType.explore_continent:
            case ConditionType.continent:
            case ConditionType.explore:
            case ConditionType.item_gear_score:
            case ConditionType.killcount:
            case ConditionType.use_merat:
            case ConditionType.home_goto:
            case ConditionType.revival:
            case ConditionType.home_doctor:
            case ConditionType.resolve_panelty:
            case ConditionType.hit_tombstone:
            case ConditionType.hero_achieve_grade:
            case ConditionType.taxiuse:
            case ConditionType.taxifee:
            case ConditionType.send_mail:
            case ConditionType.change_ugc_equip:
            case ConditionType.unlimited_enchant:
            case ConditionType.openItemBox:
            case ConditionType.quest_daily:
            case ConditionType.quest_guide:
            case ConditionType.enter_otherhouse:
            case ConditionType.playtime:
            case ConditionType.shop_buy:
            case ConditionType.shop_buy_karma_token:
            case ConditionType.shop_buy_honor_token:
                return true;
        }
        return false;
    }

    private static bool InRange(ConditionMetadata.Range<int> range, int value) {
        return value >= range.Min && value <= range.Max;
    }
}
