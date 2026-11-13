using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// The played client's skill-hit effects (skilldata <actionAdditional>) that the data pack's conditionSkill rows lack
// are added to the level and to the attack at their place, or the first of their point. Synthetic rows shaped like
// 10200062, 10200101 and 10100171 (rows on the second attack of a repeated point).
public class PlayedClientActionAdditionalsTests {
    private const string Client = """
        <ms2>
          <skill id="500">
            <level value="1">
              <actionAdditional additionalID="900" level="1" skillTarget="1"><beginCondition allowBattleRidingState="1" /></actionAdditional>
              <actionAdditional additionalID="901" level="1" skillTarget="1" />
              <actionAdditional additionalID="999" level="1" skillTarget="1" />
              <actionAdditional additionalID="903" level="1" skillTarget="1"><beginCondition jobCode="10" /></actionAdditional>
              <actionAdditional additionalID="904" level="1" skillTarget="1"><beginCondition allowBattleRidingState="True" onlyBattleRidingState="2"><!-- note --></beginCondition></actionAdditional>
              <motion>
                <attack point="p0">
                  <actionAdditional additionalID="910" level="1" skillTarget="2" skillOwner="1" dependOnDamageCount="1"><beginCondition probability="0.07" useTargetCountFactor="1" /></actionAdditional>
                  <actionAdditional additionalID="911" level="2" skillTarget="2" linkSkillID="777" />
                  <actionAdditional additionalID="901" level="1" skillTarget="2" />
                </attack>
                <attack point="p0">
                  <actionAdditional additionalID="912" level="1" skillTarget="2" />
                </attack>
                <attack point="p9">
                  <actionAdditional additionalID="913" level="1" skillTarget="2" />
                </attack>
                <attack point="p1">
                  <actionAdditional additionalID="914" level="1" skillTarget="2" />
                </attack>
              </motion>
            </level>
            <level value="2" />
          </skill>
        </ms2>
        """;

    private static readonly HashSet<(int, short)> Pack = [(900, 1), (901, 1), (903, 1), (910, 1), (911, 2), (912, 1), (913, 1), (914, 1), (904, 1), (920, 1)];

    private static SkillEffectMetadata Row(int id) => PlayedClientActionAdditionals.ToEffect(new PlayedClientActionAdditionals.Use(
        EffectId: id, Level: 1, Target: 1, Owner: 1, OnDamage: false, OverlapCount: 1, LinkSkillId: 0, AllowOnBattleMount: false, Probability: 1));

    private static SkillMetadataAttack Attack(string point, params SkillEffectMetadata[] skills) =>
        (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            Point = point,
            Skills = skills,
            SkillsOnDamage = [],
        };

    private static Dictionary<(int, short), PlayedClientActionAdditionals.LevelUses> Read(out int unsupported) {
        var document = new XmlDocument();
        document.LoadXml(Client);
        return PlayedClientActionAdditionals.Read([document], out unsupported);
    }

    [Test]
    public void ReadsTheLevelAndAttackRows() {
        Dictionary<(int, short), PlayedClientActionAdditionals.LevelUses> read = Read(out int unsupported);
        Assert.That(unsupported, Is.EqualTo(1), "the jobCode condition is not read");
        Assert.That(read.Keys, Is.EqualTo(new[] { (500, (short) 1) }), "a level with no rows is left out");
        PlayedClientActionAdditionals.LevelUses uses = read[(500, 1)];
        Assert.That(uses.Level.Select(use => use.EffectId), Is.EqualTo(new[] { 900, 901, 999, 904 }));
        Assert.That(uses.Level[0].AllowOnBattleMount, Is.True);
        Assert.That(uses.Level[0].OnlyOnBattleMount, Is.False);
        Assert.That((uses.Level[3].AllowOnBattleMount, uses.Level[3].OnlyOnBattleMount), Is.EqualTo((true, false)),
            "the battle-ride flags are the client's bools (\"True\" is set, \"2\" is not), and a comment is no condition field");
        Assert.That(uses.Attacks.Keys, Is.EqualTo(new[] { (0, 0), (0, 1), (0, 2), (0, 3) }));
        Assert.That(uses.Attacks[(0, 0)].Uses.Select(use => use.EffectId), Is.EqualTo(new[] { 910, 911, 901 }));
        Assert.That((uses.Attacks[(0, 1)].Point, uses.Attacks[(0, 1)].Uses[0].EffectId), Is.EqualTo(("p0", 912)));
        PlayedClientActionAdditionals.Use hit = uses.Attacks[(0, 0)].Uses[0];
        Assert.That((hit.OnDamage, hit.Probability, hit.Target), Is.EqualTo((true, 0.07f, 2)));
        Assert.That((uses.Attacks[(0, 0)].Uses[1].Level, uses.Attacks[(0, 0)].Uses[1].LinkSkillId), Is.EqualTo(((short) 2, 777)));
    }

