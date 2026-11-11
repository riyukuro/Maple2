using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.GroupChat;
using Maple2.Model.Metadata;
using Serilog;

namespace Maple2.Server.World.Containers;

public class GroupChatLookup : IDisposable {
    private readonly ChannelClientLookup channelClients;
    private readonly PlayerInfoLookup playerLookup;

    private readonly ConcurrentDictionary<int, GroupChatManager> groupChats;
    private int nextGroupChatId;
    // Held around every membership check and add, so two requests at once cannot both pass the
    // per-player chat limit or the per-chat member limit (the client's are 3 and 20).
    private readonly object membershipLock = new();

    public GroupChatLookup(ChannelClientLookup channelClients, PlayerInfoLookup playerLookup) {
        this.channelClients = channelClients;
        this.playerLookup = playerLookup;

        groupChats = new ConcurrentDictionary<int, GroupChatManager>();
    }

    public void Dispose() {
        foreach (GroupChatManager manager in groupChats.Values) {
            manager.Dispose();
        }
    }

    public bool TryGet(int groupChatId, [NotNullWhen(true)] out GroupChatManager? groupChat) {
        return groupChats.TryGetValue(groupChatId, out groupChat);
    }

    public IEnumerable<GroupChatManager> TryGetMany(long characterId) {
        foreach (GroupChatManager manager in groupChats.Values) {
            if (manager.GroupChat.Members.TryGetValue(characterId, out GroupChatMember? member)) {
                yield return manager;
            }
        }
    }

    public GroupChatError Create(long requesterId, out int groupChatId) {
        groupChatId = Interlocked.Increment(ref nextGroupChatId);

        PlayerInfo? requesterInfo = playerLookup.GetPlayerInfo(requesterId);
        if (requesterInfo == null) {
            return GroupChatError.s_err_groupchat_null_target_user;
        }

        var groupChat = new GroupChat(groupChatId);
        var manager = new GroupChatManager(groupChat) {
            ChannelClients = channelClients,
        };

        lock (membershipLock) {
            // CGroupChatManager::Create refuses a fourth chat with s_err_groupchat_maxgroup.
            if (TryGetMany(requesterId).Count() >= Constant.GroupChatMaxCount) {
                return GroupChatError.s_err_groupchat_maxgroup;
            }
            if (!groupChats.TryAdd(groupChatId, manager)) {
                return GroupChatError.s_err_groupchat_null_target_user;
            }

            manager.Create(requesterInfo);
        }
        return GroupChatError.none;
    }

    // Checks and adds the invited player as one step. The client's result reader shows no box for a
    // full chat (it warns before sending), so a full chat is answered with s_err_groupchat_add_member_target.
    public GroupChatError Invite(GroupChatManager manager, long requesterId, PlayerInfo info) {
        lock (membershipLock) {
            if (!manager.GroupChat.Members.ContainsKey(info.CharacterId)
                    && TryGetMany(info.CharacterId).Count() >= Constant.GroupChatMaxCount) {
                return GroupChatError.s_err_groupchat_maxgroup;
            }
            if (manager.GroupChat.Members.Count >= Constant.GroupChatMaxCapacity) {
                return GroupChatError.s_err_groupchat_add_member_target;
            }
            return manager.Invite(requesterId, info);
        }
    }

    public void Disband(int groupChatId) {
        if (!TryGet(groupChatId, out GroupChatManager? manager)) {
            Log.Error("Failed to disband group chat {GroupChatId} because it was not found", groupChatId);
            return;
        }

        if (!groupChats.TryRemove(groupChatId, out manager)) {
            Log.Error("Unable to remove group chat {GroupChatId} in World", groupChatId);
            return;
        }
        manager.Dispose();
    }
}
