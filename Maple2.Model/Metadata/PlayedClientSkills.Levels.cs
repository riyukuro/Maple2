using System.Globalization;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

// The skills and levels of the played client's skill data that the server's data pack lacks, built from the client's
// own <skill> as its loaders read it: the client casts them (and fires them from other skills) like any other, so the
// server needs their levels to take the cast. Each field is what the client's loader reads, with the client's default
// where the attribute is not given; what the client does not read (the pack's beginCondition jobs, stats and targets,
// change skill, auto targeting, recovery, upgrade, a level's conditionSkill) is left empty.
public static partial class PlayedClientSkills {
    // The skill's kinds, ui and stateAttr (SkillData::Load, commonfiles/skilloverlapcompare.cpp:814-874). The client's
    // data has no type, sub type, state or emotion; the server's checks of them see an active skill of none. The type
    // only sorts a job's skill tree into active and passive skills (SkillInfo), and none of these skills is in a tree.
    public static SkillMetadataProperty BuildProperty(XmlElement skill, short maxLevel) {
        XmlElement? basic = Child(skill, "basic");
        XmlElement? kinds = Child(basic, "kinds");
        XmlElement? ui = Child(basic, "ui");
        return new SkillMetadataProperty(
            Type: SkillType.Active,
            SubType: SkillSubType.Type0,
            RangeType: (RangeType) Number(kinds, "rangeType"),
            AttackType: (AttackType) Number(ui, "attackType"),
            Element: (Element) Number(kinds, "element"),
            State: ActorState.None,
            ContinueSkill: Number(kinds, "continue") != 0,
            SpRecoverySkill: false,
            ImmediateActive: Number(kinds, "immediateActive") != 0,
            UnrideOnHit: false,
            UnrideOnUse: Number(kinds, "unrideOnUse") != 0,
            ReleaseObjectWeapon: Number(kinds, "releaseObjectWeapon") != 0,
            SkillGroup: Number(kinds, "groupIDs"),
            MaxLevel: maxLevel,
            Emotion: "",
            MainType: Number(basic, "mainType"));
    }

    // The skill's stateAttr (SkillData::Load, skilloverlapcompare.cpp:852-871); the recharge share as Read takes it.
    public static SkillMetadataState BuildState(int id, XmlElement skill) {
        XmlElement? state = Child(Child(skill, "basic"), "stateAttr");
        int origin = Number(skill, "originID");
        return new SkillMetadataState(
            InBattle: Number(state, "battle") != 0,
            SuperArmor: (SuperArmor) Number(state, "superArmor"),
            UseInGameTime: Number(state, "useInGameTime") != 0,
            CooldownGroupId: Number(state, "cooldownGroupID"),
            IgnoreReduceCooldown: Number(state, "ignoreReduceCooldown") != 0,
            RechargeMaxCount: Number(state, "rechargeMaxCount"),
            RechargeShared: Number(state, "rechargeShareOrigin") != 0 && origin != 0 && origin != id,
            ClearCooldownOnPvpZone: Number(state, "clearCooldownFromPVPZone") != 0,
            SuperArmorApplyField: Number(state, "superArmorApplyFieldType"));
    }

