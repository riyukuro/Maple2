using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class ChatStickerPacket {
    private enum Command : byte {
        Load = 0,
        ExpiredNotify = 1,
        Add = 2,
        Use = 3,
        GroupChat = 4,
        Favorite = 5,
        Unfavorite = 6,
        Error = 7,
    }

    public static ByteWriter Load(ICollection<int> favorites, ICollection<ChatSticker> stickers) {
        var pWriter = Packet.Of(SendOp.ChatStamp);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteShort((short) favorites.Count);
        foreach (int favorite in favorites) {
            pWriter.WriteInt(favorite);
        }
        pWriter.WriteShort((short) stickers.Count);
        foreach (ChatSticker sticker in stickers) {
            pWriter.Write<ChatSticker>(sticker);
        }

        return pWriter;
    }

    public static ByteWriter Add(Item item, ChatSticker sticker) {
        var pWriter = Packet.Of(SendOp.ChatStamp);
        pWriter.Write<Command>(Command.Add);
        // CChatEmoticonCli::FUN_140a39360 (chatemoticoncli.cpp:202): item id, then a count of sticker groups and one {id, expiry} per group.
        pWriter.WriteInt(item.Id);
        pWriter.WriteInt(1);
        pWriter.Write<ChatSticker>(sticker);

        return pWriter;
    }

    // CChatEmoticonCli::OnEmoticonTypedInMessenger: sticker id, messenger window name, chat type.
    public static ByteWriter Use(int stickerId, string windowName, byte chatType) {
        var pWriter = Packet.Of(SendOp.ChatStamp);
        pWriter.Write<Command>(Command.Use);
        pWriter.WriteInt(stickerId);
        pWriter.WriteUnicodeString(windowName);
        pWriter.WriteByte(chatType);

        return pWriter;
    }

    public static ByteWriter GroupChat(int stickerId, string groupChatName) {
        var pWriter = Packet.Of(SendOp.ChatStamp);
        pWriter.Write<Command>(Command.GroupChat);
        pWriter.WriteInt(stickerId);
        pWriter.WriteUnicodeString(groupChatName);

        return pWriter;
    }

    public static ByteWriter Favorite(int stickerId) {
        var pWriter = Packet.Of(SendOp.ChatStamp);
        pWriter.Write<Command>(Command.Favorite);
        pWriter.WriteInt(stickerId);

        return pWriter;
    }

    public static ByteWriter Unfavorite(int stickerId) {
        var pWriter = Packet.Of(SendOp.ChatStamp);
        pWriter.Write<Command>(Command.Unfavorite);
        pWriter.WriteInt(stickerId);

        return pWriter;
    }

    public static ByteWriter Error(ChatStickerError error) {
        var pWriter = Packet.Of(SendOp.ChatStamp);
        pWriter.Write<Command>(Command.Error);
        pWriter.Write<ChatStickerError>(error);

        return pWriter;
    }
}
