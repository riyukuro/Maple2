using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

public static class ChatUtil {
    public static void SendEnchantAnnouncement(GameSession session, Item item) {
        if (session.ServerTableMetadata.ConstantsTable.enchantSuccessBroadcastingLevel > item.Enchant?.Enchants) {
            return;
        }

        // Saving the item to ensure the latest changes can be seen when broadcasted
        GameStorage.Request db = session.GameStorage.Context();
        db.SaveItems(session.CharacterId, item);

        const int unknownMode = 3;
        string message = string.Join(",", unknownMode, item.Uid, (int) AnnounceType.s_itemenchant_success_notice, session.PlayerName);

        ChatResponse? chatResponse = session.World.Chat(new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { item.Uid },
            // KMS2 item announcements carry the announcer's channel, from any field.
            Channel = GameServer.GetChannel(),
            SystemNotice = new ChatRequest.Types.SystemNotice(),
        });
    }

    // The client's item announcement line (FUN_140d2ab10): "3,<item uid>,<kind>,<args>". For kind 3 the args are
    // "<player name>:<box item id>" (exactly two fields), shown as s_msg_item_open_item_announce_to_world
    // "{0} has earned {2} from {1}." with the box's name and a link to the item.
    public static string ItemBoxAnnouncement(long itemUid, string playerName, int boxItemId) {
        const int unknownMode = 3;
        return string.Join(",", unknownMode, itemUid, (int) AnnounceType.s_msg_item_open_item_announce_to_world, $"{playerName}:{boxItemId}");
    }

    public static void SendItemBoxAnnouncement(GameSession session, Item item, int boxItemId) {
        // Saved first so the channels load the item as it is now for the link.
        using GameStorage.Request db = session.GameStorage.Context();
        db.SaveItems(session.CharacterId, item);

        // The box is already opened; a world server that cannot be reached only costs the announcement.
        try {
            session.World.Chat(new ChatRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                Name = session.PlayerName,
                Message = ItemBoxAnnouncement(item.Uid, session.PlayerName, boxItemId),
                ItemUids = { item.Uid },
                Channel = GameServer.GetChannel(),
                SystemNotice = new ChatRequest.Types.SystemNotice(),
            });
        } catch (RpcException) { /* ignored */ }
    }
}
