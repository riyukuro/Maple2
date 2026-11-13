using System.Numerics;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Tools.Collision;

namespace Maple2.Server.Game.Util;

public static class SkillUtils {
    // An attack's area at position facing angle, placed as the client places its collision shape (StateSkill
    // FUN_1417e95a0: DetectWithEulerRotation at the position plus the range offset turned to the facing, rotated by
    // the facing plus the range's z rotation). An attack of method type 1 takes neither the offset nor the rotation.
    public static Prism GetPrism(this SkillMetadataAttack attack, in Vector3 position, float angle,
            ApplyTargetType applyTarget = ApplyTargetType.None) =>
        attack.Range.GetPrism(position, angle, applyTarget, attack.AttackMethodType != 1);

    public static Prism GetPrism(this SkillMetadataRange range, in Vector3 position, float angle, ApplyTargetType applyTarget, bool placed = true) {
        Vector3 offset = placed ? TurnOffset(range.RangeOffset, angle) : Vector3.Zero;
        float adjustedAngle = angle + (placed ? range.RotateZDegree : 0) + 180;
        var origin = new Vector2(position.X + offset.X, position.Y + offset.Y);
        float bottom = position.Z + offset.Z;
        // Region buffs use the same box shape but are centered on the target instead of projecting forward from the caster.
        if (range.Type == SkillRegion.Box && applyTarget is ApplyTargetType.RegionBuff or ApplyTargetType.RegionBuff2) {
            float length = range.Distance + range.RangeAdd.Y;
            float width = range.Width + range.RangeAdd.X;
            return new Prism(new Rectangle(origin, width, length, adjustedAngle), bottom, range.Height + range.RangeAdd.Z);
        }
        if (range.Type == SkillRegion.None) {
            return new Prism(IPolygon.Null, 0, 0);
        }

        float boxWidth = range.Width + range.RangeAdd.X;
        IPolygon polygon = range.Type switch {
            // Box projects forward from caster (0 to distance), same as Frustum but with equal widths.
            SkillRegion.Box => new Trapezoid(origin, boxWidth, boxWidth, range.Distance + range.RangeAdd.Y, adjustedAngle),
            SkillRegion.Cylinder => new Circle(origin, range.Distance),
            SkillRegion.Frustum => new Trapezoid(origin, range.Width, range.EndWidth, range.Distance, adjustedAngle),
            SkillRegion.HoleCylinder => new HoleCircle(origin, range.Width, range.EndWidth),
            _ => throw new ArgumentOutOfRangeException($"Invalid range type: {range.Type}"),
        };

        return new Prism(polygon, bottom, range.Height + range.RangeAdd.Z);
    }

    public static Prism GetPrism(this SkillMetadataRange range, in Vector3 position, float angle) =>
        range.GetPrism(position, angle, ApplyTargetType.None);

    // A region skill's area (FieldSkill): the client never searches for these, so the offset is added as it is, not
    // turned to the facing, and the area is rotated by the angle plus the range's z rotation.
    public static Prism GetRegionPrism(this SkillMetadataAttack attack, in Vector3 position, float angle) =>
        attack.Range.GetPrism(position + attack.Range.RangeOffset, angle + attack.Range.RotateZDegree, attack.Range.ApplyTarget, placed: false);

    // The client's search frame for an attack cast at position facing angle (StateSkill FUN_1417e95a0 setting up the
    // detect context): the origin is the position plus the range offset turned to the facing, the facing is the angle
    // plus the range's z rotation, and the order is the attack's direction. An attack of method type 1 (its arrow
    // hits) takes neither the offset nor the z rotation.
    public static TargetSort GetTargetSort(this SkillMetadataAttack attack, in Vector3 position, float angle) =>
        attack.AttackMethodType == 1
            ? new TargetSort(attack.Direction ?? 3, position, angle)
            : attack.Range.GetTargetSort(attack.Direction ?? 3, position, angle);

    // The client turns the offset by MakeZRotation(pi - r) (r the facing in radians), so its y runs along the facing
    // (sin r, -cos r) and its x along the right axis (-cos r, -sin r).
    public static TargetSort GetTargetSort(this SkillMetadataRange range, int direction, in Vector3 position, float angle) =>
        new(direction, position + TurnOffset(range.RangeOffset, angle), angle + range.RotateZDegree);

