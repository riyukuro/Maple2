using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Database.Extensions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 66 asks for the bill of a blueprint uid (SendRequestCubeMode66, uiugccubebilldialog.cpp:316). The bill
// was built for any stored layout by uid; it is now built, as a preview is, only for a blueprint in the own bag or
// listed on the market.
public class CalculateEstimateBlueprintTests {
    private const int BlueprintItemId = 35200001;
    private TestGameSession test = null!;
    private long uid;

    private GameSession Start() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        GameSession session = test.Start();
        using GameStorage.Request db = test.Storage.Context();
        HomeLayout stored = db.SaveHomeLayout(new HomeLayout(1, "blueprint", 14, 5, DateTimeOffset.UnixEpoch, [])) ?? throw new InvalidOperationException("layout not saved");
        uid = stored.Uid;
        return session;
    }

    private int Estimates(GameSession session) {
        test.TakeSent();
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(66);
            writer.WriteLong(uid);
            writer.WriteBool(true);
        }));
        int count = 0;
        foreach (byte[] sent in test.TakeSent()) {
            // ResponseCube, mode 67: the bill.
            if (sent.Length > 2 && sent[2] == 67) {
                count++;
            }
        }
        return count;
    }

    [Test]
    public void NotOwnedNotListed_NoEstimate() {
        GameSession session = Start();

        Assert.That(Estimates(session), Is.Zero);
    }

    [Test]
    public void InOwnBag_Estimates() {
        GameSession session = Start();
        Item item = test.Store(TestGameSession.Metadata(BlueprintItemId));
        item.Blueprint = new ItemBlueprint { BlueprintUid = uid };
        Assert.That(session.Item.Inventory.Add(item), Is.True);

        Assert.That(Estimates(session), Is.EqualTo(1));
    }

    [Test]
    public void Listed_Estimates() {
        GameSession session = Start();
        ItemMetadata metadata = TestGameSession.Metadata(BlueprintItemId);
        test.Register(metadata);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.CreateUgcMarketItem(new UgcMarketItem(metadata) {
                Price = 500,
                TabId = 1,
                PromotionEndTime = 0,
                Status = UgcMarketListingStatus.Active,
                ListingEndTime = DateTime.Now.AddDays(1).ToEpochSeconds(),
                SellerAccountId = 2,
                SellerCharacterId = 2,
                SellerCharacterName = "Seller",
                Description = string.Empty,
                Tags = [],
                Look = new UgcItemLook(),
                Blueprint = new ItemBlueprint { BlueprintUid = uid },
            }), Is.Not.Null);
        }

        Assert.That(Estimates(session), Is.EqualTo(1));
    }
}
