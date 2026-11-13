using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

public class MasteryHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Mastery;

    private enum Command : byte {
        Reward = 1,
        Craft = 2,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Reward:
                HandleReward(session, packet);
                break;
            case Command.Craft:
                HandleCraft(session, packet);
                break;
        }
    }

    private void HandleReward(GameSession session, IByteReader packet) {
        // The client's id of a grade (CMasteryTable::MakeID): type * 1000 + grade.
        int rewardBoxDetails = packet.ReadInt();
        (int typeId, int level) = MasteryRules.RewardStep(rewardBoxDetails);
        var type = (MasteryType) typeId;

        if (session.Player.Value.Unlock.MasteryRewardsClaimed.TryGetValue(rewardBoxDetails, out bool isClaimed) && isClaimed) {
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_unknown));
            return;
        }

        // The client offers only a grade of its table that has a reward item; anything else was not asked there.
        if (!TableMetadata.MasteryRewardTable.Entries.TryGetValue(type, level, out MasteryRewardTable.Entry? entry) || entry.ItemId == 0) {
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_unknown));
            return;
        }

        if (session.Mastery[type] < entry.Value) {
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_invalid_level));
            return;
        }

        Item? rewardItem = session.Field?.ItemDrop.CreateItem(entry.ItemId, entry.ItemRarity, entry.ItemAmount);
        if (rewardItem == null) {
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_unknown));
            return;
        }

        if (!session.Item.Inventory.Add(rewardItem, true)) {
            session.Send(ChatPacket.Alert(StringCode.s_err_inventory));
            return;
        }

        session.Player.Value.Unlock.MasteryRewardsClaimed.Add(rewardBoxDetails, true);
        session.Send(MasteryPacket.ClaimReward(rewardBoxDetails, new List<ItemComponent>() {
            new(entry.ItemId, (short) entry.ItemRarity, entry.ItemAmount, ItemTag.None),
        }));
    }

    private void HandleCraft(GameSession session, IByteReader packet) {
        int recipeId = packet.ReadInt();

        if (!TableMetadata.MasteryRecipeTable.Entries.TryGetValue(recipeId, out MasteryRecipeTable.Entry? entry)) {
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_unknown));
            return;
        }

        // The client makes the same checks in the same order before it sends the craft.
        MasteryError error = MasteryRules.CraftError(session.Player.Value.Character.Level, entry.RequiredLevel,
            session.Mastery[entry.Type], entry.RequiredMastery, entry.RequiredQuests.Count, session.Mastery.HasRequiredQuest(entry),
            session.Currency.Meso, entry.RequiredMeso);
        if (error != 0) {
            session.Send(MasteryPacket.Error(error));
            return;
        }

        if (!session.Item.Inventory.ConsumeItemComponents(entry.RequiredItems)) {
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_lack_item));
            return;
        }

        session.Currency.ChangeMeso(-entry.RequiredMeso);

        if (!entry.NoRewardExp) {
            // The gain the client's craft dialog shows: the recipe's mastery raised by the type's ability.
            SpecialAttribute? bonus = MasteryRules.CraftBonus(entry.Type);
            int bonusTotal = bonus == null ? 0 : (int) session.Player.Stats.Values[bonus.Value].Total;
            session.Mastery[entry.Type] += MasteryRules.CraftReward(entry.RewardMastery, bonusTotal);
        }

        foreach (ItemComponent rewardItem in entry.RewardItems) {
            Item? item = session.Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
            if (item == null) {
                continue;
            }

            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }
        // KMS2 sends the crafted item list right after the bag updates, before the exp and the trophies.
        session.Send(MasteryPacket.GetCraftedItem(entry.Type, (ICollection<ItemComponent>) entry.RewardItems));
        session.Exp.AddExp(ExpType.manufacturing, message: MasteryRules.ExpReason(entry.Type));
        session.ConditionUpdate(ConditionType.mastery_manufacturing, codeLong: recipeId);
    }
}
