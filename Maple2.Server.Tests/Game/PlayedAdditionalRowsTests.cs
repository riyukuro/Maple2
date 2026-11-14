using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game;

// The played client's <additional> level rows written as the data pack's <level> rows (PlayedAdditionalRows.ToPack),
// on rows of the KR data both sets have.
public class PlayedAdditionalRowsTests {
    private static (XmlElement Row, List<string> Unread) Pack(string additional) {
        var document = new XmlDocument();
        document.LoadXml(additional);
        XmlElement effect = document.DocumentElement!;
        var unread = new List<string>();
        XmlElement row = PlayedAdditionalRows.ToPack(effect, (XmlElement) effect.SelectSingleNode("level")!, 1, unread);
        return (row, unread);
    }

    // isDebuff and mainType to buffType and buffSubType; no mainType is no buffSubType, as the pack's rows have it.
    [Test]
    public void WritesTheBasicPropertyOfTheEffect() {
        XmlElement debuff = Pack("""<additional id="10300271" isDebuff="1" mainType="2"><level level="1"><Basic durationTick="20000" /></level></additional>""").Row;
        XmlElement none = Pack("""<additional id="10100041"><level level="1"><Basic durationTick="20000" /></level></additional>""").Row;
        Assert.Multiple(() => {
            Assert.That(debuff["BasicProperty"]!.GetAttribute("buffType"), Is.EqualTo("2"));
            Assert.That(debuff["BasicProperty"]!.GetAttribute("buffSubType"), Is.EqualTo("4"));
            Assert.That(debuff["BasicProperty"]!.GetAttribute("durationTick"), Is.EqualTo("20000"));
            Assert.That(debuff["BasicProperty"]!.GetAttribute("level"), Is.EqualTo("1"));
            Assert.That(none["BasicProperty"]!.HasAttribute("buffSubType"), Is.False);
            Assert.That(none["BasicProperty"]!.HasAttribute("buffType"), Is.False);
        });
    }

    // <Condition>: the owner's buff test on <owner>, an id list as child rows, the hand item types as one <weapon>.
    [Test]
    public void WritesTheBeginCondition() {
        (XmlElement row, List<string> unread) = Pack("""
            <additional id="1"><level level="1">
              <Condition hasBuffID="40361101" hasBuffCount="1" usedSkillIDs="10700281,10700282" equipHandR="34" />
            </level></additional>
            """);
        XmlElement begin = row["beginCondition"]!;
        Assert.Multiple(() => {
            Assert.That(unread, Is.Empty);
            Assert.That(begin["owner"]!.GetAttribute("hasBuffID"), Is.EqualTo("40361101"));
            Assert.That(begin["owner"]!.GetAttribute("hasBuffCount"), Is.EqualTo("1"));
            Assert.That(begin.SelectNodes("requireSkillCodes")!.Cast<XmlElement>().Select(code => code.GetAttribute("code")), Is.EqualTo(new[] { "10700281", "10700282" }));
            Assert.That(begin.SelectNodes("weapon")!.Count, Is.EqualTo(1));
            Assert.That(begin["weapon"]!.GetAttribute("lh"), Is.EqualTo("0"));
            Assert.That(begin["weapon"]!.GetAttribute("rh"), Is.EqualTo("34"));
        });
    }

    // <Additional>: a <conditionSkill> with its owner tests in a <beginCondition>; the client's GreaterEqual is the
    // pack's GreaterEquals.
    [Test]
    public void WritesTheConditionSkill() {
        (XmlElement row, List<string> unread) = Pack("""
            <additional id="10800221"><level level="1">
              <Additional level="1" skillID="10800222" skillTarget="1" skillOwner="3" ownerEvent="10" ownerAdditionalID="10800221"
                          ownerAdditionalLevel="1" ownerAdditionalCount="9" ownerAdditionalMine="1" />
              <Condition casterHpRate="0.5" casterHpOp="GreaterEqual" />
            </level></additional>
            """);
        XmlElement skill = row["conditionSkill"]!;
        XmlElement owner = skill["beginCondition"]!["owner"]!;
        XmlElement stat = row["beginCondition"]!["caster"]!["compareStat"]!;
        Assert.Multiple(() => {
            Assert.That(unread, Is.Empty);
            Assert.That(skill.GetAttribute("skillID"), Is.EqualTo("10800222"));
            Assert.That(skill.GetAttribute("skillOwner"), Is.EqualTo("3"));
            Assert.That(owner.GetAttribute("eventCondition"), Is.EqualTo("10"));
            Assert.That(owner.GetAttribute("hasBuffID"), Is.EqualTo("10800221"));
            Assert.That(owner.GetAttribute("hasBuffLevel"), Is.EqualTo("1"));
            Assert.That(owner.GetAttribute("hasBuffCount"), Is.EqualTo("9"));
            Assert.That(owner.GetAttribute("hasBuffOwner"), Is.EqualTo("1"));
            Assert.That(stat.GetAttribute("hp"), Is.EqualTo("0.5"));
            Assert.That(stat.GetAttribute("func"), Is.EqualTo("GreaterEquals"));
        });
    }