    public static Vector3 TurnOffset(in Vector3 offset, float angle) {
        float radians = angle * MathF.PI / 180;
        float sin = MathF.Sin(radians), cos = MathF.Cos(radians);
        return new Vector3(-offset.X * cos + offset.Y * sin, -offset.X * sin - offset.Y * cos, offset.Z);
    }

    public static IEnumerable<T> Filter<T>(this Prism prism, IEnumerable<T> entities, int limit = 10) where T : IActor {
        HashSet<int> addedActorObjectIds = [];
        foreach (T entity in entities) {
            if (limit <= 0) {
                yield break;
            }

            if (addedActorObjectIds.Contains(entity.ObjectId)) {
                continue;
            }

            if (entity.IsDead && entity is not FieldNpc { IsCorpse: true }) {
                continue;
            }

            IPrism shape = entity.Shape;
            if (prism.Intersects(shape)) {
                addedActorObjectIds.Add(entity.ObjectId);
                limit--;
                yield return entity;
            }
        }
    }

    public static IEnumerable<T> Filter<T>(this Prism[] prisms, IEnumerable<T> entities, int limit = 10, ICollection<IActor>? ignore = null) where T : IActor {
        HashSet<int> addedActorObjectIds = [];
        foreach (T entity in entities) {
            if (limit <= 0) {
                yield break;
            }

            if (addedActorObjectIds.Contains(entity.ObjectId)) {
                continue;
            }

            if (entity.IsDead && entity is not FieldNpc { IsCorpse: true }) {
                continue;
            }
            if (ignore != null && ignore.Contains(entity)) {
                continue;
            }

            IPrism shape = entity.Shape;
            foreach (Prism prism in prisms) {
                if (prism.Intersects(shape)) {
                    limit--;
                    addedActorObjectIds.Add(entity.ObjectId);
                    yield return entity;
                }
            }
        }
    }

    public static bool Check(this BeginCondition condition, IActor caster, IActor owner, IActor target, EventConditionType eventType = EventConditionType.Activate, int eventSkillId = 0, int eventBuffId = 0, IActor? ownedBy = null) {
        if (condition.PvpZone != PvpZoneApply.Everywhere && !PvpZoneHolds(condition.PvpZone, owner.Field.Metadata.Property.Type)) {
            return false;
        }
        if (caster is FieldPlayer player) {
            if (condition is not { Probability: 1 } && condition.Probability < Random.Shared.NextDouble()) {
                return false;
            }
            if (!condition.AllowDead && caster.IsDead) {
                return false;
            }
            if (condition.OnlyShadowWorld && caster.Field.Metadata.Property.Type != MapType.Shadow) {
                return false;
            }
            if (condition.OnlyFlyableMap && !caster.Field.Metadata.Property.CanFly) {
                return false;
            }
            if (!condition.AllowOnSurvival && (caster.Field.Metadata.Property.Type is MapType.SurvivalTeam or MapType.SurvivalSolo)) {
                return false;
            }
            if (player.Value.Character.Level < condition.Level) {
                return false;
            }
            if (player.Session.Currency.Meso < condition.Mesos) {
                return false;
            }
            if (condition.Gender != Gender.All && player.Value.Character.Gender != condition.Gender) {
                return false;
            }
            if (condition.JobCode.Length > 0 && !condition.JobCode.Contains(player.Value.Character.Job.Code())) {
                return false;
            }
            if (condition.Weapon?.Length > 0) {
                if (!condition.Weapon.Any(weapon => weapon.Check(player))) {
                    return false;
                }
            }
            foreach ((BasicAttribute stat, long value) in condition.Stat) {
                if (player.Stats.Values[stat].Total < value) {
                    return false;
                }
            }
            if (condition.DurationWithoutMoving > 0) {
                if (player.PositionTick.Duration < condition.DurationWithoutMoving) {
                    return false;
                }
            }
            if (condition.Maps.Length > 0 && !condition.Maps.Contains(caster.Field.MapId)) {
                return false;
            }
            if (condition.MapTypes.Length > 0 && !condition.MapTypes.Contains(caster.Field.Metadata.Property.Type)) {
                return false;
            }
            if (condition.Continents.Length > 0 && !condition.Continents.Contains(caster.Field.Metadata.Property.Continent)) {
                return false;
            }
            if (condition.DungeonGroupType.Length > 0 &&
                (caster.Field is not DungeonFieldManager dungeonFieldManager ||
                 !condition.DungeonGroupType.Contains(dungeonFieldManager.DungeonMetadata.GroupType))) {
                return false;
            }
            if (condition.ActiveSkill.Length > 0 && condition.ActiveSkill.All(id => owner.Animation.Current?.Skill?.Id != id)) {
                return false;
            }
            if (condition.OnlyOnBattleMount && player.Session.Ride.Ride?.Metadata.Basic.Type != RideOnType.Battle) {
                return false;
            }
            if (!condition.AllowOnBattleMount && player.Session.Ride.Ride?.Metadata.Basic.Type == RideOnType.Battle) {
                return false;
            }
        }

        ownedBy ??= caster;
        return condition.Caster.Check(caster, ownedBy, eventType, eventSkillId, eventBuffId) && condition.Owner.Check(owner, ownedBy, eventType, eventSkillId, eventBuffId) && condition.Target.Check(target, ownedBy, eventType, eventSkillId, eventBuffId);
    }

