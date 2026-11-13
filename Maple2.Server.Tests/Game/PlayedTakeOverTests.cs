using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.AdditionalEffect;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game;

// A level both data sets have takes the played client's row over the pack's, part by part
// (PlayedAdditionalRows.TakeOver): the client loads only its <additional> rows. The KR rows of the Sticky Monster
// chain, which starts the charge 10600280 of Focused Fire 10600181: the pack's 10600282 lasts 100 ms and cancels the
// mark 10600281, which casts 10600282 every 10 s; the client's 10600282 lasts 10 s and cancels 10600280, and its
// mark casts nothing.
public class PlayedTakeOverTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private const string Played = """
        <ms2>
          <additional id="10600281" mainType="2" isDebuff="1" skillGroupType="1">
            <level level="1" feature="JobChange_02">
              <Basic durationTick="10000" intervalTick="10000" dotCondition="1" casterIndividualEffect="1" clearEffectFromPVPZone="1" />
              <Motion abnormalImmuneBreak="5" />
            </level>
          </additional>
          <additional id="10600282" mainType="2" skillGroupType="1">
            <level level="1" feature="JobChange_02">
              <Basic durationTick="10000" intervalTick="10000" dotCondition="1" casterIndividualEffect="1" clearEffectFromPVPZone="1" />
              <CancelEffect cancelCheckSameCaster="1" cancelEffectCodes="10600280" />
            </level>
          </additional>
          <additional id="61100120">
            <level level="1">
              <Basic durationTick="1000" />
              <Additional level="1" skillID="61100121" skillTarget="1" ownerHpLess="0.3" />
            </level>
          </additional>
        </ms2>
        """;

    // The pack's rows (additionaleffect/<id>.xml); 61100120 made up, with a condition skill the client's row widens.
    private static readonly Dictionary<int, string> Pack = new() {
        [10600281] = """
            <level><BasicProperty level="1" buffType="2" buffSubType="4" durationTick="10000" intervalTick="10000" dotCondition="1" casterIndividualEffect="1" clearEffectFromPVPZone="1" />
              <MotionProperty abnormalImmuneBreak="5" />
              <conditionSkill level="1" skillID="10600282" skillTarget="1" skillOwner="3" activeByIntervalTick="1" />
              <splashSkill level="1" skillID="10600290" splash="1" />
            </level>
            """,
        [10600282] = """
            <level><BasicProperty level="1" buffType="2" buffSubType="4" durationTick="100" intervalTick="100" casterIndividualEffect="1" clearEffectFromPVPZone="1" />
              <CancelEffectProperty cancelCheckSameCaster="1" cancelEffectCodes="10600281" />
            </level>
            """,
        [61100120] = """
            <level><BasicProperty level="1" durationTick="1000" />
              <conditionSkill level="1" skillID="61100122" skillTarget="1" />
            </level>
            """,
    };

    private static (AdditionalEffectMetadata Pack, AdditionalEffectMetadata Mapped, List<string> Unread) Levels(int id) {
        var document = new XmlDocument();
        document.LoadXml(Played);
        Dictionary<int, PlayedClientAdditionalEffects.Entry> played = PlayedClientAdditionalEffects.Read([document], _ => true);
        var mapper = (AdditionalEffectMapper) RuntimeHelpers.GetUninitializedObject(typeof(AdditionalEffectMapper));
        typeof(AdditionalEffectMapper).GetField("played", Any)!.SetValue(mapper, played);
        var rates = (CooldownGroupRates) RuntimeHelpers.GetUninitializedObject(typeof(CooldownGroupRates));
        typeof(CooldownGroupRates).GetField("rates", Any)!.SetValue(rates, System.Activator.CreateInstance(typeof(CooldownGroupRates).GetField("rates", Any)!.FieldType));
        typeof(AdditionalEffectMapper).GetField("groupRates", Any)!.SetValue(mapper, rates);
        MethodInfo build = typeof(AdditionalEffectMapper).GetMethod("Build", Any)!;
        MethodInfo complete = typeof(AdditionalEffectMapper).GetMethod("Complete", Any, [typeof(XmlElement)])!;

        var row = new XmlDocument();
        row.LoadXml(Pack[id]);
        var packData = (AdditionalEffectData) complete.Invoke(null, [row.DocumentElement!])!;
        var pack = (AdditionalEffectMetadata) build.Invoke(mapper, [id, packData, 0, null])!;
        var unread = new List<string>();
        AdditionalEffectData playedData = AdditionalEffectMapper.PlayedOnly(played[id], 1, unread);
        var mapped = (AdditionalEffectMetadata) build.Invoke(mapper, [id, playedData, 0, pack.Status])!;
        return (pack, mapped, unread);
    }

    [Test]
    public void TheChargeCasterBuffLastsTenSecondsAndCancelsTheCharge() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread) = Levels(10600282);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, taken);
        Assert.Multiple(() => {
            Assert.That(pack.Property.DurationTick, Is.EqualTo(100));
            Assert.That(taken, Is.EquivalentTo(new[] { "Basic", "CancelEffect" }));
            Assert.That(metadata.Property.DurationTick, Is.EqualTo(10000));
            Assert.That(metadata.Property.IntervalTick, Is.EqualTo(10000));
            Assert.That(metadata.Property.DotCondition, Is.EqualTo(Maple2.Model.Enum.BuffDotCondition.OnInterval));
            Assert.That(metadata.Update.Cancel!.Ids, Is.EqualTo(new[] { 10600280 }));
            Assert.That(metadata.Update.Cancel.CheckSameCaster, Is.True);
        });
    }

    // The mark's condition skill goes, its splash skill (the pack's) stays.
    [Test]
    public void TheMarkCastsNothingEveryTenSeconds() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread) = Levels(10600281);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, taken);
        Assert.Multiple(() => {
            Assert.That(pack.TickSkills.Single().Skills.Single().Id, Is.EqualTo(10600282));
            Assert.That(taken, Is.EqualTo(new[] { "Additional" }));
            Assert.That(metadata.TickSkills, Is.Empty);
            Assert.That(metadata.Skills.Single().Splash, Is.Not.Null);
            Assert.That(metadata.Skills.Single().Skills.Single().Id, Is.EqualTo(10600290));
            Assert.That(metadata.Property, Is.EqualTo(pack.Property));
        });
    }

    // An attribute with no pack name on a part keeps the pack's part: the mapped condition skill would fire wider.
    [Test]
    public void KeepsAPartWithAnAttributeItDoesNotRead() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread) = Levels(61100120);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, taken);
        Assert.Multiple(() => {
            Assert.That(unread, Is.EqualTo(new[] { "Additional.ownerHpLess" }));
            Assert.That(taken, Is.Empty);
            Assert.That(metadata, Is.SameAs(pack));
        });
    }

    // A level whose parts are all the pack's is the pack's metadata itself.
    [Test]
    public void KeepsAnEqualLevelAsItIs() {
        (AdditionalEffectMetadata pack, _, _) = Levels(10600282);
        var taken = new List<string>();
        Assert.That(PlayedAdditionalRows.TakeOver(pack, pack, [], taken), Is.SameAs(pack));
        Assert.That(taken, Is.Empty);
    }
}