    [Test]
    public void AddsOnlyTheRowsTheDataPackLacks() {
        PlayedClientActionAdditionals.LevelUses uses = Read(out _)[(500, 1)];
        // The data pack gives 900 for the level and 920 for the first p0 attack; the motion has two p0 attacks and a p1
        // (the client's p1 is its fourth attack, so it goes to the first p1).
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Skills = [Row(900)],
            Motions = [new SkillMetadataMotion(null!, [Attack("p0", Row(920)), Attack("p0"), Attack("p1")], new Dictionary<string, byte>())],
        };

        SkillMetadataLevel merged = PlayedClientActionAdditionals.Merge(level, uses, (id, lv) => Pack.Contains((id, lv)), out int added, out int unmatched);

        Assert.That(merged.Skills.Select(Id), Is.EqualTo(new[] { 900, 901, 904 }), "900 was given, 999 has no effect file");
        Assert.That((merged.Skills[2].Condition!.Condition.AllowOnBattleMount, merged.Skills[2].Condition!.Condition.OnlyOnBattleMount),
            Is.EqualTo((true, false)));
        SkillMetadataAttack first = merged.Motions[0].Attacks[0];
        Assert.That(first.Skills.Select(Id), Is.EqualTo(new[] { 920, 911 }), "901 is already the level's");
        Assert.That(first.SkillsOnDamage.Select(Id), Is.EqualTo(new[] { 910 }));
        Assert.That(merged.Motions[0].Attacks[1].Skills.Select(Id), Is.EqualTo(new[] { 912 }), "the second p0 attack's row stays on it");
        Assert.That(merged.Motions[0].Attacks[2].Skills.Select(Id), Is.EqualTo(new[] { 914 }));
        Assert.That((added, unmatched), Is.EqualTo((6, 1)), "913 has no p9 attack");

