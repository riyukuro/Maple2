using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.AdditionalEffect;
using Maple2.Model.Enum;
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
          <additional id="10800041" skillGroupType="1">
            <level level="1">
              <Basic durationTick="12000" />
              <SpecialAbility>
                <v type="cap_ignore_limit_extra" value="280" />
              </SpecialAbility>
            </level>
          </additional>
          <additional id="10800212" mainType="1">
            <level level="1">
              <Basic durationTick="60000" />
              <SpecialAbility>
                <v type="offensive_physicaldamage" value="30" />
                <v type="aevp" value="100" />
                <v type="notInTheTable" value="5" />
              </SpecialAbility>
            </level>
          </additional>
          <additional id="10800213" mainType="1">
            <level level="1">
              <Basic durationTick="60000" />
            </level>
          </additional>
          <additional id="10800214" mainType="1">
            <level level="1">
              <Basic durationTick="60000" />
              <SpecialAbility>
                <v type="fireDamage" value="10" />
              </SpecialAbility>
            </level>
          </additional>
          <additional id="10800215" mainType="1">
            <level level="1">
              <Basic durationTick="60000" />
              <SpecialAbility>
                <v type="improve_stat_wap_u" value="80" />
                <v type="FireDamage" value="10" />
                <v type="firedamage" value="20" />
              </SpecialAbility>
            </level>
          </additional>
          <additional id="10800216" mainType="1">
            <level level="1">
              <Basic durationTick="60000" />
              <SpecialAbility>
                <v type="aevp" value="100" />
              </SpecialAbility>
            </level>
          </additional>
          <additional id="10300201">
            <level level="1">
              <Basic durationTick="2000" />
              <Status deathResistanceHP="1" healRecoveryHPRate="0.5" />
            </level>
          </additional>
          <additional id="10300202">
            <level level="1">
              <Basic durationTick="2000" />
            </level>
          </additional>
          <additional id="10300203">
            <level level="1">
              <Basic durationTick="2000" />
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
        [10800041] = """
            <level><BasicProperty level="1" buffSubType="2" durationTick="8000" skillGroupType="1" />
              <StatusProperty compulsionEventTypes="1" compulsionEventRate="1.000000" />
            </level>
            """,
        [10800212] = """
            <level><BasicProperty level="1" buffSubType="2" durationTick="12000" />
              <StatusProperty><SpecialAbility darkdamagerate="0.1" /></StatusProperty>
            </level>
            """,
        [10800213] = """
            <level><BasicProperty level="1" buffSubType="2" durationTick="12000" />
              <StatusProperty><SpecialAbility darkdamagerate="0.1" /></StatusProperty>
            </level>
            """,
        [10800214] = """
            <level><BasicProperty level="1" buffSubType="2" durationTick="12000" />
              <StatusProperty><SpecialAbility firedamagerate="0.01" /></StatusProperty>
            </level>
            """,
        [10300201] = """
            <level><BasicProperty level="1" durationTick="1000" />
              <StatusProperty deathResistanceHP="true" healRecoveryHPRate="0.5" />
            </level>
            """,
        [10300202] = """
            <level><BasicProperty level="1" durationTick="1000" />
              <StatusProperty resWapR="0.2" />
            </level>
            """,
        [10300203] = """
            <level><BasicProperty level="1" durationTick="1000" />
              <StatusProperty resAspR="0.2" />
            </level>
            """,
        [61100120] = """
            <level><BasicProperty level="1" durationTick="1000" />
              <conditionSkill level="1" skillID="61100122" skillTarget="1" />
            </level>
            """,
    };

    // The client's SpecialAbility table (table/specialabilitystringtable.xml), the rows these tests use.
    private static readonly ClientSpecialAbility Table = new(Xml("""
        <ms2>
          <key id="10" option="atpToBap" valueType="2" />
          <key id="19" option="fireDamage" valueType="2" />
          <key id="111" option="strToInt" valueType="2" />
          <key id="130" option="improve_stat_wap_u" valueType="2" />
          <key id="177" option="offensive_physicaldamage" valueType="2" />
          <key id="204" option="aevp" valueType="2" />
          <key id="205" option="cap_ignore_limit_extra" valueType="2" />
          <key id="999" option="noValueType" valueType="0" />
        </ms2>
        """));

    private static XmlDocument Xml(string text) {
        var document = new XmlDocument();
        document.LoadXml(text);
        return document;
    }

    private static Dictionary<string, int> Abilities(int id) {
        Dictionary<int, PlayedClientAdditionalEffects.Entry> played = PlayedClientAdditionalEffects.Read([Xml(Played)], _ => true);
        return played[id].SpecialAbilities.GetValueOrDefault((short) 1) ?? [];
    }

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
        AdditionalEffectData playedData = AdditionalEffectMapper.PlayedOnly(played[id], 1, unread, packRow);
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

    // Shadow Stance: the client's row trades the pack's 6% shield and 12 s for 60 s and +30 physical damage; without
    // the client's SpecialAbility table, its SpecialAbility is not read, so the level stays the pack's whole.
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

    // The client's <Status> has an evasion compulsion event the pack's StatusProperty lacks: the level takes it with
    // the client's timing (before: held whole on "Status", no compulsion).
    [Test]
    public void TakesThePlayedCompulsion() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10700193);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding);
        Assert.Multiple(() => {
            Assert.That(holding, Is.Empty);
            Assert.That(taken, Is.EquivalentTo(new[] { "Basic", "Compulsion" }));
            Assert.That(pack.Status.Compulsion, Is.Null);
            Assert.That(metadata.Status.Compulsion!.Type, Is.EqualTo(BuffCompulsionEventType.EvasionChanceOverride));
            Assert.That(metadata.Status.Compulsion.Rate, Is.EqualTo(1f));
            Assert.That(metadata.Status.Compulsion.SkillIds, Is.Empty);
            Assert.That(metadata.Property.DurationTick, Is.EqualTo(800));
        });
    }

    // The client's Assassin buff 10800041 has no compulsion and is cap_ignore_limit_extra 280 only; the pack's has a
    // critical compulsion 1 (every hit critical). The pack's compulsion does not survive the takeover: the level
    // gives +0.28 critical rate past the cap for the client's 12 s (before: held whole on "Status", always critical).
    [Test]
    public void ThePacksCompulsionDoesNotSurviveTheTakeover() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10800041);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding,
            Table, Abilities(10800041));
        Assert.Multiple(() => {
            Assert.That(holding, Is.Empty);
            Assert.That(taken, Is.EquivalentTo(new[] { "Basic", "SpecialAbility", "Compulsion" }));
            Assert.That(pack.Status.Compulsion!.Type, Is.EqualTo(BuffCompulsionEventType.CritChanceOverride));
            Assert.That(pack.Status.Compulsion.Rate, Is.EqualTo(1f));
            Assert.That(metadata.Status.Compulsion, Is.Null);
            Assert.That(metadata.Status.SpecialRates[SpecialAttribute.CriticalRateIgnoreLimitExtra], Is.EqualTo(0.28f).Within(1e-6f));
            Assert.That(metadata.Property.DurationTick, Is.EqualTo(12000));
        });
    }

    // With the table, Shadow Stance takes the client's +3% physical damage in place of the pack's +10% dark damage,
    // with its 60 s and no shield.
    [Test]
    public void TakesTheClientsSpecialAbility() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10800211);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding,
            Table, Abilities(10800211));
        Assert.Multiple(() => {
            Assert.That(holding, Is.Empty);
            Assert.That(taken, Is.EquivalentTo(new[] { "Basic", "Shield", "SpecialAbility" }));
            Assert.That(pack.Status.SpecialRates[Maple2.Model.Enum.SpecialAttribute.DarkDamage], Is.EqualTo(0.1f));
            Assert.That(metadata.Status.SpecialRates.Keys, Is.EqualTo(new[] { Maple2.Model.Enum.SpecialAttribute.OffensivePhysicalDamage }));
            Assert.That(metadata.Status.SpecialRates[Maple2.Model.Enum.SpecialAttribute.OffensivePhysicalDamage], Is.EqualTo(0.03f));
            Assert.That(metadata.Property.DurationTick, Is.EqualTo(60000));
            Assert.That(metadata.Shield, Is.Null);
        });
    }

    // A type the client keeps with no server stat (aevp) holds the level; one the table lacks the client drops.
    [Test]
    public void KeepsALevelWholeWhileASpecialAbilityTypeHasNoStat() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10800212);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding,
            Table, Abilities(10800212));
        var unmapped = new List<string>();
        PlayedAdditionalRows.SpecialAbilityStats(Table, Abilities(10800212), unmapped);
        Assert.Multiple(() => {
            Assert.That(unmapped, Is.EqualTo(new[] { "aevp" }));
            Assert.That(holding, Is.EqualTo(new[] { "SpecialAbility" }));
            Assert.That(metadata, Is.SameAs(pack));
        });
    }

    // The pack's level has stats and the client's none: the level stays the pack's whole.
    [Test]
    public void KeepsALevelWholeWhileOnlyThePackHasStats() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10800213);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding,
            Table, Abilities(10800213));
        Assert.Multiple(() => {
            Assert.That(holding, Is.EqualTo(new[] { "SpecialAbility.Pack" }));
            Assert.That(taken, Is.EquivalentTo(new[] { "Basic", "SpecialAbility" }));
            Assert.That(metadata, Is.SameAs(pack));
        });
    }

    // A level with final stats whose client stats differ stays the pack's whole.
    [Test]
    public void KeepsALevelWholeWhileItHasFinalStats() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10800211);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding,
            Table, Abilities(10800211), finalRows: true);
        Assert.Multiple(() => {
            Assert.That(holding, Is.EqualTo(new[] { "SpecialAbility.FinalStatus" }));
            Assert.That(metadata, Is.SameAs(pack));
        });
    }

    // The client's fireDamage 10 is the pack's firedamagerate 0.01: the stats stay the pack's, the duration is taken.
    [Test]
    public void KeepsTheStatsWhereTheyAreTheSame() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10800214);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding,
            Table, Abilities(10800214));
        Assert.Multiple(() => {
            Assert.That(holding, Is.Empty);
            Assert.That(taken, Is.EqualTo(new[] { "Basic" }));
            Assert.That(metadata.Status, Is.SameAs(pack.Status));
            Assert.That(metadata.Property.DurationTick, Is.EqualTo(60000));
        });
    }

    // A <Status> the same as the pack's (a flag written 1 by the client and true by the pack's serializer) does not hold.
    [Test]
    public void TakesALevelWhoseStatusIsThePacks() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10300201);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding);
        Assert.Multiple(() => {
            Assert.That(packRow["StatusProperty"]!.GetAttribute("deathResistanceHP"), Is.EqualTo("true"));
            Assert.That(holding, Is.Empty);
            Assert.That(taken, Is.EqualTo(new[] { "Basic" }));
            Assert.That(metadata.Property.DurationTick, Is.EqualTo(2000));
            Assert.That(metadata.Status.DeathResistanceHp, Is.True);
        });
    }

    // The pack's StatusProperty has a resistance the client's level, with no <Status>, lacks: the level holds; one the
    // client does not read (resAspR) does not hold it.
    [Test]
    public void KeepsALevelWholeWhileOnlyThePackHasAStatus() {
        (AdditionalEffectMetadata pack, AdditionalEffectMetadata mapped, List<string> unread, XmlElement row, XmlElement packRow) = LevelsWithRow(10300202);
        var taken = new List<string>();
        AdditionalEffectMetadata metadata = PlayedAdditionalRows.TakeOver(pack, mapped, unread, row, packRow, taken, out IReadOnlyList<string> holding);
        (AdditionalEffectMetadata aspPack, AdditionalEffectMetadata aspMapped, List<string> aspUnread, XmlElement aspRow, XmlElement aspPackRow) = LevelsWithRow(10300203);
        AdditionalEffectMetadata asp = PlayedAdditionalRows.TakeOver(aspPack, aspMapped, aspUnread, aspRow, aspPackRow, new List<string>(), out IReadOnlyList<string> aspHolding);
        Assert.Multiple(() => {
            Assert.That(holding, Is.EqualTo(new[] { "Status" }));
            Assert.That(metadata, Is.SameAs(pack));
            Assert.That(aspHolding, Is.Empty);
            Assert.That(asp.Property.DurationTick, Is.EqualTo(2000));
        });
    }

    // improve_stat_wap_u is the max weapon attack rate the server reads (the pack's Rates.MaxWeaponAtk), not the
    // MaxWeaponAttack special no server calc reads.
    [Test]
    public void TheMaxWeaponAttackTypeIsTheBasicRate() {
        ClientSpecialAbility.Stats stats = PlayedAdditionalRows.SpecialAbilityStats(Table, new Dictionary<string, int> { ["improve_stat_wap_u"] = 160 }, new List<string>());
        Assert.Multiple(() => {
            Assert.That(stats.Rates[Maple2.Model.Enum.BasicAttribute.MaxWeaponAtk], Is.EqualTo(0.16f));
            Assert.That(stats.Rates.ContainsKey(Maple2.Model.Enum.BasicAttribute.MinWeaponAtk), Is.False);
            Assert.That(stats.SpecialRates, Is.Empty);
        });
    }

    // The client matches type names case-insensitively: FireDamage and firedamage are one type, the later row kept.
    [Test]
    public void TypesDifferingInCaseAreOne() {
        Dictionary<string, int> rows = Abilities(10800215);
        Assert.Multiple(() => {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows["fireDamage"], Is.EqualTo(20));
        });
    }

    // A type whose table row has valueType 0 the client drops (SpecialAbilityInfo's m_nValueType): no stat, and it
    // holds no level.
    [Test]
    public void SkipsATypeWithValueTypeZero() {
        var unmapped = new List<string>();
        ClientSpecialAbility.Stats stats = PlayedAdditionalRows.SpecialAbilityStats(Table, new Dictionary<string, int> { ["noValueType"] = 50 }, unmapped);
        Assert.Multiple(() => {
            Assert.That(Table.Rate("noValueType"), Is.Null);
            Assert.That(unmapped, Is.Empty);
            Assert.That(stats.Rates, Is.Empty);
            Assert.That(stats.Values, Is.Empty);
            Assert.That(stats.SpecialRates, Is.Empty);
            Assert.That(stats.SpecialValues, Is.Empty);
        });
    }

    // atpToBap and strToInt are the stat conversion (Accuracy to BonusAtk, Strength to Intelligence at the rate);
    // a level holds one, a second type is unmapped.
    [Test]
    public void TheConversionTypesAreTheStatConversion() {
        var unmapped = new List<string>();
        ClientSpecialAbility.Stats stats = PlayedAdditionalRows.SpecialAbilityStats(Table, new Dictionary<string, int> { ["atpToBap"] = 2200 }, unmapped);
        var both = new List<string>();
        ClientSpecialAbility.Stats two = PlayedAdditionalRows.SpecialAbilityStats(Table, new Dictionary<string, int> { ["atpToBap"] = 2200, ["strToInt"] = 700 }, both);
        Assert.Multiple(() => {
            Assert.That(unmapped, Is.Empty);
            Assert.That(stats.Conversion, Is.EqualTo(new AdditionalEffectMetadataStatus.StatConversion(Maple2.Model.Enum.BasicAttribute.Accuracy, Maple2.Model.Enum.BasicAttribute.BonusAtk, 2.2f)));
            Assert.That(stats.Rates, Is.Empty);
            Assert.That(both, Has.Count.EqualTo(1));
            Assert.That(two.Conversion, Is.Not.Null);
        });
    }

    // A conversion the pack has too holds nothing and keeps the pack's status; one the pack lacks is taken.
    [Test]
    public void AConversionLikeThePacksHoldsNothing() {
        AdditionalEffectMetadataStatus status = PlayedAdditionalRows.WithStats(Levels(10800213).Pack.Status, new ClientSpecialAbility.Stats());
        AdditionalEffectMetadataStatus pack = status with {
            Conversion = new AdditionalEffectMetadataStatus.StatConversion(Maple2.Model.Enum.BasicAttribute.Strength, Maple2.Model.Enum.BasicAttribute.Intelligence, 0.7f),
        };
        var abilities = new Dictionary<string, int> { ["strToInt"] = 700 };
        string? same = PlayedAdditionalRows.SpecialAbility(Table, abilities, pack, false, out AdditionalEffectMetadataStatus? unchanged);
        string? taken = PlayedAdditionalRows.SpecialAbility(Table, abilities, status, false, out AdditionalEffectMetadataStatus? converted);
        Assert.Multiple(() => {
            Assert.That(same, Is.Null);
            Assert.That(unchanged, Is.Null);
            Assert.That(taken, Is.Null);
            Assert.That(converted!.Conversion, Is.EqualTo(pack.Conversion));
        });
    }

    private static (List<AdditionalEffectMetadata>? Levels, List<string> Unread) Own(int id) {
        Dictionary<int, PlayedClientAdditionalEffects.Entry> played = PlayedClientAdditionalEffects.Read([Xml(Played)], _ => true);
        var mapper = (AdditionalEffectMapper) RuntimeHelpers.GetUninitializedObject(typeof(AdditionalEffectMapper));
        typeof(AdditionalEffectMapper).GetField("played", Any)!.SetValue(mapper, played);
        typeof(AdditionalEffectMapper).GetField("abilities", Any)!.SetValue(mapper, Table);
        var rates = (CooldownGroupRates) RuntimeHelpers.GetUninitializedObject(typeof(CooldownGroupRates));
        typeof(CooldownGroupRates).GetField("rates", Any)!.SetValue(rates, System.Activator.CreateInstance(typeof(CooldownGroupRates).GetField("rates", Any)!.FieldType));
        typeof(AdditionalEffectMapper).GetField("groupRates", Any)!.SetValue(mapper, rates);
        var unread = new List<string>();
        object?[] args = [id, played[id], unread, null];
        var levels = (List<AdditionalEffectMetadata>?) typeof(AdditionalEffectMapper).GetMethod("Own", Any)!.Invoke(mapper, args);
        return (levels, unread);
    }

    // An effect only the played client has takes its <SpecialAbility> where every type has a server stat.
    [Test]
    public void APlayedOnlyEffectTakesItsSpecialAbility() {
        (List<AdditionalEffectMetadata>? levels, List<string> unread) = Own(10800215);
        Assert.Multiple(() => {
            Assert.That(unread, Is.Empty);
            Assert.That(levels, Has.Count.EqualTo(1));
            Assert.That(levels![0].Property.DurationTick, Is.EqualTo(60000));
            Assert.That(levels[0].Status.Rates[Maple2.Model.Enum.BasicAttribute.MaxWeaponAtk], Is.EqualTo(0.08f));
            Assert.That(levels[0].Status.SpecialRates[Maple2.Model.Enum.SpecialAttribute.FireDamage], Is.EqualTo(0.02f));
        });
    }

    // One with a type that has no server stat (aevp) is left out.
    [Test]
    public void APlayedOnlyEffectWithATypeWithoutAStatIsLeftOut() {
        (List<AdditionalEffectMetadata>? levels, List<string> unread) = Own(10800216);
        Assert.Multiple(() => {
            Assert.That(levels, Is.Null);
            Assert.That(unread, Is.EqualTo(new[] { "SpecialAbility" }));
        });
    }

    // Every name on the pack-only list is one no played row of these tests writes.
    [Test]
    public void ThePackOnlyNamesAreNotWritten() {
        Dictionary<int, PlayedClientAdditionalEffects.Entry> played = PlayedClientAdditionalEffects.Read([Xml(Played)], _ => true);
        IReadOnlySet<string> written = PlayedAdditionalRows.Written(played.Values);
        Assert.That(PlayedAdditionalRows.PackOnly.Where(written.Contains), Is.Empty);
    }
}
