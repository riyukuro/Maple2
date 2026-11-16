using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.GroupChat;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Containers;

public class GroupChatManager : IDisposable {
    public required ChannelClientLookup ChannelClients { get; init; }
    public readonly GroupChat GroupChat;

    public GroupChatManager(GroupChat groupChat) {
        GroupChat = groupChat;
    }

    public void Dispose() {
        Broadcast(new GroupChatRequest {
            Disband = new GroupChatRequest.Types.Disband { },
        });
    }

    public void Broadcast(GroupChatRequest request) {
        if (request.GroupChatId > 0 && request.GroupChatId != GroupChat.Id) {
            throw new InvalidOperationException($"Broadcasting {request.GroupChatCase} for incorrect group chat: {request.GroupChatId} => {GroupChat.Id}");
        }

        request.GroupChatId = GroupChat.Id;
        foreach (IGrouping<short, GroupChatMember> group in GroupChat.Members.Values.GroupBy(member => member.Info.Channel)) {
            if (!ChannelClients.TryGetClient(group.Key, out ChannelClient? client)) {
                continue;
            }

            request.ReceiverIds.Clear();
            request.ReceiverIds.AddRange(group.Select(member => member.Info.CharacterId));

            try {
                client.GroupChat(request);
            } catch { }
        }
    }

    // A chat ends when nobody is left in it or nobody in it is online. One member alone keeps it: the client makes a
    // chat with no one invited (CGroupChatManager::Create), keeps a chat a member left with one name in it
    // (OnPacketRemoveMember, groupchatmanager.cpp:672) and closes a chat only on GroupChat 4 (OnPacketRemoveChat). A
    // chat ended at one member left that member with an open window and a leave that did nothing.
    public bool Ended => GroupChat.Members.IsEmpty || !GroupChat.Members.Values.Any(member => member.Info.Online);

    public GroupChatError Invite(long requestorId, PlayerInfo info) {
        if (!GroupChat.Members.TryGetValue(requestorId, out GroupChatMember? requester)) {
            return GroupChatError.s_err_groupchat_null_target_user;
        }
        if (GroupChat.Members.ContainsKey(info.CharacterId)) {
            return GroupChatError.s_err_groupchat_add_member_target;
        }
        if (!ChannelClients.TryGetClient(info.Channel, out ChannelClient? client)) {
            return GroupChatError.s_err_groupchat_add_member_target;
        }

        GroupChatMember member = new GroupChatMember {
            Info = info.Clone(),
        };
        GroupChat.Members.TryAdd(info.CharacterId, member);

        Broadcast(new GroupChatRequest {
            AddMember = new GroupChatRequest.Types.AddMember {
                CharacterId = info.CharacterId,
                RequesterName = requester.Name,
                RequesterId = requester.CharacterId,
            },
            GroupChatId = GroupChat.Id,
        });

        return GroupChatError.none;
    }

    public bool Create(PlayerInfo info) {
        var member = new GroupChatMember {
            Info = info.Clone(),
        };

        GroupChat.Members.TryAdd(info.CharacterId, member);
        return true;
    }

    // The member leaves; the leaver gets the removal too, which closes its window (GroupChat 4). True when the chat
    // ended with it (Ended), for the caller to drop it.
    public bool Leave(long characterId, bool refused = false) {
        if (!GroupChat.Members.TryGetValue(characterId, out GroupChatMember? member)) {
            Log.Error("Failed to remove member {CharacterId} from group chat {GroupChatId} because they were not found", characterId, GroupChat.Id);
            return false;
        }

        Broadcast(new GroupChatRequest {
            RemoveMember = new GroupChatRequest.Types.RemoveMember {
                CharacterId = member.CharacterId,
                Refused = refused,
            },
        });
        GroupChat.Members.TryRemove(member.CharacterId, out _);

        return Ended;
    }
}
