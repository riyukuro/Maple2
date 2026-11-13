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
          <additional id="10800211" mainType="1" skillGroupType="1">
            <level level="1" feature="JobChange_01" stringParam="3">
              <Basic durationTick="60000" clearEffectFromPVPZone="1" />
              <SpecialAbility>
                <v type="offensive_physicaldamage" value="30" />
              </SpecialAbility>
              <UI icon="10800211_buff.png" />
            </level>
          </additional>
          <additional id="10600212" mainType="2" skillGroupType="1">
            <level level="1" feature="JobChange_01" stringParam="30">
              <Basic durationTick="5500" invokeEvent="1" clearEffectFromPVPZone="1" />
              <Motion abnormalImmuneBreak="5" />
              <UI icon="10600211_buff.png" isHideCooltime="1" />
            </level>
          </additional>
          <additional id="10600222" mainType="2" skillGroupType="1">
            <level level="1" feature="JobChange_01" stringParam="30">
              <Basic durationTick="10000" invokeEvent="1" clearEffectFromPVPZone="1" />
              <Shield hpByTargetMaxHP="0.3" />
            </level>
          </additional>
          <additional id="70000154" isDebuff="1">
            <level level="1">
              <Basic durationTick="600" delayTick="1899" resetCondition="1" logoutClearEffect="1" leaveFieldClearEffect="1" />
              <Motion abnormalImmuneBreak="5" />
              <ImmuneEffect immuneEffectCodes="70000151" />
            </level>
          </additional>
          <additional id="70000999" isDebuff="1">
            <level level="1">
              <Basic durationTick="1200" />
              <Condition applyPvPZoneType="1" />
            </level>
          </additional>
          <additional id="10700193" skillGroupType="1">
            <level level="1" feature="JobChange_01">
              <Basic durationTick="800" />
              <Motion abnormalImmuneBreak="5" />
              <Status compulsionEventTypes="2" compulsionEventRate="1" />
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

    // Ion Cannon's shield, which the client's rows moved from 10600212 to 10600222, is here for the names the
    // played rows write (PlayedAdditionalRows.Written): a pack's name no played row writes is kept.
    // The pack's rows (additionaleffect/<id>.xml); 61100120 made up, with a condition skill the client's row widens;
    // 70000999 made up, an item buff whose client row lasts longer; 10700193's duration made up.
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
        [10800211] = """
            <level><BasicProperty level="1" buffSubType="2" durationTick="12000" clearEffectFromPVPZone="1" skillGroupType="1" />
              <StatusProperty><SpecialAbility darkdamagerate="0.1" /></StatusProperty>
              <ShieldProperty hpByTargetMaxHP="0.06" />
            </level>
            """,
        [10600212] = """
            <level><BasicProperty level="1" buffSubType="4" durationTick="10000" invokeEvent="1" clearEffectFromPVPZone="1" skillGroupType="1" />
              <MotionProperty abnormalImmuneBreak="5" />
              <ShieldProperty hpByTargetMaxHP="0.3" />
            </level>
            """,
        [70000154] = """
            <level><BasicProperty level="1" buffType="2" durationTick="600" delayTick="1899" resetCondition="1" logoutClearEffect="1" leaveFieldClearEffect="1" />
              <MotionProperty abnormalImmuneBreak="5" />
              <ImmuneEffectProperty immuneEffectCodes="70000151" />
              <beginCondition allowMapleSurvival="1" />
            </level>
            """,
        [70000999] = """
            <level><BasicProperty level="1" buffType="2" durationTick="600" />
              <beginCondition allowMapleSurvival="1" applyPvPZoneType="1" />
            </level>
            """,
        [10700193] = """
            <level><BasicProperty level="1" durationTick="1000" skillGroupType="1" />
              <MotionProperty abnormalImmuneBreak="5" />
            </level>
            """,
        [61100120] = """
            <level><BasicProperty level="1" durationTick="1000" />
              <conditionSkill level="1" skillID="61100122" skillTarget="1" />
            </level>
            """,
    };

    private static (AdditionalEffectMetadata Pack, AdditionalEffectMetadata Mapped, List<string> Unread) Levels(int id) {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, _, _) = LevelsWithRow(id);
        return (pack, mapped, unread);
    }

    private static (AdditionalEffectMetadata Pack, AdditionalEffectMetadata Mapped, List<string> Unread, XmlElement Row, XmlElement PackRow) LevelsWithRow(int id) {
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
        XmlElement packRow = AdditionalEffectMapper.Serialized(packData);
        AdditionalEffectData playedData = AdditionalEffectMapper.PlayedOnly(played[id], 1, unread, packRow, PlayedAdditionalRows.Written(played.Values));
        var mapped = (AdditionalEffectMetadata) build.Invoke(mapper, [id, playedData, 0, pack.Status])!;
        return (pack, mapped, unread, played[id].Rows[1], packRow);
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

    // Shadow Stance: the client's row trades the pack's 6% shield and 12 s for 60 s and +30 physical damage, a
    // SpecialAbility the ingest does not read yet, so the level stays the pack's whole.
    [Test]
    public void KeepsALevelWholeWhileAPlayedChildIsNotRead() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10800211);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding);
        Assert.Multiple(() => {
            Assert.That(holding, Is.EqualTo(new[] { "SpecialAbility" }));
            Assert.That(taken, Is.EquivalentTo(new[] { "Basic", "Shield" }));
            Assert.That(metadata, Is.SameAs(pack));
            Assert.That(metadata.Property.DurationTick, Is.EqualTo(12000));
            Assert.That(metadata.Shield!.HpByTargetMaxHp, Is.EqualTo(0.06f));
        });
    }

    // Ion Cannon: the client's row has only children the ingest reads and no shield, so the shield goes.
    [Test]
    public void TakesALevelWithOnlyReadChildren() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10600212);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding);
        Assert.Multiple(() => {
            Assert.That(holding, Is.Empty);
            Assert.That(taken, Is.EquivalentTo(new[] { "Basic", "Shield" }));
            Assert.That(metadata.Property.DurationTick, Is.EqualTo(5500));
            Assert.That(metadata.Shield, Is.Null);
        });
    }

    // A potion's debuff: the pack's beginCondition allowMapleSurvival has no attribute in the client's rows, so the
    // played level keeps it, and the level, otherwise the same, stays the pack's.
    [Test]
    public void KeepsWhatNoPlayedRowWrites() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread) = Levels(70000154);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, taken);
        Assert.Multiple(() => {
            Assert.That(pack.Condition!.AllowOnSurvival, Is.True);
            Assert.That(mapped.Condition!.AllowOnSurvival, Is.True);
            Assert.That(taken, Is.Empty);
            Assert.That(metadata, Is.SameAs(pack));
        });
    }

    // A level that takes the client's duration still starts on a Maple Survival map.
    [Test]
    public void ATakenLevelKeepsWhatNoPlayedRowWrites() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread) = Levels(70000999);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, taken);
        Assert.Multiple(() => {
            Assert.That(taken, Is.EqualTo(new[] { "Basic" }));
            Assert.That(metadata.Property.DurationTick, Is.EqualTo(1200));
            Assert.That(metadata.Condition!.AllowOnSurvival, Is.True);
        });
    }

    // The client's <Status> has a compulsion event the pack's StatusProperty lacks: the level stays the pack's whole.
    [Test]
    public void KeepsALevelWholeWhileThePlayedStatusDiffers() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10700193);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding);
        Assert.Multiple(() => {
            Assert.That(holding, Is.EqualTo(new[] { "Status" }));
            Assert.That(taken, Is.EqualTo(new[] { "Basic" }));
            Assert.That(metadata, Is.SameAs(pack));
            Assert.That(pack.Status.Compulsion, Is.Null);
        });
    }
}
