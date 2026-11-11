using System.Xml;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;
using TriggerActions = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Game.Packets;

// Client: FieldGameScoreBoardCli (0xE1) reads a byte command; 0 = byte kind (1 is the boss gauge), a narrow string
// title (DecodeStrA), int current, int most; 1 = int current; 2 closes the board.
public class ShadowExpeditionPacketTests {
    [Test]
    public void OpenBossGauge_WritesKindTitleAndPoints() {
        ByteWriter writer = ShadowExpeditionPacket.OpenBossGauge("ab", 0, 800);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ShadowExpedition));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadString(), Is.EqualTo("ab"));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(800));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + (2 + 2) + 4 + 4));
    }

    [Test]
    public void Open_TopCenterScore_WritesKindTwo() {
        ByteWriter writer = ShadowExpeditionPacket.Open(ShadowExpeditionPacket.TopCenterScore, "", 0, 0);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ShadowExpedition));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadString(), Is.EqualTo(""));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 2 + 4 + 4));
    }

    [Test]
    public void SetPoints_IsCommandOneWithInt() {
        ByteWriter writer = ShadowExpeditionPacket.SetPoints(35);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ShadowExpedition));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(35));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }

    [Test]
    public void Close_IsCommandTwo() {
        ByteWriter writer = ShadowExpeditionPacket.Close();
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ShadowExpedition));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(writer.Length, Is.EqualTo(3));
    }

    [TestCase("""<action name="shadow_expedition_open_boss_gauge" max_gauge_point="800" />""", typeof(TriggerActions.ShadowExpeditionOpenBossGauge))]
    [TestCase("""<action name="shadow_expedition_close_boss_gauge" />""", typeof(TriggerActions.ShadowExpeditionCloseBossGauge))]
    [TestCase("""<action name="give_reward_content" reward_id="39900001" />""", typeof(TriggerActions.GiveRewardContent))]
    public void Actions_Parse(string xml, System.Type type) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        XmlElement node = document.DocumentElement!;
        Assert.That(TriggerFunctionMapping.ActionMap[node.GetAttribute("name")](node.Attributes), Is.TypeOf(type));
    }
}
