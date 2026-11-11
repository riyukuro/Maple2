using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class CharacterAbilityRulesTests {
    // The window opens at level 50 with the open quest rewarded.
    [TestCase(50, true, true)]
    [TestCase(49, true, false)]
    [TestCase(60, false, false)]
    public void Open(int level, bool questDone, bool expected) {
        Assert.That(CharacterAbilityRules.IsOpen(level, questDone), Is.EqualTo(expected));
    }

    // level - 50 + default + bonus, less the learned ones. The client's default is 10.
    [TestCase(50, 10, 0, 0, 10)]
    [TestCase(60, 10, 0, 0, 20)]
    [TestCase(60, 10, 0, 20, 0)]
    [TestCase(55, 10, 3, 4, 14)]
    public void Points(int level, int defaultPoints, int bonus, int learned, int expected) {
        Assert.That(CharacterAbilityRules.FreePoints(level, defaultPoints, bonus, learned), Is.EqualTo(expected));
    }

    [TestCase(50, false, 50, 1, true)]
    [TestCase(-1, false, 50, 1, false)] // not a table row
    [TestCase(50, true, 50, 1, false)] // learned already
    [TestCase(55, false, 54, 1, false)] // level not reached
    [TestCase(55, false, 55, 1, true)]
    [TestCase(50, false, 50, 0, false)] // no free point
    public void Learn(int requireLevel, bool learned, int level, int free, bool expected) {
        Assert.That(CharacterAbilityRules.CanLearn(requireLevel, learned, level, free), Is.EqualTo(expected));
    }

    // The cool time (10080 minutes in the client's constants) runs from the last reset; whole minutes count.
    [TestCase(0, 1000, 10080, 0)]
    [TestCase(1000, 1000, 10080, 604800)]
    [TestCase(1000, 1060, 10080, 604740)]
    [TestCase(1000, 1000 + 604800 - 1, 10080, 1)]
    [TestCase(1000, 1000 + 604800, 10080, 0)]
    [TestCase(1000, 1000 + 700000, 10080, 0)]
    public void ResetWait(long last, long now, int minutes, long expected) {
        Assert.That(CharacterAbilityRules.ResetWait(last, now, minutes), Is.EqualTo(expected));
    }

    [TestCase(3, 0, 300000, 300000, CharacterAbilityRules.ResetStep.Reset)]
    [TestCase(0, 0, 300000, 300000, CharacterAbilityRules.ResetStep.Refuse)] // nothing learned
    [TestCase(3, 1, 300000, 300000, CharacterAbilityRules.ResetStep.Refuse)] // cool time
    [TestCase(3, 0, 299999, 300000, CharacterAbilityRules.ResetStep.Refuse)] // meso
    public void Reset(int learned, long wait, long meso, long price, CharacterAbilityRules.ResetStep expected) {
        Assert.That(CharacterAbilityRules.CanReset(learned, wait, meso, price), Is.EqualTo(expected));
    }

    // One effect per learned ability, skipping unknown ids, held effects and repeats.
    [Test]
    public void MissingEffects() {
        var effects = new Dictionary<int, (int, short)> {
            [1] = (101, 1),
            [2] = (102, 1),
            [3] = (101, 1),
            [4] = (0, 1),
        };
        List<(int Id, short Level)> missing = CharacterAbilityRules.MissingEffects([1, 2, 3, 4, 5],
            id => effects.TryGetValue(id, out (int, short) effect) ? effect : null,
            (id, level) => id == 102);
        Assert.That(missing, Is.EqualTo(new List<(int, short)> { (101, 1) }));
    }

    // The c2s modes of the client's senders.
    [Test]
    public void Modes() {
        Assert.That(CharacterAbilityRules.Reset, Is.EqualTo(1));
        Assert.That(CharacterAbilityRules.Learn, Is.EqualTo(2));
        Assert.That(CharacterAbilityRules.LearnAll, Is.EqualTo(3));
    }

    // c2s mode 2: u8 2, i32 ability id. Mode 1: u8 1 alone.
    [Test]
    public void LearnRequestBytes() {
        var writer = new ByteWriter();
        writer.WriteByte(CharacterAbilityRules.Learn);
        writer.WriteInt(110001001);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(110001001));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // s2c mode 0: i32 count, count x i32 id, i64 reset time, i32 bonus points.
    [Test]
    public void LoadBytes() {
        ByteWriter writer = CharacterAbilityPacket.Load([110001001, 110001005], 1700000000, 0);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.CharacterAbility));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(110001001));
        Assert.That(reader.ReadInt(), Is.EqualTo(110001005));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 8 + 8 + 4));
    }

    // s2c mode 1: no body.
    [Test]
    public void ResetBytes() {
        ByteWriter writer = CharacterAbilityPacket.Reset();
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.CharacterAbility));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(writer.Length, Is.EqualTo(3));
    }

    // s2c mode 2: i32 ability id.
    [Test]
    public void LearnBytes() {
        ByteWriter writer = CharacterAbilityPacket.Learn(110001001);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.CharacterAbility));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(110001001));
        Assert.That(writer.Length, Is.EqualTo(7));
    }

    // The table reader: one row per ability element, rows the filter drops are left out.
    [Test]
    public void TableRows() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <ability id="1001" categoryID="1" requireLevel="50" additionalEffectID="9001" additionalEffectLevel="1" />
              <ability id="1002" categoryID="2" requireLevel="55" additionalEffectID="9002" additionalEffectLevel="2" />
              <ability id="1003" categoryID="2" requireLevel="60" additionalEffectID="9003" additionalEffectLevel="1" locale="XX" />
            </ms2>
            """);
        Dictionary<int, CharacterAbilityTable.Entry> rows = TableMapper.ReadCharacterAbilities(document, node => node.GetAttribute("locale") == "");
        Assert.That(rows.Keys, Is.EquivalentTo(new[] { 1001, 1002 }));
        Assert.That(rows[1002], Is.EqualTo(new CharacterAbilityTable.Entry(1002, 2, 55, 9002, 2)));
    }
}