    // applyPvPZoneType (4,823 effect levels on 1,699 effects; no skill uses it): 1 holds outside PvP zones, 2 only in
    // them. The client loads it into its effect level data (AdditionalLevelData::LoadCondition @0x1401757f0) and
    // reads it nowhere else, so the server checks it. A PvP zone is a map of type Pvp (arenas and battlefields), as
    // for clearEffectFromPVPZone on entering one (BuffManager.EnterField); chosen here: the holder's map, for npcs
    // too. Before, both halves of a PvE/PvP pair (10100151 and 10100152) applied everywhere at once.
    internal static bool PvpZoneHolds(PvpZoneApply apply, MapType mapType) {
        return apply switch {
            PvpZoneApply.OutsidePvp => mapType != MapType.Pvp,
            PvpZoneApply.InsidePvp => mapType == MapType.Pvp,
            _ => true,
        };
    }

    // hasBuffOwner (Owned): the buff must be one ownedBy cast, the condition's caster or, for a buff's event skills, that
    // buff's caster. The client only loads the flag; its one reader of the same field family, the range select's
    // targetHasBuffOwner, takes a buff cast by the searching actor (CBaseCollision, HasEnabledWithIdAndCaster). Before,
    // the test compared the holder's object id with 0 and never failed.
    private static bool Check(this BeginConditionTarget? condition, IActor target, IActor ownedBy, EventConditionType eventType = EventConditionType.Activate, int eventSkillId = 0, int eventBuffId = 0) {
        if (condition == null) {
            return true;
        }

        if (condition.Event.Type != eventType) {
            return false;
        }

        foreach ((int id, short level, bool owned, int count, CompareType compare) in condition.Buff) {
            List<Buff> buffs = target.Buffs.EnumerateBuffs(id);
            bool validBuff = false;
            foreach (Buff buff in buffs) {
                if (buff.Level < level) {
                    continue;
                }
                if (owned && buff.Caster.ObjectId != ownedBy.ObjectId) {
                    continue;
                }

                if (count > 0) {
                    bool compareResult = compare switch {
                        CompareType.Equals => buff.Stacks == count,
                        CompareType.Less => buff.Stacks < count,
                        CompareType.LessEquals => buff.Stacks <= count,
                        CompareType.Greater => buff.Stacks > count,
                        CompareType.GreaterEquals => buff.Stacks >= count,
                        _ => true,
                    };

                    if (!compareResult) {
                        continue;
                    }
                }

                validBuff = true;
                break;
            }
            if (!validBuff) {
                return false;
            }
        }

        foreach ((BasicAttribute attribute, float value, CompareType compare, CompareStatValueType valueType) in condition.Stat) {
            float targetValue = valueType switch {
                CompareStatValueType.CurrentPercentage when target.Stats.Values[attribute].Total == 0 => 0,
                CompareStatValueType.CurrentPercentage => (float) target.Stats.Values[attribute].Current / target.Stats.Values[attribute].Total,
                CompareStatValueType.TotalValue => target.Stats.Values[attribute].Total,
                _ => 0,
            };

            bool compareResult = compare switch {
                CompareType.Equals => targetValue == value,
                CompareType.Less => targetValue < value,
                CompareType.LessEquals => targetValue <= value,
                CompareType.Greater => targetValue > value,
                CompareType.GreaterEquals => targetValue >= value,
                _ => true,
            };
            if (!compareResult) {
                return false;
            }
        }

        // hasNotBuffOwner (HasNotBuffOwned): only a buff ownedBy cast fails the test, as hasBuffOwner counts only one.
        // Only an enabled copy fails it, as the client's buff tests count only enabled records (HasEnabledWithId /
        // HasEnabledWithIdAndCaster, basecollision.cpp:333-336) and as RangeTargetSelect.Excludes does.
        if (condition.HasNotBuffIds.Length > 0 && condition.HasNotBuffIds.Any(id => target.Buffs.EnumerateBuffs(id)
                .Any(buff => buff.Enabled && (!condition.HasNotBuffOwned || buff.Caster.ObjectId == ownedBy.ObjectId)))) {
            return false;
        }

        if (condition.InRange is { } inRange && !InRangeHolds(inRange, target)) {
            return false;
        }

        // NpcRanks (KR: 7 on 90000499's proc): only an npc of a listed class. No client reader; chosen here.
        if (condition.NpcClasses is { Length: > 0 } classes && !(target is FieldNpc ranked && classes.Contains(ranked.Value.Metadata.Basic.Class))) {
            return false;
        }

        // Verify if these conditions are only for player.
        if (target is FieldPlayer player) {
            if (condition.States.Length > 0 && !condition.States.Contains(player.State)) {
                return false;
            }

            if (condition.SubStates.Length > 0 && !condition.SubStates.Contains(player.SubState)) {
                return false;
            }

            if (condition.Masteries.Count > 0 && !condition.Masteries.All(mastery => player.Session.Mastery[mastery.Key] >= mastery.Value)) {
                return false;
            }
        } else if (target is FieldNpc npc) {
            if (condition.NpcIds.Length > 0 && !condition.NpcIds.Contains(npc.Value.Id)) {
                return false;
            }
        }

        if (condition.Event.BuffIds.Length > 0 && !condition.Event.BuffIds.Contains(eventBuffId)) {
            return false;
        }
        if (condition.Event.SkillIds.Length > 0 && !condition.Event.SkillIds.Contains(eventSkillId)) {
            return false;
        }

        return true;
    }

