using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Utils;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Ingest;

// The skills a player's client can cast (PlayerSkillSources) and the cast handler's refusal of the rest. The ids are
// KR ones of each grant path; the elements are written the data's way (job.xml, skillset.xml, constants.xml, a
// skill's combo, change skill, jump and default skill).
public class PlayerSkillSourcesTests {
    private static XmlDocument Document(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document;
    }

    private static HashSet<int> CastableOf(out IReadOnlyDictionary<int, int> mainTypes) {
        XmlDocument job = Document("""<ms2><job code="90"><skills><skill main="10900001" uiPosition="2, 2" maxLevel="10" quickSlotPriority="4" /><skill main="10100061" sub="10100062" uiPosition="2, 0" quickSlotPriority="4" /></skills></job></ms2>""");
        XmlDocument skillSet = Document("""<ms2><skillSet id="99910001" skillIDs="99910002, 99910001" skillLevels="1, 1" /></ms2>""");
        XmlDocument constants = Document("""<ms2><v key="HoldAttackSkillID" value="10700252" feature="HoldAttack" /><v key="SummonPetSkillID" value="82100001" /><v key="returnHomeSkill" value="100000000" /><v key="MesoMarketProductUnit2" value="30000000" /></ms2>""");
        XmlElement combo = Document("""<skill id="1"><basic><kinds jump="11" /><stateAttr useDefaultSkillID="0" /></basic><level value="1"><combo outputSkill="100001" /><changeSkill changeSkillID="10600071, 10600072" /></level></skill>""").DocumentElement!;

        // Main types: 1 (the normal attack) needs no learning, 0 (none in the played client) and 2 do.
        mainTypes = new Dictionary<int, int> {
            [1] = 1, [11] = 2, [100001] = 2, [10600071] = 2, [10600072] = 2, [10900001] = 2, [10100062] = 2, [99910002] = 2,
            [90000036] = 2, [80000001] = 2, [10700252] = 2, [82100001] = 2, [100000000] = 2, [30000000] = 2, [50000001] = 2, [10300001] = 0,
        };
        var edges = new Dictionary<int, HashSet<int>> { [1] = PlayerSkillSources.Edges(combo).ToHashSet() };
        IEnumerable<int> granted = PlayerSkillSources.TreeSkills(job)
            .Concat(PlayerSkillSources.SkillSetSkills(skillSet))
            .Concat(PlayerSkillSources.ConstantSkills(constants))
            .Concat([90000036, 80000001]); // item skills: an item's skill and an object weapon's
        return PlayerSkillSources.Castable(mainTypes, edges, granted);
    }

    [Test]
    public void EveryGrantPathStaysCastable() {
        HashSet<int> castable = CastableOf(out _);
        // No learning: the normal attack 1. Its combo output 100001, jump 11 and change skills 10600071 and 10600072.
        Assert.That(castable, Does.Contain(1));
        Assert.That(castable, Does.Contain(100001));
        Assert.That(castable, Does.Contain(11));
        Assert.That(castable, Does.Contain(10600071));
        Assert.That(castable, Does.Contain(10600072));
        // A job tree's main skill and sub skill.
        Assert.That(castable, Does.Contain(10900001));
        Assert.That(castable, Does.Contain(10100062));
        // A skill set's skills (ride 99910001, a transformation effect's set the same way).
        Assert.That(castable, Does.Contain(99910001));
        Assert.That(castable, Does.Contain(99910002));
        // Item skills.
        Assert.That(castable, Does.Contain(90000036));
        Assert.That(castable, Does.Contain(80000001));
        // The skill constants: hold attack, pet summon, return home.
        Assert.That(castable, Does.Contain(10700252));
        Assert.That(castable, Does.Contain(82100001));
        Assert.That(castable, Does.Contain(100000000));
    }

    [Test]
    public void BossSkillIsNotCastable() {
        HashSet<int> castable = CastableOf(out _);
        // 50000001, an NPC skill only another skill's condition names; a constant that is not a skill constant; a
        // skill the played client does not have.
        Assert.That(castable, Does.Not.Contain(50000001));
        Assert.That(castable, Does.Not.Contain(30000000));
        Assert.That(castable, Does.Not.Contain(10300001));
    }

    [Test]
    public void CastOfBossSkillIsRefused() {
        // 50000001 is in no job tree. The tree check alone (the old code) lets it through; the source check refuses it.
        Assert.That(SkillCastRules.TreeRefuses(false, null, 1), Is.False);
        Assert.That(SkillCastRules.SourceRefuses(false, true), Is.True);
        // A skill some client path gives (item skill 90000036) and a tree skill are never refused by it.
        Assert.That(SkillCastRules.SourceRefuses(false, false), Is.False);
        Assert.That(SkillCastRules.SourceRefuses(true, true), Is.False);
    }
}
