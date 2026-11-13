using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.AdditionalEffect;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// <RemoveRechargingCount skillID count>: an effect's start gives a skill recharge uses back, which the client learns
// from 0x139 (CSceneStateGame::FUN_14173fc40) alone. The KR data's one row: 10600280, the charge Focused Fire 10600181
// puts on its caster, 2 uses of 10600181. The data pack does not have the effect; the ingest takes it from the played
// client.
public class RechargeGiveBackTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int FocusedFire = 10600181;

    private const string Played = """
        <ms2>
          <additional id="10600280" mainType="2" skillGroupType="1">
            <level level="1" feature="JobChange_02">
              <Basic durationTick="10000" intervalTick="10000" resetCondition="2" maxBuffCount="50" casterIndividualEffect="1" clearEffectFromPVPZone="1" />
              <UI isHideCooltime="1" />
              <RemoveRechargingCount skillID="10600181" count="2" />
            </level>
          </additional>
          <additional id="10600222">
            <level level="1"><Basic durationTick="1000" /><Motion skillUseDisable="6" /></level>
          </additional>
          <additional id="10600223">
            <level level="1"><Basic durationTick="1000" /><Spread /></level>
          </additional>
        </ms2>
        """;

    private static Dictionary<int, PlayedClientAdditionalEffects.Entry> Read() {
        var document = new XmlDocument();
        document.LoadXml(Played);
        return PlayedClientAdditionalEffects.Read([document], _ => true);
    }

    [Test]
    public void ReadsTheRowPerLevel() {
        Dictionary<int, PlayedClientAdditionalEffects.Entry> played = Read();
        Assert.That(played[10600280].RemoveRecharge[1], Is.EqualTo(new AdditionalEffectMetadataUpdate.RemoveRechargeUses(FocusedFire, 2)));
        Assert.That(played[10600222].RemoveRecharge, Is.Empty);
    }

    // An effect only the client has is taken over when the ingest reads all of each level: <Basic>, <UI> and the row.
    [Test]
    public void TakesOverOnlyAnEffectItReadsWhole() {
        Dictionary<int, PlayedClientAdditionalEffects.Entry> played = Read();
        Assert.That(PlayedAdditionalRows.Unread(played[10600280]), Is.Empty);
        Assert.That(PlayedAdditionalRows.Unread(played[10600223]), Is.EqualTo(new[] { "Spread" }), "<Spread> is not read for an effect only the client has");
        AdditionalEffectData data = AdditionalEffectMapper.PlayedOnly(played[10600280]).Single();
        Assert.Multiple(() => {
            Assert.That(data.BasicProperty.level, Is.EqualTo(1));
            Assert.That(data.BasicProperty.durationTick, Is.EqualTo(10000));
            Assert.That(data.BasicProperty.maxBuffCount, Is.EqualTo(50));
            Assert.That(data.BasicProperty.resetCondition, Is.EqualTo(2));
            Assert.That(data.BasicProperty.casterIndividualEffect, Is.True);
            Assert.That(data.MotionProperty, Is.Not.Null);
            Assert.That(data.conditionSkill, Is.Empty);
        });
    }

    // The mapper builds the metadata from the row as from a pack row: the 2 uses of Focused Fire, a 10 s buff.
    [Test]
    public void BuildsTheMetadataOfATakenOverRow() {
        Dictionary<int, PlayedClientAdditionalEffects.Entry> played = Read();
        var mapper = (AdditionalEffectMapper) RuntimeHelpers.GetUninitializedObject(typeof(AdditionalEffectMapper));
        typeof(AdditionalEffectMapper).GetField("played", Any)!.SetValue(mapper, played);
        var rates = (CooldownGroupRates) RuntimeHelpers.GetUninitializedObject(typeof(CooldownGroupRates));
        typeof(CooldownGroupRates).GetField("rates", Any)!.SetValue(rates, System.Activator.CreateInstance(typeof(CooldownGroupRates).GetField("rates", Any)!.FieldType));
        typeof(AdditionalEffectMapper).GetField("groupRates", Any)!.SetValue(mapper, rates);
        MethodInfo build = typeof(AdditionalEffectMapper).GetMethod("Build", Any)!;
        var metadata = (AdditionalEffectMetadata) build.Invoke(mapper, [10600280, AdditionalEffectMapper.PlayedOnly(played[10600280]).Single(), 0, null])!;
        Assert.Multiple(() => {
            Assert.That(metadata.Level, Is.EqualTo(1));
            Assert.That(metadata.Property.DurationTick, Is.EqualTo(10000));
            Assert.That(metadata.Property.ResetCondition, Is.EqualTo(BuffResetCondition.Reset2));
            Assert.That(metadata.Property.CasterIndividualBuff, Is.True);
            Assert.That(metadata.Update.RemoveRecharge, Is.EqualTo(new AdditionalEffectMetadataUpdate.RemoveRechargeUses(FocusedFire, 2)));
            Assert.That(metadata.Condition.Target, Is.Null);
            Assert.That(metadata.Skills, Is.Empty);
            Assert.That(metadata.InvokeEffect, Is.Null);
        });
    }

    // Settled first, then count back, never under none out.
    [Test]
    public void GivesUsesBackAfterSettling() {
        Assert.Multiple(() => {
            Assert.That(SkillCastRules.GiveBackRecharge(10_000, 3, 1_000, 9_999, 2), Is.EqualTo((10_000L, 1)));
            Assert.That(SkillCastRules.GiveBackRecharge(10_000, 1, 1_000, 9_999, 2), Is.EqualTo((10_000L, 0)));
            Assert.That(SkillCastRules.GiveBackRecharge(10_000, 3, 1_000, 10_000, 2), Is.EqualTo((11_000L, 0)), "one came back at 10 s");
            Assert.That(SkillCastRules.GiveBackRecharge(10_000, 3, 1_000, 9_999, -1), Is.EqualTo((10_000L, 3)));
        });
    }

    [Test]
    public void WritesSkillMinAndCount() {
        ByteWriterBytes(SkillPacket.RechargeUses(FocusedFire, 0, 2), out byte[] bytes);
        var expected = new List<byte> { 0x39, 0x01 };
        expected.AddRange(System.BitConverter.GetBytes(FocusedFire));
        expected.AddRange(System.BitConverter.GetBytes(0));
        expected.AddRange(System.BitConverter.GetBytes(2));
        Assert.That(bytes, Is.EqualTo(expected.ToArray()));
        Assert.That((int) SendOp.SkillRechargeUses, Is.EqualTo(0x139));
    }

    private static void ByteWriterBytes(Maple2.PacketLib.Tools.ByteWriter writer, out byte[] bytes) {
        bytes = writer.Buffer[..writer.Length];
    }

    private static Dictionary<int, SkillCooldown> Cooldowns(GameSession session) {
        typeof(ConfigManager).GetField("session", Any)!.SetValue(session.Config, session);
        var cooldowns = new Dictionary<int, SkillCooldown>();
        typeof(ConfigManager).GetField("skillCooldowns", Any)!.SetValue(session.Config, cooldowns);
        return cooldowns;
    }

    // 3 out less 2 leaves 1 and the record; 1 out less 2 drops it, as the client ends the cooldown. The packet goes
    // either way, also with no record (the client counts the uses itself).
    [Test]
    public void ConfigKeepsOrDropsTheRecord() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        Dictionary<int, SkillCooldown> cooldowns = Cooldowns(session);
        test.TakeSent();
        long far = System.Environment.TickCount64 + 60_000;
        cooldowns[FocusedFire] = new SkillCooldown(FocusedFire, 1) { EndTick = far, Charges = 3, RechargeMs = 1_000 };
        session.Config.GiveBackRecharge(FocusedFire, 2);
        Assert.That(cooldowns[FocusedFire].Charges, Is.EqualTo(1));
        session.Config.GiveBackRecharge(FocusedFire, 2);
        Assert.That(cooldowns.ContainsKey(FocusedFire), Is.False);
        session.Config.GiveBackRecharge(FocusedFire, 2);
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(3));
        Assert.That(sent.All(packet => packet[0] == 0x39 && packet[1] == 0x01), Is.True);
    }
}
