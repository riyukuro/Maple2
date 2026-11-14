using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// FunctionCube 0x002E mode 5 on a nest: the drop of a feeding (rewardItemByFeeding) or of a play (the feed item) is
// made before the feed is taken and before the play counts. A drop the server cannot make (an item it has no data
// for) refuses the use whole: the owner keeps the feed, and a visitor's play neither counts for nurturing_play nor
// is left unrecorded with the trophy counted (a repeat would count again).
public class NurturingRewardOrderTests {
    private const int CubeId = 50800010;
    private const int TermId = 153;
    private const string Tag = "NurturingGhostCats";
    private const int FeedItem = 30001433;
    private const int FeedReward = 30001500;
    private const int PlayTrophy = 92000095;
    private const int StageGift = 30001600;
    private const int MailGift = 30001135;

    private TestGameSession test = null!;

    // The session stands in a home next to a nest; `visitor` makes the home another account's. The feed item is in
    // the bag when `withFeed`; `registered` lists the items the server has data for.
    // `mailGift` is the visitor's rewardItemByFeeding (the owner's mail); `stageExp` is the stored growth, so the owner's
    // use of a grown nest takes the stage gift.
    private (GameSession Session, FieldFunctionInteract Nest) Start(bool visitor, bool withFeed, params int[] registered) =>
        Start(visitor, withFeed, mailGift: 0, stageExp: 0, registered);

    private (GameSession Session, FieldFunctionInteract Nest) Start(bool visitor, bool withFeed, int mailGift, long stageExp, params int[] registered) {
        test = new TestGameSession();
        if (withFeed) {
            test.Store(TestGameSession.Metadata(FeedItem));
        }
        test.AddAchievement(TestGameSession.Trophy(PlayTrophy, ConditionType.nurturing_play, [1, 2], codeRange: (0, int.MaxValue)));
        GameSession session = test.Start();
        test.Register(registered.Select(id => TestGameSession.Metadata(id)).ToArray());
        Plot plot = test.EnterHome();
        long ownerId = visitor ? test.AccountId + 1000 : test.AccountId;
        plot.OwnerId = ownerId;
        test.SetServerTable(new ConstantsTable(MasteryObjectInteractionDistance: 300, NurturingQuestTag: Tag));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        test.SetServerTable(new TermTagTable(new Dictionary<int, TermTagTable.Entry> {
            [TermId] = new(TermId, Tag, now - 3600, now + 3600),
        }));
        RewardItem rewardFeed = visitor ? (mailGift == 0 ? default : new RewardItem(mailGift, 1, 1)) : new RewardItem(FeedReward, 1, 1);
        var data = new FunctionCubeMetadata.NurturingData(new RewardItem(FeedItem, 1, 1), rewardFeed,
            [new FunctionCubeMetadata.NurturingData.Growth(100, 1, new RewardItem(StageGift, 1, 1))], Tag);
        var cube = new InteractCube(new Vector3B(0, 0, 0), new FunctionCubeMetadata(CubeId, 0, ConfigurableCubeType.None,
            InteractCubeState.Available, InteractCubeControlType.Nurturing, [], 0, data));
        using (GameStorage.Request db = test.Storage.Context()) {
            Nurturing created = db.CreateNurturing(ownerId, data, CubeId)!;
            if (stageExp > 0) {
                created.Exp = stageExp;
                cube.Nurturing = created;
                db.UpdateNurturing(ownerId, cube);
            }
        }
        FieldFunctionInteract nest = test.AddFunctionCube(cube);
        nest.Position = session.Player.Position;
        return (session, nest);
    }

    private void Use(GameSession session, FieldFunctionInteract nest) {
        var handler = new FunctionCubeHandler {
            FunctionCubeMetadataStorage = session.FunctionCubeMetadata,
            ServerTableMetadataStorage = test.ServerTableMetadata,
        };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteInt(1);
            writer.WriteUnicodeString(nest.InteractCube.Id);
            writer.WriteByte(0);
        }));
    }

    private Nurturing Stored(long ownerId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetNurturing(ownerId, CubeId, new FunctionCubeMetadata.NurturingData(default, default, [new(100, 1, default)], Tag))!;
    }

    private long StoredExp(long ownerId) => Stored(ownerId).Exp;

    [TestCase(true, 0, TestName = "Feed_RewardMade_TakesTheFeed")]
    [TestCase(false, 1, TestName = "Feed_RewardNotMade_KeepsTheFeed")]
    public void Feed_TakesTheFeedOnlyWithItsDrop(bool rewardKnown, int feedLeft) {
        (GameSession session, FieldFunctionInteract nest) = rewardKnown ? Start(false, true, FeedReward) : Start(false, true);

        Use(session, nest);

        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Find(FeedItem).Sum(item => item.Amount), Is.EqualTo(feedLeft));
            Assert.That(StoredExp(test.AccountId), Is.EqualTo(rewardKnown ? 10 : 0));
        });
    }

    [TestCase(true, 1L, TestName = "Play_DropMade_Counts")]
    [TestCase(false, null, TestName = "Play_DropNotMade_NotCounted")]
    public void Play_CountsOnlyWithItsDrop(bool dropKnown, long? counted) {
        (GameSession session, FieldFunctionInteract nest) = dropKnown ? Start(true, false, FeedItem) : Start(true, false);

        Use(session, nest);

        Assert.That(test.TrophyCounter(PlayTrophy), Is.EqualTo(counted));
    }

    // The owner's use of a grown nest takes the stage gift: the stage counts as claimed only with its gift made. It was
    // counted before the make, so a gift the server cannot make left the placed nest (what the field sends) claimed.
    [TestCase(true, (short) 2, TestName = "StageGift_Made_StageClaimed")]
    [TestCase(false, (short) 1, TestName = "StageGift_NotMade_StageKept")]
    public void StageGift_ClaimedOnlyWithItsItem(bool giftKnown, short claimed) {
        (GameSession session, FieldFunctionInteract nest) = giftKnown
            ? Start(false, false, mailGift: 0, stageExp: 100, StageGift)
            : Start(false, false, mailGift: 0, stageExp: 100);

        Use(session, nest);

        Assert.Multiple(() => {
            Assert.That(nest.InteractCube.Nurturing!.ClaimedGiftForStage, Is.EqualTo(claimed));
            Assert.That(Stored(test.AccountId).ClaimedGiftForStage, Is.EqualTo(claimed));
        });
    }

    // A visitor's play mails the owner the feeding gift; the mail is written only with its item. It was written first,
    // so a gift the server cannot make left the owner an empty mail.
    [TestCase(true, 1, TestName = "OwnerMail_GiftMade_Mailed")]
    [TestCase(false, 0, TestName = "OwnerMail_GiftNotMade_NoMail")]
    public void OwnerMail_OnlyWithItsGift(bool giftKnown, int mails) {
        (GameSession session, FieldFunctionInteract nest) = giftKnown
            ? Start(true, false, mailGift: MailGift, stageExp: 0, FeedItem, MailGift)
            : Start(true, false, mailGift: MailGift, stageExp: 0, FeedItem);

        Use(session, nest);

        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetAllMail(test.AccountId + 1000).Count, Is.EqualTo(mails));
    }
}