    // targetCheckRange / targetInRangeCount / targetFriendly / targetCountSign: the count of the checked actor's
    // enemies (friendly 1) or allies (2) within the range, compared with the count. The client loads the check
    // (Unk_SkillOverlapCondition) and never evaluates it, so the server does. Tooltips give the meaning: 90050886
    // (900, 4, enemies, GreaterEquals) is "4 or more hostile targets within 9 m, combat power +520", 90051222 (1000,
    // 1, Equals) "active when one hostile target is within 10 m", 10500111 (400, 0, Equals) spirit regen with no
    // enemy within 4 m. A held buff re-checks it on each update (Buff.UpdateEnabled), so it turns on and off as
    // actors come and go. Chosen here: distance between positions, dead actors and the checked actor not counted.
    internal static bool InRangeHolds(BeginConditionTarget.TargetCheck inRange, IActor target) {
        int count = target.Field.CountAround(target, inRange.MinRangeDistance, inRange.RangeDistance, inRange.Friendly != 2);
        return CountHolds(inRange.Compare, count, inRange.RangeCount);
    }

    internal static bool CountHolds(CompareType compare, int count, int wanted) => compare switch {
        CompareType.Equals => count == wanted,
        CompareType.Less => count < wanted,
        CompareType.LessEquals => count <= wanted,
        CompareType.Greater => count > wanted,
        CompareType.GreaterEquals => count >= wanted,
        _ => false,
    };

    private static bool Check(this BeginConditionWeapon weapon, FieldPlayer player) {
        return IsValid(weapon.LeftHand, EquipSlot.LH) && IsValid(weapon.RightHand, EquipSlot.RH);

        bool IsValid(ItemType itemType, EquipSlot slot) {
            if (itemType.Type == 0) return true;
            Item? handItem = player.Session.Item.Equips.Get(slot);
            return handItem != null && handItem.Type.Type == itemType.Type;
        }
    }
}
