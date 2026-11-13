using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.GroupChat;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class GroupChatPacket {
    private enum Command : byte {
        Load = 0,
        Create = 1,
        Invite = 2,
        Join = 3,
        Leave = 4,
        AddMember = 6,
        RemoveMember = 7,
        LoginNotice = 8,
        LogoutNotice = 9,
        Chat = 10,
        ItemLinks = 11,
        MemberPicture = 12,
        Error = 13,
    }

    public static ByteWriter Load(GroupChat groupChat) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteInt(groupChat.Id);
        pWriter.WriteByte((byte) groupChat.Members.Count);
        foreach ((long characterId, GroupChatMember member) in groupChat.Members) {
            pWriter.WriteBool(true); // is member?
            pWriter.WriteClass<PlayerInfo>(member.Info);
        }

        return pWriter;
    }

    public static ByteWriter Create(int groupChatId) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.Create);
        pWriter.WriteInt(groupChatId);

        return pWriter;
    }

    public static ByteWriter Invite(string memberName, string targetName, int groupChatId) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.Invite);
        pWriter.WriteUnicodeString(memberName);
        pWriter.WriteUnicodeString(targetName);
        pWriter.WriteInt(groupChatId);

        return pWriter;
    }

    public static ByteWriter Join(string senderMemberName, string receiverTargetName, int groupChatId) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.Join);
        pWriter.WriteUnicodeString(senderMemberName);
        pWriter.WriteUnicodeString(receiverTargetName);
        pWriter.WriteInt(groupChatId);

        return pWriter;
    }

    public static ByteWriter Leave(int groupChatId) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.Leave);
        pWriter.WriteInt(groupChatId);

        return pWriter;
    }

    public static ByteWriter AddMember(PlayerInfo targetCharacter, string inviterName, int groupChatId) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.AddMember);
        pWriter.WriteInt(groupChatId);
        pWriter.WriteUnicodeString(inviterName);
        pWriter.WriteBool(true); // is member?
        pWriter.WriteClass<PlayerInfo>(targetCharacter);

        return pWriter;
    }

    // Client 2525 OnPacketRemoveMember reads the chat, a reason byte and the name; the group messenger shows "left"
    // for reason 0 and "declined the invitation" for reason 1 (uigroupmessenger.cpp:773-776).
    public static ByteWriter RemoveMember(string memberName, int groupChatId, bool refused = false) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.RemoveMember);
        pWriter.WriteInt(groupChatId);
        pWriter.WriteByte((byte) (refused ? 1 : 0));
        pWriter.WriteUnicodeString(memberName);

        return pWriter;
    }

    // Client 2525 OnPacketMemberPicture: the chat, then the member by name, gets the new profile picture.
    public static ByteWriter MemberPicture(int groupChatId, string memberName, string picture) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.MemberPicture);
        pWriter.WriteInt(groupChatId);
        pWriter.WriteUnicodeString(memberName);
        pWriter.WriteUnicodeString(picture);

        return pWriter;
    }

    public static ByteWriter LoginNotice(string memberName, int groupChatId) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.LoginNotice);
        pWriter.WriteInt(groupChatId);
        pWriter.WriteUnicodeString(memberName);

        return pWriter;
    }

    public static ByteWriter LogoutNotice(string memberName, int groupChatId) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.LogoutNotice);
        pWriter.WriteInt(groupChatId);
        pWriter.WriteUnicodeString(memberName);

        return pWriter;
    }

    public static ByteWriter Chat(string memberName, int groupChatId, string message) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.Chat);
        pWriter.WriteInt(groupChatId);
        pWriter.WriteUnicodeString(memberName);
        pWriter.WriteUnicodeString(message);

        return pWriter;
    }

    // CGroupChatManager 0xb: the chat and the sender, whom the client must hold as a member, then the linked
    // items as UIGroupMessengerBrowser::ReadItemLinks reads them (the same item block as MessengerBrowser).
    public static ByteWriter ItemLinks(int groupChatId, string memberName, ICollection<Item> items) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.ItemLinks);
        pWriter.WriteInt(groupChatId);
        pWriter.WriteUnicodeString(memberName);
        pWriter.WriteInt(items.Count);
        foreach (Item item in items) {
            pWriter.WriteLong(item.Uid);
            pWriter.WriteInt(item.Id);
            pWriter.WriteInt(item.Rarity);
            pWriter.WriteClass<Item>(item);
        }

        return pWriter;
    }

    public static ByteWriter Error(GroupChatError error, string memberName, string targetName) {
        var pWriter = Packet.Of(SendOp.GroupChat);
        pWriter.Write<Command>(Command.Error);
        // CGroupChatManager::OnPacketResult: u8 (not used), i32 code, two names. It shows a box for codes
        // 3, 8, 10 and 13 only, formatted with the second name.
        pWriter.WriteByte(2);
        pWriter.WriteInt((int) error);
        pWriter.WriteUnicodeString(memberName);
        pWriter.WriteUnicodeString(targetName);

        return pWriter;
    }
}
