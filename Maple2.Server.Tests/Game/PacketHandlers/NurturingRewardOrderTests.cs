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

    private TestGameSession test = null!;

    // The session stands in a home next to a nest; `visitor` makes the home another account's. The feed item is in
    // the bag when `withFeed`; `registered` lists the items the server has data for.
    private (GameSession Session, FieldFunctionInteract Nest) Start(bool visitor, bool withFeed, params int[] registered) {
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
        var data = new FunctionCubeMetadata.NurturingData(new RewardItem(FeedItem, 1, 1), visitor ? default : new RewardItem(FeedReward, 1, 1),
            [new FunctionCubeMetadata.NurturingData.Growth(100, 1, default)], Tag);
        using (GameStorage.Request db = test.Storage.Context()) {
            db.CreateNurturing(ownerId, data, CubeId);
        }
        var cube = new InteractCube(new Vector3B(0, 0, 0), new FunctionCubeMetadata(CubeId, 0, ConfigurableCubeType.None,
            InteractCubeState.Available, InteractCubeControlType.Nurturing, [], 0, data));
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

    private long StoredExp(long ownerId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetNurturing(ownerId, CubeId, new FunctionCubeMetadata.NurturingData(default, default, [new(100, 1, default)], Tag))!.Exp;
    }

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
}
