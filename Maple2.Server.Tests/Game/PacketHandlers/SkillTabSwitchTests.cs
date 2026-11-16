using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The skill book's tab switch (SkillBookTree 1 with no tab saved): a tab the book has becomes the active one and its
// points go on the skills; one it has not changes nothing, and either is answered. A new tab of an honest tree is
// stored when the book has room and is not made active unless it is named so by a tab that already existed.
public class SkillTabSwitchTests {
    private const int SkillId = 10500001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        SkillBookSaveTests.Setup(test);
        session.Config.Skill.SkillBook.MaxSkillTabs = 3;
        session.Config.Skill.SkillBook.SkillTabs.Add(new SkillTab("one") { Id = 3 });
        var seven = new SkillTab("seven") { Id = 7 };
        seven.Skills[SkillId] = 4;
        session.Config.Skill.SkillBook.SkillTabs.Add(seven);
        session.Config.Skill.SkillBook.ActiveSkillTabId = 3;
    }

    private void Switch(long active) {
        new SkillBookHandler { BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(active);
            writer.WriteLong(0);
            writer.WriteInt(0);
        }));
    }

    private bool Answered() => test.TakeSent().Any(packet => TestGameSession.Op(packet) == SendOp.SkillBookTree);

    private SkillManager Skill => session.Config.Skill;

    [Test]
    public void SwitchingToATabTheBookHas_PutsItsPointsOnTheSkills() {
        Switch(7);
        Assert.Multiple(() => {
            Assert.That(Skill.SkillBook.ActiveSkillTabId, Is.EqualTo(7));
            Assert.That(Skill.SkillInfo.TreeLevel(SkillId), Is.EqualTo(4));
            Assert.That(Answered(), Is.True);
        });
    }

    [Test]
    public void SwitchingToATabTheBookHasNot_KeepsTheActiveTab() {
        Switch(7);
        test.TakeSent();
        Switch(99);
        Assert.Multiple(() => {
            Assert.That(Skill.SkillBook.ActiveSkillTabId, Is.EqualTo(7));
            Assert.That(Skill.SkillInfo.TreeLevel(SkillId), Is.EqualTo(4));
            Assert.That(Answered(), Is.True);
        });
    }

    // A new tab of a 0 point tree (any character's): stored and answered; the active tab, named as the new one, stays.
    [Test]
    public void ANewTab_IsStoredWhenTheBookHasRoom() {
        new SkillBookHandler { BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(3);
            writer.WriteLong(-1);
            writer.WriteInt(0);
            writer.WriteInt(1);
            writer.WriteClass<SkillTab>(new SkillTab("new") { Id = -1 });
        }));
        Assert.Multiple(() => {
            Assert.That(Skill.SkillBook.SkillTabs.Select(skillTab => skillTab.Name), Is.EqualTo(new[] { "one", "seven", "new" }));
            Assert.That(Skill.SkillBook.ActiveSkillTabId, Is.EqualTo(3));
            Assert.That(Answered(), Is.True);
        });
    }

    [Test]
    public void ANewTab_IsNotStoredInAFullBook() {
        Skill.SkillBook.MaxSkillTabs = 2;
        new SkillBookHandler { BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(3);
            writer.WriteLong(-1);
            writer.WriteInt(0);
            writer.WriteInt(1);
            writer.WriteClass<SkillTab>(new SkillTab("new") { Id = -1 });
        }));
        Assert.Multiple(() => {
            Assert.That(Skill.SkillBook.SkillTabs, Has.Count.EqualTo(2));
            Assert.That(Answered(), Is.False);
        });
    }
}
