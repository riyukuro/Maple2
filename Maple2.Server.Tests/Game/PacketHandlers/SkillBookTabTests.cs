using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The skill book's tab requests as the client sends them (RequestSkillBookTree 0, open the book; 2, rename a tab): the
// book is sent on open; a tab's new name is taken and echoed, a banned one is refused with the ban message box, and a
// rename of a tab the book does not have changes nothing.
public class SkillBookTabTests {
    private const long TabId = 7;
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private SkillTab tab = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        SkillBookSaveTests.Setup(test);
        tab = new SkillTab("first") { Id = TabId };
        session.Config.Skill.SkillBook.SkillTabs.Add(tab);
    }

    private void Handle(Action<Maple2.PacketLib.Tools.ByteWriter> write) {
        var words = BanWordStorage.WithDynamicWords(
            new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options), () => ["badword"]);
        new SkillBookHandler { BanWordStorage = words }.Handle(session, TestGameSession.Packet(write));
    }

    private void Rename(long tabId, string name) => Handle(writer => {
        writer.WriteByte(2);
        writer.WriteLong(tabId);
        writer.WriteUnicodeString(name);
    });

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Open_SendsTheBook() {
        Handle(writer => writer.WriteByte(0));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(SkillBookPacket.Load(session.Config.Skill.SkillBook)) }));
    }

    [Test]
    public void Rename_TakesTheNameAndEchoesIt() {
        Rename(TabId, "second");
        Assert.Multiple(() => {
            Assert.That(tab.Name, Is.EqualTo("second"));
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(SkillBookPacket.Rename(tab)) }));
        });
    }

    [Test]
    public void Rename_ABannedName_IsRefused() {
        Rename(TabId, "badword");
        Assert.Multiple(() => {
            Assert.That(tab.Name, Is.EqualTo("first"));
            Assert.That(test.TakeSent().Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Notice }));
        });
    }

    [Test]
    public void Rename_ATabTheBookHasNot_ChangesNothing() {
        Rename(TabId + 1, "second");
        Assert.Multiple(() => {
            Assert.That(tab.Name, Is.EqualTo("first"));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }
}
