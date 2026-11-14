using System.Xml;
using Maple2.File.Ingest.Utils;
using Maple2.File.Parser.Xml.Npc;

namespace Maple2.Server.Tests.Ingest;

// An NPC only the played client has, read from its npcdata layout (basic values on <environment>, speeds on <model>).
// Synthetic values.
public class PlayedNpcDataTests {
    private static XmlElement Environment(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return (XmlElement) document.DocumentElement!.SelectSingleNode("environment")!;
    }

    [Test]
    public void ReadsTheNpcdataLayout() {
        NpcData data = PlayedNpcData.Read(Environment(
            "<npc id=\"1\"><environment feature=\"F\" npcAttackGroup=\"1\" npcDefenseGroup=\"2\" hitImmune=\"99\" level=\"70\" class=\"6\" " +
            "kind=\"13\" friendly=\"2\" shopId=\"7\" rotationDisabled=\"1\" propertyTags=\"keep_battle,no_die\" raceString=\"mystic\" " +
            "customExp=\"0\" lookAtMyPCWhenTalking=\"0\" stringID=\"Boss\">" +
            "<model kfm=\"K\" scale=\"1.45\" shadowScale=\"950\" rotationSpeed=\"720\" walkSpeed=\"250\" runSpeed=\"500\" />" +
            "<distance avoid=\"400\" sight=\"900\" />" +
            "<skill ids=\"10,11\" levels=\"1,2\" />" +
            "<dead time=\"6\" defaultaction=\"Dead_A\" />" +
            "<normal action=\"Idle_A,Walk_A\" prob=\"5000,5000\" movearea=\"150\" />" +
            "<aiInfo path=\"AI_X.xml\" />" +
            "<effectdummy><dummy name=\"Eff_Head\" /></effectdummy>" +
            "</environment></npc>"));

        Assert.Multiple(() => {
            Assert.That(data.basic.npcAttackGroup, Is.EqualTo(1));
            Assert.That(data.basic.npcDefenseGroup, Is.EqualTo(2));
            Assert.That(data.basic.hitImmune, Is.EqualTo(99));
            Assert.That(data.basic.level, Is.EqualTo(70));
            Assert.That(data.basic.@class, Is.EqualTo(6));
            Assert.That(data.basic.kind, Is.EqualTo(13));
            Assert.That(data.basic.friendly, Is.EqualTo(2));
            Assert.That(data.basic.shopId, Is.EqualTo(7));
            Assert.That(data.basic.rotationDisabled, Is.True);
            Assert.That(data.basic.propertyTags, Is.EqualTo(new[] { "keep_battle", "no_die" }));
            Assert.That(data.basic.raceString, Is.EqualTo(new[] { "mystic" }));
            Assert.That(data.basic.stringID, Is.EqualTo("Boss"));
            Assert.That(data.exp.customExp, Is.EqualTo(0));
            Assert.That(data.lookattarget.lookAtMyPCWhenTalking, Is.EqualTo(0));
            Assert.That(data.lookattarget.useTalkMotion, Is.EqualTo(1));
            Assert.That(data.model.kfm, Is.EqualTo("K"));
            Assert.That(data.model.scale, Is.EqualTo(1.45f));
            Assert.That(data.speed.rotation, Is.EqualTo(720f));
            Assert.That(data.speed.walk, Is.EqualTo(250f));
            Assert.That(data.speed.run, Is.EqualTo(500f));
            Assert.That(data.distance.avoid, Is.EqualTo(400f));
            Assert.That(data.distance.sight, Is.EqualTo(900f));
            Assert.That(data.distance.sightHeightUP, Is.EqualTo(200f));
            Assert.That(data.distance.sightHeightDown, Is.EqualTo(50f));
            Assert.That(data.skill.ids, Is.EqualTo(new[] { 10, 11 }));
            Assert.That(data.skill.levels, Is.EqualTo(new short[] { 1, 2 }));
            Assert.That(data.dead.time, Is.EqualTo(6f));
            Assert.That(data.dead.count, Is.EqualTo(0));
            Assert.That(data.dead.revival, Is.EqualTo(0));
            Assert.That(data.normal.action, Is.EqualTo(new[] { "Idle_A", "Walk_A" }));
            Assert.That(data.normal.movearea, Is.EqualTo(150));
            Assert.That(data.aiInfo.path, Is.EqualTo("AI_X.xml"));
        });
    }

    // What the played file leaves out is the client's default (CNpcData(), Unk_NpcDead(), Unk_NpcNormal()), not the
    // parser's (kind 3, dead time 2 and count 1, movearea 200, customExp 0).
    [Test]
    public void MissingValuesAreTheClientDefaults() {
        NpcData data = PlayedNpcData.Read(Environment("<npc id=\"1\"><environment><model kfm=\"K\" /></environment></npc>"));

        Assert.Multiple(() => {
            Assert.That(data.basic.kind, Is.EqualTo(0));
            Assert.That(data.basic.level, Is.EqualTo(1));
            Assert.That(data.basic.@class, Is.EqualTo(1));
            Assert.That(data.basic.friendly, Is.EqualTo(0));
            Assert.That(data.exp.customExp, Is.EqualTo(-1));
            Assert.That(data.lookattarget.lookAtMyPCWhenTalking, Is.EqualTo(1));
            Assert.That(data.lookattarget.useTalkMotion, Is.EqualTo(1));
            Assert.That(data.speed.rotation, Is.EqualTo(0f));
            Assert.That(data.speed.walk, Is.EqualTo(0f));
            Assert.That(data.speed.run, Is.EqualTo(0f));
            Assert.That(data.dead.time, Is.EqualTo(0f));
            Assert.That(data.dead.count, Is.EqualTo(0));
            Assert.That(data.dead.lifeTime, Is.EqualTo(0f));
            Assert.That(data.dead.extendRoomTime, Is.EqualTo(0));
            Assert.That(data.normal.movearea, Is.EqualTo(0));
            Assert.That(data.distance.sightHeightUP, Is.EqualTo(200f));
            Assert.That(data.model.scale, Is.EqualTo(1f));
            Assert.That(data.model.anispeed, Is.EqualTo(1f));
            Assert.That(data.collision.shape, Is.Empty);
            Assert.That(data.skill.ids, Is.Empty);
            Assert.That(data.aiInfo.path, Is.EqualTo(PlayedNpcData.DefaultAi));
        });
    }

    [Test]
    public void FriendlyNpcWithoutAiKeepsNone() {
        NpcData data = PlayedNpcData.Read(Environment("<npc id=\"1\"><environment friendly=\"2\" /></npc>"));
        Assert.That(data.aiInfo.path, Is.Empty);
    }
}