    // One <level> of the skill. The condition is the skill's <condition> (SkillCondition::Load,
    // skilloverlapcompare.cpp:521): the shadow world, flyable map and battle ride flags, the map codes, the hand
    // weapons and the recharge gap, with the level's cooldown (SkillLevelData::Load, skillleveldata.cpp:923, in
    // milliseconds). requireEffect and hasNotAdditionalEffectGroup have no place in the server's condition and are left
    // out (UnreadConditionRows counts them). The level's sp, ep, meso and hpRate are its cost; detectProperty is only a
    // shape to the client (Unk_RangeShape), so only its distance is kept and the caster is not taken in.
    public static SkillMetadataLevel BuildLevel(XmlElement skill, XmlElement level) {
        XmlElement? condition = Child(Child(skill, "basic"), "condition");
        var stat = new Dictionary<BasicAttribute, long>();
        if (Number(level, "sp") is var sp and not 0) {
            stat[BasicAttribute.Spirit] = sp;
        }
        if (Number(level, "ep") is var ep and not 0) {
            stat[BasicAttribute.Stamina] = ep;
        }
        return new SkillMetadataLevel(
            Condition: new BeginCondition(
                Level: 1,
                Mesos: 0,
                Gender: Gender.All,
                JobCode: [],
                Probability: 1f,
                CooldownTime: Number(level, "cooldown") / 1000f,
                DurationWithoutDamage: 0,
                DurationWithoutMoving: 0,
                OnlyShadowWorld: Number(condition, "onlyShadowWorld") != 0,
                OnlyFlyableMap: Number(condition, "onlyFlyableMap") != 0,
                AllowDead: false,
                AllowOnBattleMount: Number(condition, "allowBattleRidingState") != 0,
                OnlyOnBattleMount: Number(condition, "onlyBattleRidingState") != 0,
                AllowOnSurvival: false,
                DungeonGroupType: [],
                Stat: new Dictionary<BasicAttribute, long>(),
                Maps: Children(condition, "requireMapCodes").Select(code => Number(code, "code")).ToArray(),
                MapTypes: [],
                Continents: [],
                ActiveSkill: [],
                Weapon: Children(condition, "weapon").Select(weapon => new BeginConditionWeapon(
                    new ItemType(1, (byte) Number(weapon, "lh")), new ItemType(1, (byte) Number(weapon, "rh")))).ToArray(),
                Target: null,
                Owner: null,
                Caster: null,
                RechargeGap: Real(condition, "defaultRechargingCooldownTime")),
            Change: null,
            AutoTargeting: null,
            Consume: new SkillMetadataConsume(
                Meso: Number(level, "meso"),
                UseItem: false,
                HpRate: Real(level, "hpRate"),
                Stat: stat),
            Detect: new SkillMetadataDetect(
                IncludeCaster: false,
                Distance: Real(Child(level, "detectProperty"), "distance")),
            Recovery: new SkillMetadataRecovery(0, 0f),
            Skills: [],
            Motions: Children(level, "motion").Select(BuildMotion).ToArray());
    }

    // Of a data pack skill's level values (in order), the one a level the pack lacks takes the rest from: the highest
    // below it, else the lowest above it; none when the pack has no level.
    public static short? NearestLevel(IReadOnlyList<short> packValues, short value) {
        short? below = null;
        foreach (short pack in packValues) {
            if (pack < value) below = pack;
            else return below ?? pack;
        }
        return below;
    }

    // A played client's level (BuildLevel) added to a data pack skill: the client's own condition fields (cooldown,
    // shadow world, flyable map, battle ride, map codes, hand weapons, recharge gap) and cost, and what the client's
    // level does not give from the pack's level: the rest of its begin condition (level, jobs, stats, targets) and
    // whether it is an item skill's level (consume useItem, which the cast checks its condition and cooldown for).
    public static SkillMetadataLevel WithPackLevel(SkillMetadataLevel built, SkillMetadataLevel pack) {
        BeginCondition client = built.Condition;
        return built with {
            Condition = pack.Condition with {
                CooldownTime = client.CooldownTime,
                OnlyShadowWorld = client.OnlyShadowWorld,
                OnlyFlyableMap = client.OnlyFlyableMap,
                AllowOnBattleMount = client.AllowOnBattleMount,
                OnlyOnBattleMount = client.OnlyOnBattleMount,
                Maps = client.Maps,
                Weapon = client.Weapon,
                RechargeGap = client.RechargeGap,
            },
            Consume = built.Consume with { UseItem = pack.Consume.UseItem },
        };
    }

    // The rows of the skill's <condition> the server's condition has no place for.
    public static int UnreadConditionRows(XmlElement skill) {
        XmlElement? condition = Child(Child(skill, "basic"), "condition");
        return Children(condition, "requireEffect").Count() + Children(condition, "hasNotAdditionalEffectGroup").Count();
    }