        SkillEffectMetadata onHit = first.SkillsOnDamage[0];
        Assert.That(onHit.Condition!.Condition.Probability, Is.EqualTo(0.07f));
        Assert.That((onHit.Condition.Target, onHit.Condition.Owner), Is.EqualTo(((SkillTargetType) 2, SkillTargetType.Owner)));
        Assert.That(first.Skills[1].Skills[0], Is.EqualTo(new SkillEffectMetadata.Skill(911, 2, 777)));
        Assert.That(merged.Skills[1].Condition!.Condition.AllowOnBattleMount, Is.False);
        Assert.That(level.Skills.Length, Is.EqualTo(1), "the input level is left as it was");
    }

    private static int Id(SkillEffectMetadata effect) => effect.Skills[0].Id;

    // The caster's and target's buff tests (SkillHasBuffCondition): one buff to have, with its count and owner flag, and
    // one not to have. Rows shaped like the played 10000020 and 10500061 ones; a field or child the client does not
    // read, or a second <target>, still leaves the row out.
    private const string Conditions = """
        <ms2>
          <skill id="600">
            <level value="1">
              <actionAdditional additionalID="930" level="1" skillTarget="1"><beginCondition><target hasBuffID="700" hasBuffCount="3" hasBuffOwner="1" /></beginCondition></actionAdditional>
              <actionAdditional additionalID="931" level="1" skillTarget="1"><beginCondition probability="0.5"><caster hasBuffID="701" /><target hasNotBuffID="702" hasNotBuffOwner="1" /></beginCondition></actionAdditional>
              <actionAdditional additionalID="932" level="1" skillTarget="1"><beginCondition><caster hasBuffID="0" hasBuffOwner="1" /></beginCondition></actionAdditional>
              <actionAdditional additionalID="933" level="1" skillTarget="1"><beginCondition><target hasBuffID="700" hasBuffLevel="2" /></beginCondition></actionAdditional>
              <actionAdditional additionalID="934" level="1" skillTarget="1"><beginCondition><owner hasBuffID="700" /></beginCondition></actionAdditional>
              <actionAdditional additionalID="935" level="1" skillTarget="1"><beginCondition><target hasBuffID="700" /><target hasBuffID="701" /></beginCondition></actionAdditional>
              <actionAdditional additionalID="936" level="1" skillTarget="1"><beginCondition><target hasBuffID="700"><stat /></target></beginCondition></actionAdditional>
            </level>
          </skill>
        </ms2>
        """;

    [Test]
    public void ReadsTheCasterAndTargetBuffTests() {
        var document = new XmlDocument();
        document.LoadXml(Conditions);
        PlayedClientActionAdditionals.Use[] uses = PlayedClientActionAdditionals.Read([document], out int unsupported)[(600, 1)].Level;
        Assert.That(unsupported, Is.EqualTo(4), "hasBuffLevel, <owner>, a second <target> and a child of <target> are not read");
        Assert.That(uses.Select(use => use.EffectId), Is.EqualTo(new[] { 930, 931, 932 }));
        Assert.That(uses[0].TargetTest, Is.EqualTo(new PlayedClientActionAdditionals.BuffTest(700, 3, true, 0, false)));
        Assert.That(uses[0].CasterTest, Is.Null);
        Assert.That((uses[1].CasterTest, uses[1].TargetTest, uses[1].Probability), Is.EqualTo((
            new PlayedClientActionAdditionals.BuffTest(701, 0, false, 0, false), new PlayedClientActionAdditionals.BuffTest(0, 0, false, 702, true), 0.5f)));

        BeginCondition first = PlayedClientActionAdditionals.ToEffect(uses[0]).Condition!.Condition;
        Assert.That(first.Caster, Is.Null);
        Assert.That(first.Target!.Buff, Is.EqualTo(new[] { new BeginConditionTarget.HasBuff(700, 0, true, 3, CompareType.Equals) }),
            "the data pack's rows give the same test: any level, the stacks equal to the count");
        Assert.That((first.Target.HasNotBuffIds, first.Target.Event.Type, first.Target.Stat), Is.EqualTo((new int[0], EventConditionType.Activate, new BeginConditionTarget.BeginConditionStat[0])));

        BeginCondition second = PlayedClientActionAdditionals.ToEffect(uses[1]).Condition!.Condition;
        Assert.That(second.Caster!.Buff.Select(buff => (buff.Id, buff.Count, buff.Owned)), Is.EqualTo(new[] { (701, 0, false) }));
        Assert.That((second.Target!.Buff, second.Target.HasNotBuffIds), Is.EqualTo((new BeginConditionTarget.HasBuff[0], new[] { 702 })));
        Assert.That((first.Target.HasNotBuffOwned, second.Target.HasNotBuffOwned), Is.EqualTo((false, true)), "hasNotBuffOwner is taken over");
        Assert.That(PlayedClientActionAdditionals.ToEffect(uses[2]).Condition!.Condition.Caster, Is.Null,
            "a test with no buff to have or not to have is none (the client's HasContent)");
    }

    // A data pack row with a hasNotBuffID: the played client's row of the same effect and level with hasNotBuffOwner on
    // the same buff marks it, on the level or an attack's on-damage rows (10300222 with 10300227); a row whose buff or
    // effect level differs, or whose played row has no owner flag, is left as it is.
    private static SkillEffectMetadata NotBuffRow(int id, int notBuffId, short level = 1, bool owner = false, bool caster = false) {
        var test = new PlayedClientActionAdditionals.BuffTest(0, 0, false, notBuffId, owner);
        return PlayedClientActionAdditionals.ToEffect(new PlayedClientActionAdditionals.Use(
            EffectId: id, Level: level, Target: 2, Owner: 1, OnDamage: true, OverlapCount: 1, LinkSkillId: 0, AllowOnBattleMount: false,
            Probability: 1, CasterTest: caster ? test : null, TargetTest: caster ? null : test));
    }

    private static PlayedClientActionAdditionals.Use NotBuffUse(int id, int notBuffId, bool owner, short level = 1, bool caster = false) {
        var test = new PlayedClientActionAdditionals.BuffTest(0, 0, false, notBuffId, owner);
        return new PlayedClientActionAdditionals.Use(EffectId: id, Level: level, Target: 2, Owner: 1, OnDamage: true, OverlapCount: 1,
            LinkSkillId: 0, AllowOnBattleMount: false, Probability: 1, CasterTest: caster ? test : null, TargetTest: caster ? null : test);
    }

    [Test]
    public void MarksThePacksHasNotBuffOwner() {
        SkillMetadataAttack attack = Attack("p0") with {
            SkillsOnDamage = [NotBuffRow(940, 950), NotBuffRow(941, 951), NotBuffRow(942, 952), NotBuffRow(943, 953, level: 2), NotBuffRow(944, 954, caster: true)],
        };
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Skills = [NotBuffRow(945, 955)],
            Motions = [new SkillMetadataMotion(null!, [attack], new Dictionary<string, byte>())],
        };
        var uses = new PlayedClientActionAdditionals.LevelUses(
            [NotBuffUse(945, 955, owner: true)],
            new Dictionary<(int, int), PlayedClientActionAdditionals.AttackUses> {
                [(0, 0)] = new("p0", [
                    NotBuffUse(940, 950, owner: true), NotBuffUse(941, 959, owner: true), NotBuffUse(942, 952, owner: false),
                    NotBuffUse(943, 953, owner: true), NotBuffUse(944, 954, owner: true, caster: true),
                ]),
            });
        SkillMetadataLevel marked = PlayedClientActionAdditionals.MarkNotBuffOwned(level, uses, out int count);
        SkillEffectMetadata[] rows = marked.Motions[0].Attacks[0].SkillsOnDamage;
        Assert.Multiple(() => {
            Assert.That(count, Is.EqualTo(3));
            Assert.That(rows.Select(row => row.Condition!.Condition.Target?.HasNotBuffOwned), Is.EqualTo(new bool?[] { true, false, false, false, null }));
            Assert.That(rows[4].Condition!.Condition.Caster!.HasNotBuffOwned, Is.True, "a caster test the same way");
            Assert.That(marked.Skills[0].Condition!.Condition.Target!.HasNotBuffOwned, Is.True, "the level's own rows");
            Assert.That(level.Motions[0].Attacks[0].SkillsOnDamage[0].Condition!.Condition.Target!.HasNotBuffOwned, Is.False, "the input is not changed");
        });
        Assert.That(PlayedClientActionAdditionals.MarkNotBuffOwned(level, new PlayedClientActionAdditionals.LevelUses([], new Dictionary<(int, int), PlayedClientActionAdditionals.AttackUses>()), out int none),
            Is.SameAs(level));
        Assert.That(none, Is.Zero);
    }

    // Row by row: of two rows of the same effect, level and buff, only the one whose own use has the owner flag is
    // marked, and a use of one attack never marks the same effect's row on another attack, nor an on-damage use a
    // row run on every hit.
    [Test]
    public void MarksTheHasNotBuffOwnerPerRow() {
        SkillMetadataAttack first = Attack("p0") with {
            Skills = [NotBuffRow(946, 956)],
            SkillsOnDamage = [NotBuffRow(946, 956), NotBuffRow(946, 956)],
        };
        SkillMetadataAttack second = Attack("p1") with { SkillsOnDamage = [NotBuffRow(946, 956)] };
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Skills = [],
            Motions = [new SkillMetadataMotion(null!, [first, second], new Dictionary<string, byte>())],
        };
        var uses = new PlayedClientActionAdditionals.LevelUses([],
            new Dictionary<(int, int), PlayedClientActionAdditionals.AttackUses> {
                [(0, 0)] = new("p0", [NotBuffUse(946, 956, owner: false), NotBuffUse(946, 956, owner: true)]),
                [(0, 1)] = new("p1", [NotBuffUse(946, 956, owner: false)]),
            });
        SkillMetadataLevel marked = PlayedClientActionAdditionals.MarkNotBuffOwned(level, uses, out int count);
        Assert.Multiple(() => {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(marked.Motions[0].Attacks[0].SkillsOnDamage.Select(row => row.Condition!.Condition.Target!.HasNotBuffOwned),
                Is.EqualTo(new[] { false, true }));
            Assert.That(marked.Motions[0].Attacks[0].Skills[0].Condition!.Condition.Target!.HasNotBuffOwned, Is.False);
            Assert.That(marked.Motions[0].Attacks[1].SkillsOnDamage[0].Condition!.Condition.Target!.HasNotBuffOwned, Is.False);
        });
    }

    // TiXmlElement::QueryBoolAttribute: a prefix of "true", "yes" or "1" in any case is set; "false", "no", "0" and
    // anything else (a 2, an empty value) is not.
    [Test]
    public void FlagsAreTheClientsBools() {
        string[] set = ["1", "true", "TRUE", "Yes", "10", "truely"];
        string[] unset = ["0", "2", "false", "no", "", " 1", "on"];
        Assert.Multiple(() => {
            foreach (string text in set) {
                Assert.That(PlayedClientActionAdditionals.QueryBool(text), Is.True, text);
            }
            foreach (string text in unset) {
                Assert.That(PlayedClientActionAdditionals.QueryBool(text), Is.False, text);
            }
        });
    }
}
