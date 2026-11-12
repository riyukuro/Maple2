using System.Text.RegularExpressions;
using System.Xml;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class UserChatHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.UserChat;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    public required GameStorage GameStorage { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        var type = packet.Read<ChatType>();
        string message = packet.ReadUnicodeString();
        string recipient = packet.ReadUnicodeString();
        long clubId = packet.ReadLong();
        ICollection<long> itemUids = LinkedItemUids(session, message);

        // Handle ChatCommands
        if (message.StartsWith('/')) {
            session.CommandHandler.Invoke(message[1..]);
            return;
        }

        if (ChatRestriction.LimitsLength(type) && ChatRestriction.TooLong(Constants, message)) {
            return;
        }
        if (ChatRestriction.Covers(type)) {
            ChatRestriction.Result result = session.ChatRestriction.Check(Constants, message, Environment.TickCount64);
            if (!result.Allowed) {
                session.Send(NoticePacket.Message(new InterfaceText(result.Notice, result.Seconds.ToString())));
                return;
            }
        }

        switch (type) {
            case ChatType.Normal:
                HandleNormal(session, message, itemUids);
                break;
            case ChatType.WhisperTo:
                HandleWhisper(session, message, recipient, itemUids);
                break;
            case ChatType.WhisperReject:
                // Sent by the client itself, not said by the player.
                HandleWhisperReject(session, recipient);
                return;
            case ChatType.Party:
                HandleParty(session, message, itemUids);
                break;
            case ChatType.Guild:
                HandleGuild(session, message, itemUids);
                break;
            case ChatType.GuildNotice:
                HandleGuild(session, message, itemUids, notice: true);
                break;
            case ChatType.World:
                HandleWorld(session, message, itemUids);
                break;
            case ChatType.Channel:
                HandleChannel(session, message, itemUids);
                break;
            case ChatType.Super:
                HandleSuper(session, message, itemUids);
                break;
            case ChatType.Club:
                HandleClub(session, message, clubId, itemUids);
                break;
            case ChatType.Wedding:
                HandleWedding(session, message, itemUids);
                break;
            case ChatType.UgcEvent:
                // The home OX quiz line (kind 22) is the server's announcement of the event (texttohtml.cpp:575,
                // joinUGCEvent link); the client never sends it, so one from a client goes nowhere.
                return;
            case ChatType.MicroGame:
                // KMS2 gives the line back to the field with the same type and channel 0.
                session.Field.Broadcast(ChatPacket.Message(session.Player.Value, ChatType.MicroGame, message));
                break;
        }

        session.ConditionUpdate(ConditionType.chat, targetLong: session.Field.MapId);
    }

    // The channel int of a player's own line (normal, party, guild, whisper): KMS2 sends the sender's channel from a
    // field that is no instance and 0 from an instanced one (home, dungeon, instanced map).
    internal static int LineChannel(GameSession session) {
        return session.Field is { FieldInstance.Type: InstanceType.none } ? GameServer.GetChannel() : 0;
    }

    private static void HandleNormal(GameSession session, string message, ICollection<long> itemUids) {
        if (itemUids.Count > 0) {
            List<Item> items = [];
            using GameStorage.Request db = session.GameStorage.Context();
            foreach (long itemUid in itemUids) {
                Item? item = db.GetItem(itemUid);
                if (item != null) {
                    items.Add(item);
                }
            }
            if (items.Count > 0) {
                session.Field?.Broadcast(MessengerBrowserPacket.Link(items.ToArray()));
            }
        }

        session.Field?.Broadcast(ChatPacket.Message(session.Player.Value, ChatType.Normal, message, LineChannel(session)));
    }

    private void HandleWhisper(GameSession session, string message, string recipient, ICollection<long> itemUids) {
        if (recipient == session.PlayerName) {
            session.Send(ChatPacket.Alert(StringCode.s_whisper_err_myself));
            return;
        }

        using GameStorage.Request db = GameStorage.Context();
        long characterId = db.GetCharacterId(recipient);
        if (!session.PlayerInfo.GetOrFetch(characterId, out PlayerInfo? info)) {
            session.Send(ChatPacket.Alert(StringCode.s_whisper_err_target));
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Gm = AdminLevel.IsGm(session.Player.AdminPermissions),
            Picture = session.Player.Value.Character.Picture,
            Channel = LineChannel(session),
            Whisper = new ChatRequest.Types.Whisper { RecipientId = info.CharacterId, RecipientName = recipient },
        };

        try {
            World.Chat(request);
            // The sender's own copy names the recipient; KMS2 sends account and character id 0 in it.
            session.Send(ChatPacket.Whisper(0, 0, info.Name, request.Message));
        } catch (RpcException ex) {
            switch (ex.StatusCode) {
                case StatusCode.NotFound:
                    session.Send(ChatPacket.Alert(StringCode.s_err_cannot_find_user));
                    return;
                case StatusCode.PermissionDenied:
                    session.Send(ChatPacket.WhisperReject(info.AccountId, info.CharacterId, info.Name, 0));
                    return;
                case StatusCode.Unavailable:
                    return;
            }
        }
    }

    // Chat kind 6: the client's whisper block option answers a received whisper, naming its sender. Only a player
    // whose whisper reached this session is sent the notice, on whatever channel it is now.
    private void HandleWhisperReject(GameSession session, string recipient) {
        if (!session.WhisperedBy.TryRemove(recipient, out long characterId)) {
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Channel = LineChannel(session),
            Whisper = new ChatRequest.Types.Whisper { RecipientId = characterId, RecipientName = recipient, Reject = true },
        };

        try {
            World.Chat(request);
        } catch (RpcException) { }
    }

    private void HandleParty(GameSession session, string message, ICollection<long> itemUids) {
        if (session.Party.Party == null) {
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Gm = AdminLevel.IsGm(session.Player.AdminPermissions),
            Channel = LineChannel(session),
            Party = new ChatRequest.Types.Party {
                PartyId = session.Party.Id,
            },
        };

        try {
            World.Chat(request);
        } catch (RpcException) { }
    }

    // notice: the guild megaphone (kind 18). The client sends it only with the rank permission 0x800 (SendAlert) and
    // otherwise prints string 0x415; KMS2 gives the line to the guild with channel 0.
    private void HandleGuild(GameSession session, string message, ICollection<long> itemUids, bool notice = false) {
        if (session.Guild.Guild == null) {
            return;
        }
        if (notice && !session.Guild.HasPermission(session.CharacterId, GuildPermission.SendAlert)) {
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Gm = AdminLevel.IsGm(session.Player.AdminPermissions),
            Channel = notice ? 0 : LineChannel(session),
            Guild = new ChatRequest.Types.Guild {
                GuildId = session.Guild.Id,
                Notice = notice,
            },
        };

        try {
            World.Chat(request);
        } catch (RpcException) { }
    }

    private void HandleWorld(GameSession session, string message, ICollection<long> itemUids) {
        var voucher = session.Item.Inventory.Filter(item => item.Metadata.Property.Tag == ItemTag.FreeWorldChatCoupon).FirstOrDefault();
        if (voucher != null) {
            if (!session.Item.Inventory.Consume(voucher.Uid, 1)) {
                session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_err_invalid_item));
                return;
            }
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_worldchat_use_coupon));
        } else {
            int meretCost = Constants.MeratConsumeWorldChat;
            if (session.FindEvent(GameEventType.SaleChat).FirstOrDefault()?.Metadata.Data is SaleChat gameEvent) {
                meretCost -= (int) (meretCost * Convert.ToSingle(gameEvent.WorldChatDiscount) / 10000);
            }

            if (session.Currency.Meret < meretCost) {
                session.Send(ChatPacket.Alert(StringCode.s_err_lack_merat));
                return;
            }
            session.Currency.Meret -= meretCost;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Gm = AdminLevel.IsGm(session.Player.AdminPermissions),
            Channel = GameServer.GetChannel(),
            World = new ChatRequest.Types.World(),
        };

        try {
            World.Chat(request);
        } catch (RpcException) { }
    }

    private void HandleChannel(GameSession session, string message, ICollection<long> itemUids) {
        var voucher = session.Item.Inventory.Filter(item => item.Metadata.Property.Tag == ItemTag.FreeChannelChatCoupon).FirstOrDefault();
        if (voucher != null) {
            if (!session.Item.Inventory.Consume(voucher.Uid, 1)) {
                session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_err_invalid_item));
                return;
            }
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_channelchat_use_coupon));
        } else {
            int meretCost = Constants.MeratConsumeChannelChat;
            if (session.FindEvent(GameEventType.SaleChat).FirstOrDefault()?.Metadata.Data is SaleChat gameEvent) {
                meretCost -= (int) (meretCost * Convert.ToSingle(gameEvent.ChannelChatDiscount) / 10000);
            }

            if (session.Currency.Meret < meretCost) {
                session.Send(ChatPacket.Alert(StringCode.s_err_lack_merat));
                return;
            }
            session.Currency.Meret -= meretCost;
        }

        // The linked items go first, as in the other lines; channel chat sent none, so its item links showed nothing.
        if (itemUids.Count > 0) {
            using GameStorage.Request db = session.GameStorage.Context();
            Item[] items = itemUids.Select(db.GetItem).OfType<Item>().ToArray();
            if (items.Length > 0) {
                session.ChannelBroadcast(MessengerBrowserPacket.Link(items));
            }
        }
        session.ChannelBroadcast(ChatPacket.Message(session.AccountId, session.CharacterId, session.PlayerName, ChatType.Channel, message, gm: AdminLevel.IsGm(session.Player.AdminPermissions), channel: GameServer.GetChannel()));
    }

    // The megaphone item the player selected (SuperChat Select), if it is still in the inventory with the function
    // of the line's kind. Otherwise the selection is dropped and the player told so.
    private static Item? SelectedMegaphone(GameSession session, ItemFunction function) {
        if (session.SuperChatId == 0) {
            return null;
        }

        Item? item = session.Item.Inventory.Find(session.SuperChatItemId).FirstOrDefault();
        if (item?.Metadata.Function?.Type == function) {
            return item;
        }
        Deselect(session);
        session.Send(ChatPacket.Alert(item == null ? StringCode.s_err_lack_super_coupon : StringCode.s_err_invalid_item));
        return null;
    }

    private static void Deselect(GameSession session) {
        session.SuperChatId = 0;
        session.SuperChatItemId = 0;
        session.Send(SuperChatPacket.Deselect(session.Player.ObjectId));
    }

    // A super chat line takes the selected SuperWorldChat item; the client sends a selected wedding megaphone as
    // kind 25, so kind 16 with one is refused.
    private void HandleSuper(GameSession session, string message, ICollection<long> itemUids) {
        Item? superChatItem = SelectedMegaphone(session, ItemFunction.SuperWorldChat);
        if (superChatItem == null) {
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Gm = AdminLevel.IsGm(session.Player.AdminPermissions),
            Channel = GameServer.GetChannel(),
            Super = new ChatRequest.Types.Super { ItemId = session.SuperChatId },
        };

        if (session.Item.Inventory.Consume(superChatItem.Uid, 1)) {
            try {
                Deselect(session);
                World.Chat(request);
            } catch (RpcException) { }
        }

    }

    private void HandleClub(GameSession session, string message, long clubId, ICollection<long> itemUids) {
        if (!session.Clubs.ContainsKey(clubId)) {
            session.Send(ChatPacket.Alert(StringCode.s_err_cannot_find_club));
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Gm = AdminLevel.IsGm(session.Player.AdminPermissions),
            Club = new ChatRequest.Types.Club { ClubId = clubId },
        };

        try {
            World.Chat(request);
        } catch (RpcException) { }
    }

    // Chat kind 25, the wedding megaphone: the client sends it from the super chat tab with a selected WeddingChat
    // item (UIMessenger SendChat, uimessenger.cpp:4219). The line goes to every channel like super chat and carries
    // the sender's reservation, so readers can follow its link to the ceremony. Without a reserved hall, or once the
    // hall is over, the item stays and the sender gets the client's text.
    private void HandleWedding(GameSession session, string message, ICollection<long> itemUids) {
        Item? item = SelectedMegaphone(session, ItemFunction.WeddingChat);
        if (item == null) {
            return;
        }

        Marriage marriage = session.Marriage.Marriage;
        WeddingHall hall = session.Marriage.WeddingHall;
        StringCode? error = WeddingInviteRules.WeddingChatError(hall.Id, marriage.Status, hall.CeremonyTime,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Model.WeddingCeremony.EnterWindowSeconds);
        if (error != null) {
            session.Send(ChatPacket.Alert(error.Value));
            return;
        }

        var request = new ChatRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = session.PlayerName,
            Message = message,
            ItemUids = { itemUids },
            Gm = AdminLevel.IsGm(session.Player.AdminPermissions),
            Channel = GameServer.GetChannel(),
            Wedding = new ChatRequest.Types.Wedding {
                ItemId = session.SuperChatId,
                MarriageId = marriage.Id,
                PackageId = hall.PackageId,
                HallId = hall.PackageHallId,
                CeremonyTime = hall.CeremonyTime,
                // The hall's fixed roles, as in the invitation mail: the session's partners are relative to it.
                Groom = hall.ReserverName,
                Bride = hall.PartnerName,
            },
        };

        if (session.Item.Inventory.Consume(item.Uid, 1)) {
            try {
                Deselect(session);
                World.Chat(request);
            } catch (RpcException) { }
        }
    }

    private static ICollection<long> LinkedItemUids(GameSession session, string message) {
        List<long> itemUids = [];

        MatchCollection matches = Regex.Matches(message, "<A HREF=\"event:[^\"]+\">[^<]+</A>");
        string[] links = matches.Select(match => match.Value).ToArray();
        foreach (string link in links) {
            string linkHtml = System.Net.WebUtility.HtmlDecode(link);
            XmlDocument xml = new();
            xml.LoadXml(linkHtml);

            // Select the 'A' node and get the 'HREF' attribute
            XmlNode? aNode = xml.SelectSingleNode("//A");

            string? hrefValue = aNode?.Attributes?["HREF"]?.Value;
            if (hrefValue == null) {
                continue;
            }

            // Remove "event:" and split by comma
            string eventValue = hrefValue.Substring("event:".Length);
            string[] parameters = eventValue.Split(',');

            switch (parameters[0]) {
                case "itemTooltip":
                    if (!long.TryParse(parameters[1], out long itemUid)) {
                        continue;
                    }
                    // Can only link items player owns.
                    Item? item = session.Item.Inventory.Get(itemUid) ?? session.Item.Equips.Get(itemUid);
                    if (item != null) {
                        itemUids.Add(item.Uid);
                    }
                    break;
                case "achieveTooltip":
                case "billDialog":
                case "searchFromMeratmarket":
                    break;
            }
        }
        return itemUids;
    }
}
