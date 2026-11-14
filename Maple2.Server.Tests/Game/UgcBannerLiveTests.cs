using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Model.Game.Ugc;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A banner field's live slot: the current hour's design goes to the field, not an earlier hour's slot that was live.
public class UgcBannerLiveTests {
    private static BannerSlot Slot(DateTimeOffset hour, string url, bool active = false) {
        return new BannerSlot(1, hour.Year * 10000 + hour.Month * 100 + hour.Day, hour.Hour) {
            Active = active,
            Template = new UgcItemLook { Id = hour.Hour + 1, Url = url },
        };
    }

    private static bool Holds(byte[] packet, string text) {
        var writer = new ByteWriter();
        writer.WriteUnicodeString(text);
        byte[] bytes = writer.ToArray()[2..];
        return Enumerable.Range(0, packet.Length - bytes.Length + 1).Any(i => packet.AsSpan(i, bytes.Length).SequenceEqual(bytes));
    }

    // The hour before was live (its slot is kept for 4 hours): the next minute tick sends the current hour's design.
    [Test]
    public void Update_SendsTheCurrentHoursDesign() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BannerSlot earlier = Slot(now.AddHours(-1), "banner/earlier.png", active: true);
        BannerSlot current = Slot(now, "banner/current.png");
        var banner = new FieldUgcBanner(session.Field!, 1, session.Field!.MapId, [earlier, current]);
        test.TakeSent();

        banner.Update(0);

        Assert.That(earlier.Active, Is.False);
        Assert.That(current.Active, Is.True);
        byte[][] sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Ugc).ToArray();
        Assert.That(sent, Has.Length.EqualTo(1));
        Assert.That(Holds(sent[0], "banner/current.png"), Is.True);
        Assert.That(Holds(sent[0], "banner/earlier.png"), Is.False);

        banner.Update(0);
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Ugc), Is.Empty);
    }

    // A design whose picture has not arrived (no url) is not sent: the client would download an empty url.
    [Test]
    public void DesignWithoutItsPicture_IsNotSent() {
        BannerSlot live = Slot(DateTimeOffset.UtcNow, "", active: true);
        Assert.That(UgcRules.LiveDesign([live]), Is.Null);
        var banner = new UgcBanner(1, 2000001, [live]);
        var reader = new ByteReader(UgcPacket.ActivateBanner(banner).ToArray());
        reader.ReadShort();
        reader.ReadByte();
        Assert.That(reader.ReadLong(), Is.EqualTo(1));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.EqualTo(0));

        live.Template!.Url = "banner/done.png";
        Assert.That(UgcRules.LiveDesign([live]), Is.SameAs(live));
        Assert.That(Holds(UgcPacket.ActivateBanner(banner).ToArray(), "banner/done.png"), Is.True);
    }
}
