using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class PrestigeHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Prestige;

    private enum Command : byte {
        RankReward = 3,
        MissionReward = 5,
    }

    // The mission reward (5) comes only from the new adventure level dialog (uiadventureleveldialog.cpp:556), which
    // the game menu and every other opener show only with AdventureLevelMission on; with it off they show the old
    // dialog, which sends the rank reward (3) only (gameeventmanagercli.cpp:930, uigamemenudialog.cpp:1494,
    // scenestategame.cpp:8877-8945).
    public const string MissionFeature = "AdventureLevelMission";

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.RankReward:
                HandleRankReward(session, packet);
                break;
            case Command.MissionReward:
                HandleMissionReward(session, packet);
                break;
        }
    }

    private void HandleRankReward(GameSession session, IByteReader packet) {
        int level = packet.ReadInt();

        if (level > session.Exp.PrestigeLevel) {
            return;
        }

        if (session.Exp.PrestigeRewardsClaimed.Contains(level)) {
            return;
        }

        if (!TableMetadata.PrestigeLevelRewardTable.Entries.TryGetValue(level, out PrestigeLevelRewardMetadata? metadata)) {
            return;
        }

        // Only an item reward is claimed. Both dialogs send no claim for a stat point reward (type 2), which the
        // server grants by level (ConfigManager), or for a level without a row (uiadventureleveldialog.cpp:525
        // ClaimLevelReward, uiadventurelevelolddialog.cpp:456 SendPrestige), and they show a reward slot only for
        // an item (type 1, uiadventureleveldialog.cpp:452). The KMS2 table has no other type.
        if (metadata.Type != PrestigeAwardType.item) {
            return;
        }

        Item? item = session.Field?.ItemDrop.CreateItem(metadata.Id, metadata.Rarity, metadata.Value);
        if (item == null) {
            return;
        }

        // Room for the whole amount; a currency, medal or furnishing reward needs no slot.
        if (!session.Item.Inventory.HasRoomFor(item)) {
            session.Send(ChatPacket.Alert(StringCode.s_err_inventory));
            return;
        }

        // KMS2 order: the claim, then the item. The claim is stored before the item's row is made, so a crash between
        // them cannot give the reward again after a relog; a refused store gives nothing and leaves the level to claim.
        session.Exp.PrestigeRewardsClaimed.Add(level);
        if (!session.TrySavePurchase(db => db.SavePrestigeClaims(session.Player))) {
            session.Exp.PrestigeRewardsClaimed.Remove(level);
            Logger.Warning("Prestige rank reward {Level} claim not stored, nothing given CharacterId={CharacterId}", level, session.CharacterId);
            return;
        }
        session.Send(PrestigePacket.ClaimReward(level));
        if (!session.Item.Inventory.Add(item, true, commit: true)) {
            session.Item.MailItem(item);
        }
    }

    private void HandleMissionReward(GameSession session, IByteReader packet) {
        int missionId = packet.ReadInt();
        if (!session.ServerTableMetadata.FeatureTable.IsOn(MissionFeature)) {
            return;
        }

        PrestigeMission? mission = session.Exp.PrestigeMissions.FirstOrDefault(m => m.Id == missionId);
        if (mission == null) {
            return;
        }

        if (!session.TableMetadata.PrestigeMissionTable.Entries.TryGetValue(missionId, out PrestigeMissionMetadata? metadata)) {
            return;
        }

        // The client sends the claim only once the mission's gained levels reached its count.
        if (!PrestigeRules.CanClaimMission(mission.Awarded, mission.GainedLevels, metadata.Count)) {
            return;
        }

        Item? item = session.Field?.ItemDrop.CreateItem(metadata.Item.ItemId, metadata.Item.Rarity, metadata.Item.Amount);
        if (item == null) {
            return;
        }

        // KMS2 order: the missions, then the item; the claim is stored first, as for the rank reward.
        mission.Awarded = true;
        if (!session.TrySavePurchase(db => db.SavePrestigeClaims(session.Player))) {
            mission.Awarded = false;
            Logger.Warning("Prestige mission {MissionId} claim not stored, nothing given CharacterId={CharacterId}", missionId, session.CharacterId);
            return;
        }
        session.Send(PrestigePacket.UpdateMissions(session.Player.Value.Account));
        if (!session.Item.Inventory.Add(item, true, commit: true)) {
            session.Item.MailItem(item);
        }
    }
}
