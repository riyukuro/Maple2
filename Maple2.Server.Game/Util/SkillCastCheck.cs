using System.Numerics;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Enum;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;
using Maple2.Tools.Extensions;

using Serilog;

namespace Maple2.Server.Game.Util;

// The checks a cast passes before any cost is paid, for a skill use (Skill 0x20, SkillHandler) and a state skill
// (StateSkill 0x21, StateSkillHandler). The client sends both from the same use path (MyPC::vfunc_102, then
// StateSkill::FUN_1417ea570 picks 0x21 for a skill whose main type has a state), after the same checks
// (CSkillSystem::FUN_141769b70 and FUN_14176a8c0), so the server checks both alike. Only the cast timing is left out
// for a state skill: the server keeps no cast time for one.
public static class SkillCastCheck {
    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(SkillCastCheck));

    // The home row that stops a first-motion cast of the main type for this player, or null. The client asks it on
    // the base skill (CreateSkillRecord); the server has the cast id, whose main type is the same for every KR combo.
    public static HomePermission? HomeRowStops(GameSession session, int mainType, bool laterMotion) {
        if (laterMotion || HomePermissionRules.SkillRow(mainType) is not { } row
            || !HomePermissionRules.SkillRowApplies(row, AdminLevel.Of(session.Player.Value.Account.AdminPermissions))) {
            return null;
        }
        return HomeRowCheck.Blocks(session, row) ? row : null;
    }

    // Checked before any cost is paid. A dead player casts only a skill that allows it, and no one casts a skill
    // that a buff held disables or that names other fields, or a skill that takes the caster off the ride while on it;
    // no first motion comes without the buffs the condition requires or with a buff of a group it refuses. A skill of a job's tree
    // must be in the caster's own tree at that level or above; a skill of no tree must be one some client path gives a player. An item skill must also
    // be off cooldown, its own and its cooldown group's; a job skill must not come too early after its last cast. Every skill must
    // find its hand weapons held and its spirit threshold met.
    // No cast comes before the motion of the last cast lets the next one in. castTick null (a state skill) leaves
    // the timing checks out.
    public static bool Allowed(GameSession session, SkillRecord record, long tick, int? castTick, out SkillCooldown? blocking) {
        blocking = null;
        BeginCondition condition = record.Metadata.Data.Condition;
        if (SkillCastRules.RefusedDead(session.Player.IsDead, condition.AllowDead)) {
            return false;
        }
        SkillRecord? held = session.Player.ActiveSkills.Get(record.CastUid);
        bool laterMotion = SkillCastRules.LaterMotion(record.MotionPoint, record.SkillId, held?.SkillId, held?.MotionPoint ?? 0);
        if (SkillCastRules.TypeDisabled(session.Player.Buffs.SkillUseDisableMask(), SkillCastRules.FieldTypeMask(session.Field.Metadata.Limit.DisableSkills),
                record.Metadata.Property.MainType, laterMotion)) {
            Logger.Debug("Cast refused, skill {SkillId} of main type {MainType} is disabled by a buff held or the field", record.SkillId, record.Metadata.Property.MainType);
            return false;
        }
        // A home's row for the skill's main type (potion, glide, climb or a learned skill) stops it there for the
        // player it covers (HomeRowCheck): checked on the first motion only, as the client checks it before the cast.
        if (HomeRowStops(session, record.Metadata.Property.MainType, laterMotion) is { } row) {
            Logger.Debug("Cast refused, skill {SkillId}: the home's {Row} row stops it", record.SkillId, row);
            return false;
        }
        if (SkillCastRules.StateFieldRefusal(SkillCastRules.ExpectedState(record.Metadata.Property.MainType, record.Metadata.Property.State), laterMotion,
                session.Field.Metadata.Property.CanFly, session.Field.Metadata.Property.CanClimb,
                AdminLevel.Of(session.Player.Value.Account.AdminPermissions)) is { } fieldAnswer) {
            Logger.Debug("Cast refused, skill {SkillId}: field {MapId} answers {Answer:X} for its state", record.SkillId, session.Field.MapId, fieldAnswer);
            return false;
        }
        // The same holds for a buff that disables the skill itself (answer 0x1f): checked on the first motion only.
        if (!laterMotion && session.Player.Buffs.SkillUseDisabled(record.SkillId)) {
            Logger.Debug("Cast refused, a buff held disables skill {SkillId}", record.SkillId);
            return false;
        }
        if (SkillCastRules.MapRefuses(condition.Maps, session.Field.MapId)) {
            Logger.Debug("Cast refused, skill {SkillId} is not for field {MapId}", record.SkillId, session.Field.MapId);
            return false;
        }
        FieldPlayer player = session.Player;
        if (SkillCastRules.ConditionRefusal(record.SkillId, laterMotion, condition.OnlyShadowWorld, session.Field.Metadata.Property.Continent == Continent.ShadowWorld,
                condition.OnlyFlyableMap, session.Field.Metadata.Property.CanFly, condition.AllowOnBattleMount, condition.OnlyOnBattleMount,
                session.Ride.Ride?.Metadata.Basic.Type == RideOnType.Battle, player.Stats.Values[BasicAttribute.Health].Current) is { } answer) {
            Logger.Debug("Cast refused, skill {SkillId}: the client's begin condition answers {Answer:X}", record.SkillId, answer);
            return false;
        }
        if (SkillCastRules.EffectRefusal(record.SkillId, laterMotion, condition.RequireEffects, id => player.Buffs.HeldOrJustLost(id, tick),
                condition.NotEffectGroups, group => player.Buffs.GroupHeldSettled(group, tick)) is { } effectAnswer) {
            Logger.Debug("Cast refused, skill {SkillId}: the client's begin condition answers {Answer:X} for the buffs held", record.SkillId, effectAnswer);
            return false;
        }
        if (SkillCastRules.LapenshardFieldRefuses(record.SkillId, laterMotion, session.Field.Metadata.Property.Type == MapType.Pvp,
                ContentShutdownConfig.Contents.Contains(SkillCastRules.LapenshardSkillContent),
                session.Config.WornLapenshards().Select(itemId => session.ItemMetadata.TryGet(itemId, out ItemMetadata? item) ? item.Skill : null))) {
            Logger.Debug("Cast refused, skill {SkillId}: a worn shard gives it and field {MapId} is a PvP field", record.SkillId, session.Field.MapId);
            return false;
        }
        if (SkillCastRules.GuildPvpAwakeningRefuses(record.SkillId, laterMotion, session.Field.FieldInstance?.Type == InstanceType.GuildPvp,
                () => session.TableMetadata.JobTable.Entries.TryGetValue(session.Player.Value.Character.Job.Code(), out JobTable.Entry? job)
                    ? job.Skills.GetValueOrDefault(SkillRank.Awakening) : null)) {
            Logger.Debug("Cast refused, skill {SkillId} is an awakening skill and field {MapId} is a guild PvP field", record.SkillId, session.Field.MapId);
            return false;
        }
        if (SkillCastRules.HeldWeaponRefuses(record.SkillId, laterMotion, record.Metadata.Property.MainType, session.HeldLiftup?.SkillId)) {
            Logger.Debug("Cast refused, skill {SkillId} is an object weapon's skill and no weapon of it is held", record.SkillId);
            return false;
        }
        if (SkillCastRules.EpFieldRefuses(session.Field.Metadata.Limit.Dash, record.Metadata.Data.Consume.Stat.GetValueOrDefault(BasicAttribute.Stamina))) {
            Logger.Debug("Cast refused, skill {SkillId} costs EP and field {MapId} allows no EP skill", record.SkillId, session.Field.MapId);
            return false;
        }
        if (SkillCastRules.RideRefuses(record.Metadata.Property.UnrideOnUse, record.Metadata.Property.MainType,
                session.Ride.Ride is { Action: RideOnActionDefault } ride && ride.OwnerId == session.Player.ObjectId)) {
            Logger.Debug("Cast refused, skill {SkillId} is not cast from a ride", record.SkillId);
            return false;
        }
        SkillInfo skillInfo = session.Config.Skill.SkillInfo;
        bool inAnyTree = skillInfo.InAnyTree(record.SkillId);
        if (SkillCastRules.TreeRefuses(inAnyTree, skillInfo.TreeLevel(record.SkillId), record.Level)) {
            Logger.Debug("Cast refused, skill {SkillId} level {Level} is not in the tree of {Job} at that level", record.SkillId, record.Level, skillInfo.Job);
            return false;
        }
        if (SkillCastRules.SourceRefuses(inAnyTree, record.Metadata.Property.NoPlayerSource)) {
            Logger.Debug("Cast refused, no client path gives skill {SkillId} to a player", record.SkillId);
            return false;
        }
        if (SkillCastRules.RideSetRefuses(laterMotion, inAnyTree, record.Metadata.Property.RideSkillSets, session.Ride.Ride?.Metadata.Basic.SkillSetId)) {
            Logger.Debug("Cast refused, skill {SkillId} is a mount's skill and the caster is not on such a mount", record.SkillId);
            return false;
        }
        if (!laterMotion && !inAnyTree && SkillCastRules.ShardSourceRefuses(record.SkillId, record.Level, laterMotion, inAnyTree,
                ShardSkills(session).Contains(record.SkillId) && !SkillCastRules.NoLearnMainTypes.Contains(record.Metadata.Property.MainType),
                record.ItemUid > 0 && record.Level <= 1 && session.Item.Inventory.Get(record.ItemUid) is { } bagItem
                    && !bagItem.Type.IsLapenshard && bagItem.Metadata.Skill?.Id == record.SkillId,
                session.ServerTableMetadata.FeatureTable.IsOn(SkillCastRules.LapenshardFeature),
                ContentShutdownConfig.Contents.Contains(SkillCastRules.LapenshardSkillContent),
                session.Config.WornLapenshards().Select(itemId => session.ItemMetadata.TryGet(itemId, out ItemMetadata? item) ? item.Skill : null))) {
            Logger.Debug("Cast refused, skill {SkillId} level {Level} is a shard's skill and no worn shard gives it at that level", record.SkillId, record.Level);
            return false;
        }
        // A running ShutdownSkillItem event stops the skill of an item it lists (SkillCastRules.ItemShutdownRefuses).
        int? inventoryItemId = laterMotion || record.ItemUid <= 0 ? null : session.Item.Inventory.Get(record.ItemUid)?.Id;
        if (SkillCastRules.ItemShutdownRefuses(laterMotion, inventoryItemId, () => session.FindEvent(GameEventType.ShutdownSkillItem))) {
            Logger.Debug("Cast refused, skill {SkillId}: item {ItemId} is shut down by a running event", record.SkillId, inventoryItemId);
            return false;
        }
        bool useItem = record.Metadata.Data.Consume.UseItem;
        if (castTick is { } sentTick && !TimingAllows(session, record, useItem, inAnyTree, sentTick, tick)) {
            return false;
        }
        if (!HandsAndSpiritAllow(session, record)) {
            return false;
        }
        if (!useItem) {
            return true;
        }
        if (RecoveryFull(session, record) is { } full) {
            Logger.Debug("Cast refused, item skill {SkillId}: the client's check answers {Answer} for a full pool", record.SkillId, full);
            return false;
        }
        // The pack's beginCondition is not tested here: the client's cast check reads none of what only it holds
        // (allowMapleSurvival, level, gender, job, stats and the rest; SkillCondition::Load, skilloverlapcompare.cpp:522),
        // and what it does read is checked above (HandsAndSpiritAllow included). KR: 83 of the 94 item skills lack allowMapleSurvival, so every potion
        // was refused in the Maple Survival fields (82000000-82000003, map type 15).
        // A skill whose cooldown is not shown in the UI (useCoolDownShowUI 0) is cast by the client on cooldown when a
        // skill state picks it as its next record (a held quickslot key, MyPC::vfunc_104 under SetCastAllowed(1),
        // stateskill.cpp:2906): the cooldown check answers 0x23 only without that (CSkillSystem::FUN_14176a530). Such a
        // cast cannot be told from another here, so the cooldown refuses none. KR: 18 item skills (90000121 and the other
        // Maple Survival items with 3 s, 90000040 to 90000043 and others with 0.5 s).
        if (!condition.CooldownShownInUi) {
            return true;
        }
        blocking = session.Config.BlockingCooldown(record.Metadata, tick);
        return blocking == null;
    }

    // The skills of the shards with an itemLapenshardUpgrade row (SkillCastRules.ShardSourceRefuses), once per table.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LapenshardUpgradeTable, HashSet<int>> shardSkills = new();

    private static HashSet<int> ShardSkills(GameSession session) => shardSkills.GetValue(session.TableMetadata.LapenshardUpgradeTable,
        table => table.Entries.Keys.Select(itemId => session.ItemMetadata.TryGet(itemId, out ItemMetadata? item) ? item.Skill?.Id ?? 0 : 0)
            .Where(skillId => skillId > 0).ToHashSet());

    // The two timing checks (ConfigManager.CastTimingRefusal) refuse only when CastCheckSetting says so, else they log.
    private static bool TimingAllows(GameSession session, SkillRecord record, bool useItem, bool inAnyTree, int castTick, long tick) {
        string? early = session.Config.CastTimingRefusal(record.Metadata, !useItem && SkillCastRules.IsJobSkill(record.SkillId, inAnyTree), castTick, tick, out bool clockTurnedFast);
        if (clockTurnedFast) {
            Logger.Information("Fast clock: the cast ticks of {Player} ({CharacterId}) run ahead of the server's time or jump back", session.PlayerName, session.CharacterId);
        }
        if (early == null) {
            return true;
        }
        if (CastCheckSetting.Enforce) {
            Logger.Debug("Cast refused, skill {SkillId}, {Reason}", record.SkillId, early);
            return false;
        }
        if (session.Config.CastLog.Count(tick) is > 0 and int unlogged) {
            Logger.Information("Cast not refused (log only), skill {SkillId} of {Player}, {Reason} ({Count} since the last line)", record.SkillId, session.PlayerName, early, unlogged);
        }
        return true;
    }

    // The recovery effects of an item skill's level, tested against the caster's pools (SkillCastRules.RecoveryFullRefusal).
    private static int? RecoveryFull(GameSession session, SkillRecord record) {
        IEnumerable<AdditionalEffectMetadataRecovery?> recoveries = record.Metadata.Data.Skills
            .SelectMany(effect => effect.Skills)
            .Select(skill => session.SkillMetadata.TryGetEffect(skill.Id, skill.Level, out AdditionalEffectMetadata? metadata) ? metadata.Recovery : null);
        Stats stats = session.Player.Stats.Values;
        return SkillCastRules.RecoveryFullRefusal(recoveries, Pool(stats, BasicAttribute.Health), Pool(stats, BasicAttribute.Spirit), Pool(stats, BasicAttribute.Stamina));

        static (long, long) Pool(Stats stats, BasicAttribute attribute) => (stats[attribute].Current, stats[attribute].Total);
    }

    // A skill's weapon condition and spirit threshold, tested for every skill as the client tests them (FUN_14176a8c0;
    // no KR item skill has either).
    private static bool HandsAndSpiritAllow(GameSession session, SkillRecord record) {
        BeginCondition condition = record.Metadata.Data.Condition;
        if (condition.Weapon is { Length: > 0 } weapons) {
            Item? right = session.Item.Equips.Get(EquipSlot.RH);
            Item? left = session.Item.Equips.Get(EquipSlot.LH);
            (int heldRight, int heldLeft) = SkillCastRules.HandTypes(right?.Type.Type ?? 0, right?.Uid ?? 0, right == null || PcItemUse.CanUseItem(session, right),
                left?.Type.Type ?? 0, left?.Uid ?? 0, left == null || PcItemUse.CanUseItem(session, left));
            WeaponActionTable.Action? held = session.TableMetadata.WeaponActionTable.Held(heldRight, heldLeft);
            if (SkillCastRules.WeaponRefuses(weapons.Select(weapon => ((int) weapon.LeftHand.Type, (int) weapon.RightHand.Type)).ToArray(), held)) {
                Logger.Debug("Cast refused, skill {SkillId} needs another weapon than {Left}/{Right} (action {Action})", record.SkillId,
                    held?.LeftHand ?? heldLeft, held?.RightHand ?? heldRight, held == null ? "none" : "held");
                return false;
            }
        }
        if (condition.Stat.TryGetValue(BasicAttribute.Spirit, out long threshold)) {
            FieldPlayer player = session.Player;
            (int invokeValue, float invokeRate) = player.Buffs.GetInvokeValues(InvokeEffectType.ReduceSpiritCost, record.SkillId, record.Metadata.Property.SkillGroup);
            int reduced = SkillCastRules.SpiritCost(threshold, invokeValue, invokeRate, player.Buffs.ConsumeSpRate);
            if (SkillCastRules.SpiritThresholdRefuses(reduced, player.Stats.Values[BasicAttribute.Spirit].Current)) {
                Logger.Debug("Cast refused, skill {SkillId} needs {Spirit} spirit", record.SkillId, reduced);
                return false;
            }
        }
        return true;
    }
}