    // SkillMotionData::Load (skillleveldata.cpp:1159-1202; sequence speed 1 and faceTarget 1 when not given, the
    // ctor). The attack points as the data pack's (CollectAttackPoints): a point named twice is 0xFF.
    private static SkillMetadataMotion BuildMotion(XmlElement motion) {
        XmlElement[] attacks = Children(motion, "attack").ToArray();
        IReadOnlyDictionary<string, byte> points = AttackPoints(attacks.Select(attack => attack.GetAttribute("point")));
        return new SkillMetadataMotion(
            MotionProperty: new SkillMetadataMotionProperty(
                SequenceName: motion.GetAttribute("sequenceName"),
                SequenceSpeed: Real(motion, "sequenceSpeed", 1f),
                MoveDistance: Real(motion, "movedistance"),
                FaceTarget: Number(motion, "faceTarget", 1) != 0,
                MoveType: Number(motion, "movetype"),
                SplashLifeTick: Number(motion, "splashLifeTick"),
                SplashInvokeCoolTick: Number(motion, "splashInvokeCoolTick"),
                KeyMove: Real(motion, "keyMove")),
            Attacks: attacks.Select(BuildAttack).ToArray(),
            AttackPoints: points);
    }

    // The attack points of a motion's attacks in their order, as the data pack's (CollectAttackPoints): a point named
    // twice is 0xFF.
    public static IReadOnlyDictionary<string, byte> AttackPoints(IEnumerable<string> attackPoints) {
        var points = new Dictionary<string, byte>();
        int i = 0;
        foreach (string point in attackPoints) {
            if (i >= byte.MaxValue) break;
            points[point] = points.ContainsKey(point) ? byte.MaxValue : (byte) i;
            i++;
        }
        return points;
    }

    // The <attack> rows of the level's motion at that place (SkillMotionData::Load), where the level has as many
    // motions as the data pack's level; else none, the motions do not pair by place.
    public static XmlElement[]? MotionAttacks(XmlElement level, int motion, int motions) {
        XmlElement[] all = Children(level, "motion").ToArray();
        return all.Length == motions && motion < all.Length ? Children(all[motion], "attack").ToArray() : null;
    }

    // For each of the client's attacks of a motion, in its order, the place of the data pack's attack of that motion
    // it pairs with, or -1. They pair by point, in order: each client attack takes the first data pack attack with
    // its point after the last one taken. The client fires its own list (StateSkill takes the attack by its place in
    // the motion), so a data pack attack no client attack takes has no place in the cast.
    public static int[] AlignAttacks(IReadOnlyList<string> pack, IReadOnlyList<string> client) {
        var result = new int[client.Count];
        int next = 0;
        for (int j = 0; j < client.Count; j++) {
            result[j] = -1;
            for (int i = next; i < pack.Count; i++) {
                if (pack[i] == client[j]) {
                    result[j] = i;
                    next = i + 1;
                    break;
                }
            }
        }
        return result;
    }

    // The client attack's <actionSkill> rows (BuildSplash) none of whose skills is fired by the data pack's rows of
    // the attack: the splashes the client fires that the data pack's attack lacks.
    public static SkillEffectMetadata[] MissingSplashes(XmlElement attack, IEnumerable<SkillEffectMetadata> pack) {
        var packSkills = pack.SelectMany(effect => effect.Skills).Select(skill => skill.Id).ToHashSet();
        return Children(attack, "actionSkill").Select(BuildSplash)
            .Where(splash => splash.Skills.Length > 0 && splash.Skills.All(skill => !packSkills.Contains(skill.Id))).ToArray();
    }

