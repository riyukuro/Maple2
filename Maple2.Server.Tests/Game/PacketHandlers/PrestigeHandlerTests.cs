using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The prestige claims (level reward and mission reward): the answer first, then the item, as KMS2 sends them,
// and one reward per claim.
public class PrestigeHandlerTests {
    private const int RewardId = 20000001;
    private const int MissionId = 7;

    private TestGameSession test = null!;
    private PrestigeHandler handler = null!;

    private GameSession Login(bool fullBag = false, int slotMax = 100, int openSlots = 0) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(RewardId, slotMax: slotMax));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata> {
            [3] = new(3, PrestigeAwardType.item, RewardId, 1, 2),
        }));
        test.SetTable(new PrestigeMissionTable(new Dictionary<int, PrestigeMissionMetadata> {
            [MissionId] = new(MissionId, 5, new ItemComponent(RewardId, 1, 4, ItemTag.None)),
        }));
        if (fullBag) {
            test.Store(TestGameSession.Metadata(20000009));
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = [(short) (1 + openSlots), 0] });
        }
        GameSession session = test.Start();
        handler = new PrestigeHandler { TableMetadata = test.TableMetadata };
        session.Exp.PrestigeLevel = 3;
        session.Exp.PrestigeRewardsClaimed.Clear();
        session.Exp.PrestigeMissions.Clear();
        session.Exp.PrestigeMissions.Add(new PrestigeMission(MissionId) { GainedLevels = 5 });
        test.TakeSent();
        return session;
    }

    private void Claim(GameSession session, byte command, int id) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteInt(id);
        }));
    }

    private static int Held(GameSession session) => session.Item.Inventory.Find(RewardId).Sum(item => item.Amount);

    [Test]
    public void LevelReward_TheClaimComesBeforeTheItemAndIsPaidOnce() {
        GameSession session = Login();

        Claim(session, 3, 3);

        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Convert.ToHexString(PrestigePacket.ClaimReward(3).ToArray())));
        Assert.That(sent, Has.Count.GreaterThan(1));
        Assert.That(sent.Skip(1).Select(TestGameSession.Op), Is.All.EqualTo(SendOp.ItemInventory));
        Assert.That(Held(session), Is.EqualTo(2));

        Claim(session, 3, 3);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held(session), Is.EqualTo(2));
        Assert.That(test.Errors, Is.Empty);
    }

    // A level above the account's, and a level without a reward row: no answer, nothing given.
    [TestCase(4)]
    [TestCase(2)]
    public void LevelReward_OfALevelNotReachedOrWithoutARowGivesNothing(int level) {
        GameSession session = Login();

        Claim(session, 3, level);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held(session), Is.EqualTo(0));
        Assert.That(session.Exp.PrestigeRewardsClaimed, Is.Empty);
    }

    // A full bag: the notice, and the level can still be claimed.
    [Test]
    public void LevelReward_WithAFullBagStaysClaimable() {
        GameSession session = Login(fullBag: true);
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Misc), Is.EqualTo(0));

        Claim(session, 3, 3);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.UserChat }));
        Assert.That(Held(session), Is.EqualTo(0));
        Assert.That(session.Exp.PrestigeRewardsClaimed, Is.Empty);
    }

    // A reward of more pieces than the open slots hold (two pieces that do not stack, one open slot): refused
    // whole, the level can still be claimed.
    [Test]
    public void LevelReward_ThatDoesNotFitWholeStaysClaimable() {
        GameSession session = Login(fullBag: true, slotMax: 1, openSlots: 1);
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Misc), Is.EqualTo(1));

        Claim(session, 3, 3);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.UserChat }));
        Assert.That(Held(session), Is.EqualTo(0));
        Assert.That(session.Exp.PrestigeRewardsClaimed, Is.Empty);
    }

    [Test]
    public void MissionReward_TheMissionsComeBeforeTheItemAndArePaidOnce() {
        GameSession session = Login();

        Claim(session, 5, MissionId);

        List<byte[]> sent = test.TakeSent();
        Assert.That(TestGameSession.Op(sent[0]), Is.EqualTo(SendOp.Prestige));
        Assert.That(Convert.ToHexString(sent[0])[4..], Is.EqualTo("06" + "01" + "01000000" + "0700000000000000" + "0500000000000000" + "01"));
        Assert.That(sent, Has.Count.GreaterThan(1));
        Assert.That(sent.Skip(1).Select(TestGameSession.Op), Is.All.EqualTo(SendOp.ItemInventory));
        Assert.That(Held(session), Is.EqualTo(4));

        Claim(session, 5, MissionId);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held(session), Is.EqualTo(4));
    }

    // A mission whose levels are not gained yet, and a mission the account does not have.
    [Test]
    public void MissionReward_NotReachedGivesNothing() {
        GameSession session = Login();
        session.Exp.PrestigeMissions[0].GainedLevels = 4;

        Claim(session, 5, MissionId);
        Claim(session, 5, MissionId + 1);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Held(session), Is.EqualTo(0));
        Assert.That(session.Exp.PrestigeMissions[0].Awarded, Is.False);
    }

    // A claim is one-time, as in the client: it keeps the claim set until a Prestige 0 replaces it and adds to it at a
    // Prestige 4 (scenestategame.cpp:8870, :8876), and has no reset. The weekly reset keeps the claims (it starts only
    // the missions again, PrestigeWeeklyMissionTests).
    [Test]
    public void LevelReward_TheWeeklyResetKeepsTheClaim() {
        GameSession session = Login();
        Claim(session, 3, 3);
        test.TakeSent();

        session.WeeklyReset();

        Assert.That(session.Exp.PrestigeRewardsClaimed, Does.Contain(3));
        // Only the weekly missions' list (Prestige 7) is sent again; no Prestige 0 replaces the claim set.
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Prestige).Select(packet => packet[2]),
            Is.EqualTo(new byte[] { 7 }));
        Claim(session, 3, 3);
        Assert.That(Held(session), Is.EqualTo(2));
    }
}
