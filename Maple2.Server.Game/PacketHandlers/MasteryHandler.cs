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

    // Both requests come only from the mastery window (uimasterydialog.cpp:507/522/589), which lists gathering,
    // farming, alchemy and cooking only with MasterySecond on and pet taming only with PetBattle on
    // (UIMasteryDialog::UpdateSubTabs and FUN_140eb86b0, :667-683, :1823-1841).
    public const string SecondFeature = "MasterySecond";

    public static string? FeatureOf(MasteryType type) => type switch {
        MasteryType.Gathering or MasteryType.Farming or MasteryType.Alchemist or MasteryType.Cooking => SecondFeature,
        MasteryType.PetTaming => PetHandler.ComposeFeature,
        _ => null,
    };

    private static bool Offered(GameSession session, MasteryType type) =>
        FeatureOf(type) is not { } feature || session.ServerTableMetadata.FeatureTable.IsOn(feature);

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
        if (!Offered(session, type)) {
            return; // no tab for this mastery: only a forged request gets here
        }

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

        if (!session.Item.Inventory.HasRoomFor(rewardItem)) {
            session.Send(ChatPacket.Alert(StringCode.s_err_inventory));
            return;
        }

        // The claim is in the database before its box is given (as the premium club claim), so a crash between them
        // cannot give the box twice. A failed save takes the mark back out and gives nothing.
        IDictionary<int, bool> claimed = session.Player.Value.Unlock.MasteryRewardsClaimed;
        bool marked = false;
        if (!session.TrySavePurchase(db => {
            marked = true;
            claimed[rewardBoxDetails] = true;
            return db.SaveMasteryRewardsClaimed(session.Player);
        })) {
            if (marked) {
                claimed.Remove(rewardBoxDetails);
            }
            Logger.Warning("Mastery: reward {RewardId} claim not stored, nothing given", rewardBoxDetails);
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_unknown));
            return;
        }

        // The stored claim's box: to the inventory, or by mail when the inventory no longer takes it.
        if (!session.Item.Inventory.Add(rewardItem, true, commit: true) && !session.Item.MailItem(rewardItem)) {
            Logger.Error("Mastery: claimed reward {RewardId} item {ItemId} x{Amount} neither added nor mailed", rewardBoxDetails, rewardItem.Id, rewardItem.Amount);
        }
        session.Send(MasteryPacket.ClaimReward(rewardBoxDetails, new List<ItemComponent>() {
            new(entry.ItemId, (short) entry.ItemRarity, entry.ItemAmount, ItemTag.None),
        }));
    }

    private void HandleCraft(GameSession session, IByteReader packet) {
        int recipeId = packet.ReadInt();

        // A gathering recipe (mining, gathering, breeding, farming) is not a craft: it costs nothing and is
        // got by gathering its object, so a craft of one is answered as an unknown recipe.
        if (!TableMetadata.MasteryRecipeTable.Entries.TryGetValue(recipeId, out MasteryRecipeTable.Entry? entry) || !MasteryRules.IsCraft(entry.Type)) {
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_unknown));
            return;
        }
        if (!Offered(session, entry.Type)) {
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
        // The fee is held here without a packet (the materials' packets go out before it was taken, and a spend on
        // another thread could come in between), given back when the materials cannot be taken; the packet goes
        // where the take was. A short balance here gets the check's lack of meso answer.
        if (!session.Currency.TryTakeMeso(entry.RequiredMeso, notify: false)) {
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_lack_meso));
            return;
        }

        if (!session.Item.Inventory.ConsumeCraftIngredients(entry.RequiredItems)) {
            session.Currency.ReturnMeso(entry.RequiredMeso, notify: false);
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_lack_item));
            return;
        }

        session.Currency.NotifyMeso();

        if (!entry.NoRewardExp) {
            // The gain the client's craft dialog shows: the recipe's mastery raised by the type's ability.
            SpecialAttribute? bonus = MasteryRules.CraftBonus(entry.Type);
            int bonusTotal = bonus == null ? 0 : (int) session.Player.Stats.Values[bonus.Value].Total;
            int gain = MasteryRules.CraftReward(entry.RewardMastery, bonusTotal)
                * session.Mastery.DoubleFactor(MasteryRules.DoubleMastery(entry.Type));
            if (entry.Type == MasteryType.PetTaming) {
                // A snare craft is pet taming mastery other than a capture: a running PetMastery event's other rate,
                // named as a craft (not 3200) so the client shows the other rate's event text.
                int otherRate = EventSaleRules.PetMasteryRates(session.FindEvent(GameEventType.PetMastery)).Other;
                session.Mastery.Add(entry.Type, EventSaleRules.PetMasteryWithEvent(gain, otherRate), (int) ExpMessageCode.manufacturing);
            } else {
                session.Mastery[entry.Type] += gain;
            }
        }

        int factor = session.Mastery.DoubleFactor(MasteryRules.DoubleReward(entry.Type));
        foreach (ItemComponent rewardItem in entry.RewardItems) {
            Item? item = session.Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount * factor);
            if (item == null) {
                continue;
            }

            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }
        // KMS2 sends the crafted item list right after the bag updates, before the exp and the trophies.
        session.Send(MasteryPacket.GetCraftedItem(entry.Type, (ICollection<ItemComponent>) entry.RewardItems));
        // A recipe with exceptRewardExp gives no exp, as a gather of one gives none (MasteryManager.Gather). The KR quest
        // recipes have it (70000001 costs 10 meso and no ingredients, so its craft was a repeatable exp source).
        if (!entry.NoRewardExp) {
            session.Exp.AddExp(ExpType.manufacturing, message: MasteryRules.ExpReason(entry.Type));
        }
        session.ConditionUpdate(ConditionType.mastery_manufacturing, codeLong: recipeId);
    }
}