    // <Skill> to a splash skill; a chainDistance is the pack's chain="1" with it.
    [Test]
    public void WritesTheSplashSkill() {
        XmlElement splash = Pack("""<additional id="53200000"><level level="1"><Skill level="1" skillID="50101700" chainDistance="15000" /></level></additional>""").Row["splashSkill"]!;
        Assert.Multiple(() => {
            Assert.That(splash.GetAttribute("splash"), Is.EqualTo("1"));
            Assert.That(splash.GetAttribute("chain"), Is.EqualTo("1"));
            Assert.That(splash.GetAttribute("chainDistance"), Is.EqualTo("15000"));
        });
    }

    // What has no pack name is listed: an attribute, a child. <Skill> is mapped but not taken over (the pack's
    // nonTargetActive is not in the client's row).
    [Test]
    public void ListsWhatItDoesNotRead() {
        List<string> unread = Pack("""
            <additional id="61100120"><level level="1">
              <Additional level="1" skillID="61100121" skillTarget="1" ownerHpLess="0.3" />
              <Spread type="1" />
            </level></additional>
            """).Unread;
        Assert.Multiple(() => {
            Assert.That(unread, Is.EquivalentTo(new[] { "Additional.ownerHpLess", "Spread" }));
            Assert.That(PlayedAdditionalRows.Gated, Does.Not.Contain("Skill"));
            Assert.That(PlayedAdditionalRows.Gated, Does.Not.Contain("Status"));
            Assert.That(PlayedAdditionalRows.Gated, Does.Contain("Shield"));
        });
    }

    // <additional>: skillGroupType goes to <BasicProperty> by its name; an attribute with no pack name is listed.
    [Test]
    public void ListsAnEffectAttributeItDoesNotRead() {
        (XmlElement row, List<string> unread) = Pack("""<additional id="10600280" mainType="2" skillGroupType="1" other="1"><level level="1"><Basic durationTick="10000" /></level></additional>""");
        Assert.Multiple(() => {
            Assert.That(row["BasicProperty"]!.GetAttribute("skillGroupType"), Is.EqualTo("1"));
            Assert.That(unread, Is.EquivalentTo(new[] { "additional.other" }));
        });
    }

    // KR 10800059, an effect only the client has (Assassin skill 10800051 at every level): 1 s of evasion compulsion
    // 2 at rate 1. <Status> goes to <StatusProperty> under the client's names, so the effect is not left out (before:
    // "Status" unread, the effect and the skill's fifth effect dropped).
    [Test]
    public void WritesTheStatusOfAnEffectOnlyTheClientHas() {
        const string played = """<ms2><additional id="10800059" mainType="1" skillGroupType="1"><level level="1"><Basic durationTick="1000" /><Status compulsionEventTypes="2" compulsionEventRate="1" /></level></additional></ms2>""";
        var document = new XmlDocument();
        document.LoadXml(played);
        Dictionary<int, PlayedClientAdditionalEffects.Entry> entries = PlayedClientAdditionalEffects.Read([document], _ => true);
        (XmlElement row, List<string> unread) = Pack("""<additional id="10800059" mainType="1" skillGroupType="1"><level level="1"><Basic durationTick="1000" /><Status compulsionEventTypes="2" compulsionEventRate="1" /></level></additional>""");
        Assert.Multiple(() => {
            Assert.That(PlayedAdditionalRows.Unread(entries[10800059]), Is.Empty);
            Assert.That(unread, Is.Empty);
            Assert.That(row["StatusProperty"]!.GetAttribute("compulsionEventTypes"), Is.EqualTo("2"));
            Assert.That(row["StatusProperty"]!.GetAttribute("compulsionEventRate"), Is.EqualTo("1"));
        });
    }

    // The client reads only attributes of <Status> (additionalactionadditional.cpp:1211-1225): a child element of it is
    // listed, so the effect is not taken over without it (before: dropped with nothing unread).
    [Test]
    public void ListsAChildOfTheStatus() {
        const string played = """<ms2><additional id="10800059" mainType="1" skillGroupType="1"><level level="1"><Basic durationTick="1000" /><Status compulsionEventTypes="2" compulsionEventRate="1"><stat str="10" /></Status></level></additional></ms2>""";
        var document = new XmlDocument();
        document.LoadXml(played);
        Dictionary<int, PlayedClientAdditionalEffects.Entry> entries = PlayedClientAdditionalEffects.Read([document], _ => true);
        Assert.That(PlayedAdditionalRows.Unread(entries[10800059]), Is.EquivalentTo(new[] { "Status/stat" }));
    }
}