    // SkillAttackData::Load (skillattackdata.cpp:573-622; direction 3 when not given, the ctor, which the server
    // keeps as null). Its <range> (Unk_RangeProperty::LoadElement), <arrow> (SkillArrow::Load), <damage>
    // (SkillDamage::Load) and <actionSkill> rows; the client has no pet taming and no skill on damage count.
    public static SkillMetadataAttack BuildAttack(XmlElement attack) {
        string point = attack.GetAttribute("point");
        int direction = Number(attack, "direction", 3);
        XmlElement? arrow = Child(attack, "arrow");
        var taken = new AttackArrow(Number(arrow, "overlap"), Number(arrow, "explosion"), Number(arrow, "rayPhysxTest"),
            Number(arrow, "nonTarget"), Number(arrow, "bounceType"), Number(arrow, "bounceCount"), Real(arrow, "bounceRadius"),
            Number(arrow, "bounceOverlap", 1), Point(arrow, "collision"), Point(arrow, "collisionAdd"), Number(arrow, "rayType"));
        int[] splashActive = Tokens(attack, "splashActiveSkillID").Where(id => id > 0).ToArray();
        return new SkillMetadataAttack(
            Point: point,
            PointGroup: Number(attack, "pointGroupID"),
            TargetCount: Number(attack, "targetCount"),
            MagicPathId: Number(attack, "magicPathID"),
            CubeMagicPathId: Number(attack, "cubeMagicPathID"),
            HitImmuneBreak: Number(attack, "hitImmuneBreak"),
            BrokenOffence: Number(attack, "brokenOffence"),
            CompulsionTypes: Tokens(attack, "compulsionType").Select(type => (CompulsionType) type).ToArray(),
            Pet: null,
            Range: BuildRange(Child(attack, "range")),
            Arrow: TakeArrow(taken, point, point)!,
            Damage: BuildDamage(Child(attack, "damage")),
            Skills: Children(attack, "actionSkill").Select(BuildSplash).ToArray(),
            SkillsOnDamage: [],
            GrabTargetType: Number(attack, "grabTargetType"),
            Direction: direction == 3 ? null : direction,
            AttackMethodType: Number(attack, "attackMethodType"),
            SplashActiveSkillIds: splashActive.Length > 0 ? splashActive : null);
    }

    // Unk_RangeProperty::LoadElement (unit_1408001f0_skillrangeandtarget.cpp:150): the shape, the target properties,
    // the buff, party and guild fields only when targetSelectType is set, the stat sort always, and the npc ranks to
    // leave out. The select as the data pack's (SkillMapper.ConvertSelect).
    private static SkillMetadataRange BuildRange(XmlElement? range) {
        int mode = Number(range, "targetSelectType");
        bool filters = mode != 0;
        int statSort = Number(range, "targetStatCompareFunction");
        int[] ranks = Tokens(range, "applyTargetIgnoreNpcRanks").ToArray();
        return new SkillMetadataRange(
            Type: ShapeType(range?.GetAttribute("rangeType") ?? ""),
            Distance: Real(range, "distance"),
            Height: Real(range, "height"),
            Width: Real(range, "width"),
            EndWidth: Real(range, "endWidth"),
            RotateZDegree: Real(range, "rangeZRotateDegree"),
            RangeAdd: Point(range, "rangeAdd"),
            RangeOffset: Point(range, "rangeOffset"),
            IncludeCaster: (IncludeCasterType) Number(range, "includeCaster"),
            ApplyTarget: (ApplyTargetType) Number(range, "applyTarget"),
            CastTarget: (SkillTargetType) Number(range, "castTarget"),
            Select: !filters && statSort == 0 ? null : new SkillMetadataRangeSelect(
                Mode: mode,
                BuffId: filters ? Number(range, "targetHasBuffID") : 0,
                BuffFromCaster: filters && Number(range, "targetHasBuffOwner") != 0,
                SameParty: filters && Number(range, "targetSelectParty") != 0,
                SameGuild: filters && Number(range, "targetSelectGuild") != 0,
                Stat: (BasicAttribute) Number(range, "targetStatCompare"),
                StatSort: statSort),
            IgnoreNpcClasses: ranks.Length > 0 ? ranks : null,
            HasNotBuffId: filters ? Number(range, "targetHasNotBuffID") : 0);
    }

