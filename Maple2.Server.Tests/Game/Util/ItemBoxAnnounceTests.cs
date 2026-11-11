using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// A drop row with isAnnounce: the client shows s_msg_item_open_item_announce_to_world for the item it got from a box
// (FUN_140d2ab10 / FUN_140d2b190, unnamed/unit_140d2a7e0_chatlinkhelpers.cpp).
public class ItemBoxAnnounceTests {
    private static Item NewItem(long uid = 0) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(11300001, "11300001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Item(metadata, 4, 1) { Uid = uid };
    }

    [Test]
    public void Message_IsTheClientsKindThreeLine() {
        Assert.That(ChatUtil.ItemBoxAnnouncement(91, "Tsone", 20302072), Is.EqualTo("3,91,3,Tsone:20302072"));
    }

    // The client splits the line on ',' into source, uid, kind and args (4 parts at least), and for kind 3 splits the
    // args on ':' into exactly the name and the box item id; any other count shows nothing.
    [Test]
    public void Message_SplitsTheWayTheClientReadsIt() {
        string[] parts = ChatUtil.ItemBoxAnnouncement(1234567890123, "ab", 20301778).Split(',');
        Assert.That(parts, Has.Length.EqualTo(4));
        Assert.That(int.Parse(parts[0]), Is.EqualTo(3)); // source 3: the item is looked up by uid
        Assert.That(ulong.Parse(parts[1]), Is.EqualTo(1234567890123UL));
        Assert.That(int.Parse(parts[2]), Is.EqualTo((int) AnnounceType.s_msg_item_open_item_announce_to_world));
        string[] fields = parts[3].Split(':');
        Assert.That(fields, Is.EqualTo(new[] { "ab", "20301778" }));
    }

    [Test]
    public void OnlyFlaggedItemsThatReachedTheInventory() {
        Item flagged = NewItem();
        flagged.AnnounceOnOpen = true;
        Assert.That(ItemBoxManager.ShouldAnnounce(flagged, NewItem(91)), Is.True);
        // Currency, medals and furnishings are not inventory items: no target.
        Assert.That(ItemBoxManager.ShouldAnnounce(flagged, null), Is.False);
        Assert.That(ItemBoxManager.ShouldAnnounce(flagged, NewItem()), Is.False);
        // Rows without isAnnounce stay quiet; the flag is not set by default.
        Item plain = NewItem();
        Assert.That(plain.AnnounceOnOpen, Is.False);
        Assert.That(ItemBoxManager.ShouldAnnounce(plain, NewItem(91)), Is.False);
    }
}
