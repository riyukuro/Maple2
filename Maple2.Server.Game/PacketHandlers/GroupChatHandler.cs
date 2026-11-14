using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Game.PacketHandlers;

public class GroupChatHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.GroupChat;

    private enum Command : byte {
        Create = 1,
        Invite = 2,
        Leave = 4,
        Chat = 10,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Create:
                HandleCreate(session, packet);
                break;
            case Command.Invite:
                HandleInvite(session, packet);
                break;
            case Command.Leave:
                HandleLeave(session, packet);
                break;
            case Command.Chat:
                HandleChat(session, packet);
                break;
        }
    }

    private void HandleCreate(GameSession session, IByteReader packet) {
        // CGroupChatManager::Create: the player to invite into the new chat, empty for an empty chat.
        string targetPlayerName = packet.ReadUnicodeString();
        if (!GroupChatRules.NameValid(targetPlayerName)) {
            return;
        }

        // CGroupChatManager::Create: at most 3 chats per player (GroupChatMaxCapacity is the member limit).
        if (session.GroupChats.Count >= Constant.GroupChatMaxCount) {
            session.Send(GroupChatPacket.Error(GroupChatError.s_err_groupchat_maxgroup, session.PlayerName, session.PlayerName));
            return;
        }

        // Check the invited player before the chat exists, so a failed invite leaves no empty chat behind.
        long targetCharacterId = 0;
        if (!string.IsNullOrEmpty(targetPlayerName) && !CheckInviteTarget(session, targetPlayerName, out targetCharacterId)) {
            return;
        }

        // An unreachable World would end the session (Session.StartInternal): log it and answer as a failed add.
        GroupChatResponse response;
        try {
            response = World.GroupChat(new GroupChatRequest {
                Create = new GroupChatRequest.Types.Create(),
                RequesterId = session.CharacterId,
            });
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to create a group chat for {CharacterId}", session.CharacterId);
            session.Send(GroupChatPacket.Error(GroupChatError.s_err_groupchat_add_member_target, session.PlayerName, targetPlayerName));
            return;
        }

        var error = (GroupChatError) response.Error;
        if (error != GroupChatError.none) {
            session.Send(GroupChatPacket.Error(error, session.PlayerName, string.Empty));
            return;
        }

        if (!session.GroupChats.TryAdd(response.GroupChat.Id, new GroupChatManager(response.GroupChat, session))) {
            throw new InvalidOperationException($"Failed to add group chat: {response.GroupChat.Id}");
        }

        session.Send(GroupChatPacket.Create(response.GroupChat.Id));
        if (targetCharacterId != 0) {
            Invite(session, targetPlayerName, targetCharacterId, response.GroupChat.Id);
        }
    }

    private void HandleInvite(GameSession session, IByteReader packet) {
        (string targetPlayerName, int groupChatId) = GroupChatRules.ReadInvite(packet);
        // The client invites only into a chat it is in, and sends no name above 24 letters (groupchatmanager.cpp:506-535).
        if (!GroupChatRules.NameValid(targetPlayerName) || !session.GroupChats.ContainsKey(groupChatId)) {
            return;
        }
        if (CheckInviteTarget(session, targetPlayerName, out long characterId)) {
            Invite(session, targetPlayerName, characterId, groupChatId);
        }
    }

    // The invited player exists and has room for one more chat. Sends the error and returns false otherwise.
    private bool CheckInviteTarget(GameSession session, string targetPlayerName, out long characterId) {
        using (GameStorage.Request db = session.GameStorage.Context()) {
            characterId = db.GetCharacterId(targetPlayerName);
        }
        if (characterId == 0) {
            session.Send(GroupChatPacket.Error(GroupChatError.s_err_groupchat_null_target_user, session.PlayerName, targetPlayerName));
            return false;
        }

        try {
            GroupChatInfoResponse infoResponse = World.GroupChatInfo(new GroupChatInfoRequest {
                CharacterId = characterId,
            });
            if (infoResponse.Infos.Count >= Constant.GroupChatMaxCount) {
                session.Send(GroupChatPacket.Error(GroupChatError.s_err_groupchat_maxgroup, session.PlayerName, targetPlayerName));
                return false;
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to look up the group chats of {Name}", targetPlayerName);
            session.Send(GroupChatPacket.Error(GroupChatError.s_err_groupchat_add_member_target, session.PlayerName, targetPlayerName));
            return false;
        }
        return true;
    }

    private void Invite(GameSession session, string targetPlayerName, long characterId, int groupChatId) {
        try {
            session.Send(GroupChatPacket.Invite(session.PlayerName, targetPlayerName, groupChatId));
            var request = new GroupChatRequest {
                Invite = new GroupChatRequest.Types.Invite {
                    ReceiverId = characterId,
                },
                GroupChatId = groupChatId,
                RequesterId = session.CharacterId,
            };

            GroupChatResponse response = World.GroupChat(request);
            var error = (GroupChatError) response.Error;
            if (error != GroupChatError.none) {
                session.Send(GroupChatPacket.Error(error, session.PlayerName, targetPlayerName));
            }
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to invite {Name} to group chat", targetPlayerName);
            session.Send(GroupChatPacket.Error(GroupChatError.s_err_groupchat_add_member_target, session.PlayerName, targetPlayerName));
        }
    }

    private void HandleLeave(GameSession session, IByteReader packet) {
        (int groupChatId, bool refused) = GroupChatRules.ReadLeave(packet); // 1 when the client's refuse-invites option turned down an invite, 0 when the player leaves

        if (!session.GroupChats.ContainsKey(groupChatId)) {
            return;
        }

        try {
            World.GroupChat(new GroupChatRequest {
                Leave = new GroupChatRequest.Types.Leave { Refused = refused },
                GroupChatId = groupChatId,
                RequesterId = session.CharacterId,
            });
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to leave group chat {GroupChatId}", groupChatId);
        }
    }

    private void HandleChat(GameSession session, IByteReader packet) {
        (string message, int groupChatId) = GroupChatRules.ReadChat(packet);

        // The client sends no text above 2048 letters (groupchatmanager.cpp:557-559).
        if (!GroupChatRules.TextValid(message) || !session.GroupChats.ContainsKey(groupChatId)) {
            return;
        }
        // CGroupChatManager::SendGroupChatMode10 checks the chat restriction before it sends.
        if (UserChatHandler.RefuseBlockedChat(session)) {
            return;
        }

        try {
            World.GroupChat(new GroupChatRequest {
                Chat = new GroupChatRequest.Types.Chat {
                    Message = message,
                    // Only items the sender holds, as for other chat lines.
                    ItemUids = { UserChatHandler.LinkedItemUids(session, message) },
                },
                GroupChatId = groupChatId,
                RequesterId = session.CharacterId,
            });
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to send to group chat {GroupChatId}", groupChatId);
        }
    }
}