    // SkillDamage::Load (skillattackdata.cpp:251-290; count 1, hit speed 1 and push probability 1 when not given, the
    // ctor at :175). The push as ReadAttackPushes takes it.
    private static SkillMetadataDamage BuildDamage(XmlElement? damage) {
        int push = Number(damage, "push");
        return new SkillMetadataDamage(
            Count: Number(damage, "count", 1),
            Rate: Real(damage, "rate"),
            HitSpeed: Real(damage, "hitSpeedRate", 1f),
            HitDelay: Real(damage, "hitPauseTime"),
            IsConstDamage: Number(damage, "isConstDamageValue") != 0,
            Value: Long(damage, "value"),
            DamageByTargetMaxHp: Real(damage, "damageByTargetMaxHP"),
            SuperArmorBreak: Number(damage, "superArmorBreak") != 0,
            Push: IsPush(push) ? new SkillMetadataPush(
                Type: push,
                EaseType: PushEaseType(Number(damage, "pushEaseType")),
                ApplyField: Number(damage, "pushApplyField"),
                Distance: Real(damage, "pushdistance"),
                UpDistance: Real(damage, "pushUpDistance"),
                Down: Number(damage, "pushDown"),
                Fall: Number(damage, "pushFall") != 0,
                Duration: Real(damage, "pushduration"),
                Probability: Real(damage, "pushprob", 1f),
                Priority: Number(damage, "pushPriority"),
                PriorityHitImmune: Number(damage, "pushPriorityHitImmune")) : null,
            ConstDamageByTargetHp: Long(damage, "constDamageByTargetHP"),
            ConstDamageByTargetSp: Long(damage, "constDamageByTargetSP"));
    }

    // SkillActionSkill::Load (skillleveldata.cpp:293; overlap count 1, fire count 1 and chain distance 150 when not
    // given, the ctor at :251). The client's attack actionSkill is a splash: it reads only the splash fields (no
    // begin condition, target or owner) and the data pack's conditionSkill rows of the same skills are splash ones.
    // The ids and levels pair as the data pack's do (TriggerSkill.Convert).
    private static SkillEffectMetadata BuildSplash(XmlElement action) {
        int[] ids = Tokens(action, "skillID").ToArray();
        int[] levels = Tokens(action, "level").ToArray();
        int[] links = Tokens(action, "linkSkillID").ToArray();
        SkillEffectMetadata.Skill[] skills = links.Length > 0
            ? ids.Zip(levels).Zip(links, (skill, link) => new SkillEffectMetadata.Skill(skill.First, (short) skill.Second, link)).ToArray()
            : ids.Zip(levels, (id, level) => new SkillEffectMetadata.Skill(id, (short) level)).ToArray();
        return new SkillEffectMetadata(
            FireCount: Number(action, "fireCount", 1),
            Skills: skills,
            Condition: null,
            Splash: new SkillEffectMetadataSplash(
                Interval: Number(action, "interval"),
                Delay: Number(action, "delay"),
                RemoveDelay: Number(action, "removeDelay"),
                UseDirection: Number(action, "useDirection") != 0,
                ImmediateActive: Number(action, "immediateActive") != 0,
                NonTargetActive: Number(action, "nonTargetActive") != 0,
                OnlySensingActive: Number(action, "onlySensingActive") != 0,
                DependOnCasterState: Number(action, "dependOnCasterState") != 0,
                Independent: Number(action, "independent") != 0,
                Chain: Number(action, "chain") != 0 ? new SkillEffectMetadataChain(Real(action, "chainDistance", 150f)) : null));
    }

    private static XmlElement? Child(XmlElement? element, string name) =>
        element?.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.Name == name);

    private static IEnumerable<XmlElement> Children(XmlElement? element, string name) =>
        element?.ChildNodes.OfType<XmlElement>().Where(child => child.Name == name) ?? [];

    // A comma list of numbers (XmlSplit, XmlTokenList): empty tokens skipped, each read by atoi.
    private static IEnumerable<int> Tokens(XmlElement? element, string attribute) =>
        (element?.GetAttribute(attribute) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(text => ScanInt(text, out int value) ? value : 0);

    // A 64-bit attribute (TiXml's QueryInt64 through sscanf "%lld"): the digits in front, the fallback otherwise.
    private static long Long(XmlElement? element, string attribute) {
        if (element == null || !element.HasAttribute(attribute)) {
            return 0;
        }
        string text = element.GetAttribute(attribute);
        int start = SkipSpace(text);
        int end = start < text.Length && text[start] is '+' or '-' ? start + 1 : start;
        int digits = end;
        while (end < text.Length && char.IsAsciiDigit(text[end])) {
            end++;
        }
        return end > digits && long.TryParse(text.AsSpan(start, end - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value)
            ? value : 0;
    }
}
