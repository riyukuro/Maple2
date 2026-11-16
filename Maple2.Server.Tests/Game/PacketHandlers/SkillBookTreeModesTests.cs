using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The skill window's RequestSkillBookTree modes other than the save, as the client sends them: 0 when the window has
// loaded (UISkillBookTreeDialog OnLoadComplete, uiskillbookautodistributedialog.cpp:1955-1957), 2 a tab's new name
// (i64 tab id, unicode name; :1054-1057) and 4 the add-tab price box's confirm (i32 UI id 0x4f2d, bool coupon check
// box; :1111-1114).
public class SkillBookTreeModesTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private SkillBookHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        Assert.That(SkillBookSaveTests.Setup(test), Is.SameAs(test.Session));
        session.Config.Skill.SkillBook.SkillTabs.Add(new SkillTab("Tab") { Id = 7 });
        session.Config.Skill.SkillBook.ActiveSkillTabId = 7;
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        handler = new SkillBookHandler { BanWordStorage = new BanWordStorage(words) };
        test.TakeSent();
    }

    private byte[][] Sent() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.SkillBookTree).ToArray();

    private static ByteReader Reply(byte[] packet, byte mode) {
        var reader = new ByteReader(packet, 2);
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    [Test]
    public void Load_SendsTheBook() {
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));

        byte[][] sent = Sent();
        Assert.That(sent, Has.Length.EqualTo(1));
        ByteReader reader = Reply(sent[0], 0);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(1), "max tabs");
            Assert.That(reader.ReadLong(), Is.EqualTo(7), "active tab");
            Assert.That(reader.ReadInt(), Is.EqualTo(1), "tab count");
            Assert.That(reader.ReadLong(), Is.EqualTo(7), "the tab's id");
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Tab"));
        });
    }

    private void Rename(long tabId, string name) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(tabId);
            writer.WriteUnicodeString(name);
        }));
    }

    [Test]
    public void Rename_NamesTheTabAndEchoesIt() {
        Rename(7, "A\u00e9");

        byte[][] sent = Sent();
        Assert.That(sent, Has.Length.EqualTo(1));
        ByteReader reader = Reply(sent[0], 2);
        Assert.Multiple(() => {
            Assert.That(session.Config.Skill.GetSkillTab(7)!.Name, Is.EqualTo("A\u00e9"));
            Assert.That(reader.ReadLong(), Is.EqualTo(7));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("A\u00e9"));
            Assert.That(reader.ReadBool(), Is.False, "no ban notice");
        });
    }

    [Test]
    public void Rename_AnotherCharactersOrNoTab_ChangesNothing() {
        Rename(8, "X");
        Rename(7, "");
        Assert.Multiple(() => {
            Assert.That(Sent(), Is.Empty);
            Assert.That(session.Config.Skill.GetSkillTab(7)!.Name, Is.EqualTo("Tab"));
        });
    }

    private void Expand(int dialogId, bool coupon) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(dialogId);
            writer.WriteBool(coupon);
        }));
    }

    // The client asks for a tab once its tabs fill the book and the player holds the price (or a coupon). With no
    // coupon the price (1000 merets, the client's constant) is taken and the book grows by one tab.
    [Test]
    public void Expand_PaidInMerets_AddsATab() {
        session.Currency.Meret = 1500;
        Expand(0x4f2d, coupon: false);

        byte[][] sent = Sent();
        Assert.That(sent, Has.Length.EqualTo(1));
        ByteReader reader = Reply(sent[0], 4);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
            Assert.That(session.Config.Skill.SkillBook.MaxSkillTabs, Is.EqualTo(2));
            Assert.That(session.Currency.Meret, Is.EqualTo(500));
        });
    }

    // Short of the price with no coupon held, or another UI id: nothing is taken or added.
    [TestCase(0x4f2d, false, 999)]
    [TestCase(0x4f2d, true, 999)]
    [TestCase(0x4f2c, false, 1500)]
    public void Expand_Unpaid_AddsNothing(int dialogId, bool coupon, long meret) {
        session.Currency.Meret = meret;
        Expand(dialogId, coupon);
        Assert.Multiple(() => {
            Assert.That(Sent(), Is.Empty);
            Assert.That(session.Config.Skill.SkillBook.MaxSkillTabs, Is.EqualTo(1));
            Assert.That(session.Currency.Meret, Is.EqualTo(meret));
        });
    }
}
