using Maple2.Model.Common;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class HomeEditRulesTests {
    [Test]
    public void Owner() {
        Assert.That(HomeEditRules.OwnsPlot(7, 7), Is.True);
        Assert.That(HomeEditRules.OwnsPlot(7, 8), Is.False);
        Assert.That(HomeEditRules.OwnsPlot(0, 0), Is.False);
        // RequestCube: a visitor (account 8) in the home of account 7 gets no plot to edit.
        Assert.That(HomeEditRules.OwnsPlot(plotOwnerId: 7, accountId: 8), Is.False);
    }

    [Test]
    public void PortalBytes() {
        var writer = new ByteWriter();
        writer.WriteByte(0);
        writer.WriteInt(0x00030201);
        writer.WriteUnicodeString("gate");
        writer.WriteByte(1);
        writer.WriteByte(2);
        writer.WriteUnicodeString("target");
        var reader = new ByteReader(writer.ToArray(), 0);
        (Vector3B cell, string name, byte method, byte destination, string target) = HomeEditRules.ReadPortal(reader);
        Assert.That(cell, Is.EqualTo(new Vector3B(1, 2, 3)));
        Assert.That(name, Is.EqualTo("gate"));
        Assert.That(method, Is.EqualTo(1));
        Assert.That(destination, Is.EqualTo(2));
        Assert.That(target, Is.EqualTo("target"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void NoticeBytes() {
        var writer = new ByteWriter();
        writer.WriteByte(1);
        writer.WriteInt(0x00030201);
        writer.WriteUnicodeString("hello");
        writer.WriteByte(3);
        var reader = new ByteReader(writer.ToArray(), 0);
        (Vector3B cell, string notice, byte distance) = HomeEditRules.ReadNotice(reader);
        Assert.That(cell, Is.EqualTo(new Vector3B(1, 2, 3)));
        Assert.That(notice, Is.EqualTo("hello"));
        Assert.That(distance, Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Portal() {
        string[] others = ["a", "b"];
        Assert.That(HomeEditRules.PortalAllowed("c", 1, 2, "", others), Is.True);
        Assert.That(HomeEditRules.PortalAllowed("a", 0, 0, "", others), Is.False);
        Assert.That(HomeEditRules.PortalAllowed("c", 2, 0, "", others), Is.False);
        Assert.That(HomeEditRules.PortalAllowed("c", 0, 3, "", others), Is.False);
        Assert.That(HomeEditRules.PortalAllowed(new string('x', HomeEditRules.MaxPortalNameLength + 1), 0, 0, "", others), Is.False);
        Assert.That(HomeEditRules.PortalAllowed("c", 0, 0, new string('x', HomeEditRules.MaxPortalTargetLength + 1), others), Is.False);
    }

    // A map (1) or a friend's home (2) is sent as digits; a target that is not a number is refused before anything
    // changes (it threw in the portal's spawn after the cube's settings were taken).
    [Test]
    public void PortalTarget_MustBeANumberForAMapOrAHome() {
        string[] others = [];
        Assert.Multiple(() => {
            Assert.That(HomeEditRules.PortalAllowed("c", 0, 1, "2000062", others), Is.True);
            Assert.That(HomeEditRules.PortalAllowed("c", 0, 1, "", others), Is.True);
            Assert.That(HomeEditRules.PortalAllowed("c", 0, 1, "abc", others), Is.False);
            Assert.That(HomeEditRules.PortalAllowed("c", 0, 2, "123456789012", others), Is.True);
            Assert.That(HomeEditRules.PortalAllowed("c", 0, 2, "x1", others), Is.False);
            Assert.That(HomeEditRules.PortalAllowed("c", 0, 0, "anything", others), Is.True);
        });
    }

    [Test]
    public void Notice() {
        Assert.That(HomeEditRules.NoticeAllowed(new string('x', HomeEditRules.MaxNoticeLength)), Is.True);
        Assert.That(HomeEditRules.NoticeAllowed(new string('x', HomeEditRules.MaxNoticeLength + 1)), Is.False);
    }
}
