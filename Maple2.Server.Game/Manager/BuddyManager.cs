using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util.Sync;
using Maple2.Tools.Extensions;
using Serilog;
using static Maple2.Model.Error.BuddyError;

namespace Maple2.Server.Game.Manager;

public class BuddyManager : IDisposable {
    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    // The packet handlers (the session's receive task), the World's buddy notifications (gRPC threads) and the
    // buddy status listeners change and read these at once.
    private readonly ConcurrentDictionary<long, Buddy> buddies;
    private readonly ConcurrentDictionary<long, Buddy> blocked;

    // The "refuse friend requests" game option as the client last reported it (Buddy sub-type 20).
    public bool AutoReject { get; set; }

    private readonly CancellationTokenSource tokenSource;
    private readonly ILogger logger = Log.Logger.ForContext<BuddyManager>();

    public BuddyManager(GameStorage.Request db, GameSession session) {
        this.session = session;
        IEnumerable<IGrouping<bool, BuddyEntry>> groups =
            db.ListBuddies(session.CharacterId).GroupBy(entry => entry.Type == BuddyType.Blocked);
        foreach (IGrouping<bool, BuddyEntry> group in groups) {
            var result = new ConcurrentDictionary<long, Buddy>(group
                .Select(entry => session.PlayerInfo.GetOrFetch(entry.BuddyId, out PlayerInfo? info) ? new Buddy(entry, info) : null)
                .WhereNotNull()
                .Select(entry => KeyValuePair.Create(entry.Id, entry)));
            if (group.Key) {
                blocked = result;
            } else {
                buddies = result;
            }
        }

        // Set to empty dictionary if uninitialized by db.
        buddies ??= new ConcurrentDictionary<long, Buddy>();
        blocked ??= new ConcurrentDictionary<long, Buddy>();
        tokenSource = new CancellationTokenSource();

        foreach (Buddy buddy in buddies.Values) {
            BeginListen(buddy);
        }
    }

    public void Dispose() {
        tokenSource.Cancel();
        tokenSource.Dispose();

        foreach (Buddy buddy in buddies.Values) {
            buddy.Dispose();
        }
        foreach (Buddy buddy in blocked.Values) {
            buddy.Dispose();
        }
    }

    public bool IsBuddy(long characterId) {
        Buddy? buddy = buddies.Values.FirstOrDefault(entry => entry.Info.CharacterId == characterId);
        return buddy != null && buddy.Type == BuddyType.Default;
    }

    // The block list is keyed by the entry id; callers ask by the other character's id.
    public bool IsBlocked(long characterId) {
        return blocked.Values.Any(entry => entry.Info.CharacterId == characterId);
    }

    // The count that ends the list (Buddy 19): KMS2 leaves out the blocked entries and the requests the player sent.
    public static int ListCount(IEnumerable<BuddyType> types) {
        return types.Count(type => type is not (BuddyType.OutRequest or BuddyType.Blocked));
    }

    public void Load() {
        session.Send(BuddyPacket.StartList());
        if (buddies.Count + blocked.Count > 0) {
            session.Send(BuddyPacket.Load(buddies.Values, blocked.Values));
        }
        session.Send(BuddyPacket.EndList(ListCount(buddies.Values.Select(buddy => buddy.Type))));
        // The option lives in the client's own settings; ask for it once per session.
        session.Send(BuddyPacket.AskAutoReject());

        // What the player's friends did while it was offline; after the list, as "approved" marks the friend in it.
        using GameStorage.Request db = session.GameStorage.Context();
        IList<BuddyNotice> notices = db.TakeBuddyNotices(session.CharacterId);
        if (notices.Count > 0) {
            session.Send(BuddyPacket.Notices(notices));
        }
    }

    // Keeps the change as a notice for the other player's next login when the world found it offline.
    private void KeepNotice(GameStorage.Request db, BuddyResponse response, long receiverId, BuddyNoticeType type, long entryId) {
        if (Util.BuddyRules.Delivered(response)) {
            return;
        }
        if (!db.CreateBuddyNotice(receiverId, new BuddyNotice(session.PlayerName, type, entryId))) {
            logger.Warning("Failed to keep buddy notice {Type} for {ReceiverId}", type, receiverId);
        }
    }

    // Tells the other player's game server through World, after the change is stored. An unreachable World would end
    // the session (RpcException is no SystemException, Session.StartInternal), so it is logged and the change counts
    // as not delivered: an empty answer, so KeepNotice keeps it for the other player's next login.
    private BuddyResponse Notify(BuddyRequest request) {
        try {
            return session.World.Buddy(request);
        } catch (RpcException ex) {
            logger.Warning(ex, "Buddy {Case}: World could not be reached for {ReceiverId}", request.BuddyCase, request.ReceiverId);
            return new BuddyResponse();
        }
    }

    // The invite error for the receiver's entry of this character, ok when there is none.
    private BuddyError PairError(GameStorage.Request db, long receiverId) {
        return db.GetBuddyType(receiverId, session.CharacterId) switch {
            BuddyType.Default => s_buddy_err_already_buddy,
            BuddyType.InRequest => s_buddy_err_already_request_somebody,
            BuddyType.OutRequest => s_buddy_err_request_somebody,
            BuddyType.Blocked => s_buddy_refused_request_from_somebody,
            _ => ok,
        };
    }

    public void SendInvite(string name, string message) {
        if (name.Length > Constant.CharacterNameLengthMax || message.Length > Constant.BuddyMessageLengthMax) {
            session.Send(BuddyPacket.Invite(error: s_buddy_err_unknown));
            return;
        }
        // Names are looked up without case (GetCharacterId; the client's FindByName uses CompareNoCase).
        if (SameName(name, session.PlayerName)) {
            session.Send(BuddyPacket.Invite(error: s_buddy_err_my_id_ex));
            return;
        }
        if (buddies.Count >= Constants.MaxBuddyCount) {
            session.Send(BuddyPacket.Invite(error: s_buddy_err_max_buddy));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long receiverId = db.GetCharacterId(name);
        if (receiverId == default) {
            session.Send(BuddyPacket.Invite(error: s_buddy_err_miss_id));
            return;
        }
        if (receiverId == session.CharacterId) {
            session.Send(BuddyPacket.Invite(error: s_buddy_err_my_id_ex));
            return;
        }

        // The player's own block entry for the receiver: a request next to it would give two entries for one character.
        if (IsBlocked(receiverId)) {
            session.Send(BuddyPacket.Invite(name, error: s_buddy_err_request_somebody));
            return;
        }

        BuddyError error = PairError(db, receiverId);
        if (error != ok) {
            session.Send(BuddyPacket.Invite(name, error: error));
            return;
        }

        try {
            db.BeginTransaction();
            if (db.CountBuddy(receiverId) >= Constants.MaxBuddyCount) {
                session.Send(BuddyPacket.Invite(name: name, error: s_buddy_err_target_full));
                return;
            }
            // Both rows or neither: the receiver's row failing used to be ignored, and the sender's request was kept
            // with no entry on the other side (an accept could never pair it).
            BuddyEntry? entry = db.CreateBuddy(session.CharacterId, receiverId, BuddyType.OutRequest, message);
            if (entry == null || db.CreateBuddy(receiverId, session.CharacterId, BuddyType.InRequest, message) == null) {
                db.Rollback();
                // The pair is kept once per side (unique index): an invite of the receiver that crossed this one and
                // was stored first refuses these rows, and is answered as the check above answers it.
                BuddyError crossed = PairError(db, receiverId);
                session.Send(crossed == ok ? BuddyPacket.Invite(error: s_buddy_err_unknown) : BuddyPacket.Invite(name, error: crossed));
                return;
            }
            db.Commit();

            if (!session.PlayerInfo.GetOrFetch(entry.BuddyId, out PlayerInfo? info)) {
                session.Send(BuddyPacket.Invite(error: s_buddy_err_miss_id));
                return;
            }

            var self = new Buddy(entry, info);
            buddies[self.Id] = self;

            session.Send(BuddyPacket.Invite(name, message));
            session.Send(BuddyPacket.Append(self));

            BuddyResponse _ = Notify(new BuddyRequest {
                ReceiverId = receiverId,
                Invite = new BuddyRequest.Types.Invite { SenderId = session.CharacterId },
            });
            session.ConditionUpdate(ConditionType.buddy_request);
        } catch (SystemException ex) {
            logger.Warning(ex, "Invite failed");
            session.Send(BuddyPacket.Invite(error: s_buddy_err_unknown));
        }
    }

    public void ReceiveInvite(long senderId) {
        using GameStorage.Request db = session.GameStorage.Context();
        BuddyEntry? entry = db.GetBuddy(session.CharacterId, senderId);
        if (entry == null || !session.PlayerInfo.GetOrFetch(entry.BuddyId, out PlayerInfo? info)) {
            logger.Debug("Could not find sender:{SenderId} for ReceiveInvite", senderId);
            return;
        }

        var self = new Buddy(entry, info);
        // DB written by sender as single transaction.
        buddies[self.Id] = self;
        if (!Util.BuddyRules.ShowsRequest(AutoReject, self.Type)) {
            // The request is not shown to this client. A client that gets it with the option on sends its own
            // decline for the same entry id (the first long of the entry), which then found the entry gone.
            SendDecline(self.Id);
            return;
        }
        session.Send(BuddyPacket.Append(self));
    }

    public void SendAccept(long entryId) {
        if (!buddies.TryGetValue(entryId, out Buddy? self) || self.Type != BuddyType.InRequest) {
            logger.Debug("Could not find entry:{EntryId} to Accept", entryId);
            session.Send(BuddyPacket.Accept(entryId, s_buddy_err_miss_id));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long receiverId = self.Info.CharacterId;
        BuddyEntry? other = db.GetBuddy(receiverId, session.CharacterId);
        if (other == null) {
            logger.Warning("Accept without paired entry");
            session.Send(BuddyPacket.Accept(entryId, s_buddy_err_miss_id));
            return;
        }

        // The friend limit of both lists (the client's errors 7 and 8, uibuddyeventlistener.cpp:1876-1905); the
        // request stays for a later accept.
        if (buddies.Values.Count(buddy => buddy.Type == BuddyType.Default) >= Constants.MaxBuddyCount) {
            session.Send(BuddyPacket.Accept(entryId, s_buddy_err_max_buddy));
            return;
        }
        if (db.CountFriends(receiverId) >= Constants.MaxBuddyCount) {
            session.Send(BuddyPacket.Accept(entryId, s_buddy_err_target_full));
            return;
        }

        self.SetType(BuddyType.Default);
        other.Type = BuddyType.Default;

        bool stored;
        try {
            stored = db.UpdateBuddy(self, other);
        } catch (SystemException ex) {
            logger.Warning(ex, "Buddy Accept failed");
            stored = false;
        }
        if (!stored) {
            logger.Error("Failed to update buddy");
            self.SetType(BuddyType.InRequest);
            session.Send(BuddyPacket.Accept(entryId, s_buddy_err_unknown));
            return;
        }

        // Both entries are friends in storage now, so the client is told so even if the other side cannot be reached
        // (it sees the friend at its next list load).
        try {
            BuddyResponse response = Notify(new BuddyRequest {
                ReceiverId = receiverId,
                Accept = new BuddyRequest.Types.Accept { EntryId = other.Id },
            });
            KeepNotice(db, response, receiverId, BuddyNoticeType.Approved, other.Id);
            if (session.PlayerInfo.GetOrFetch(self.Info.CharacterId, out PlayerInfo? info)) {
                self.Info.Update(UpdateField.All, info);
            }
        } catch (Exception ex) { // RpcException when World is down
            logger.Warning(ex, "Buddy Accept: the other side was not told");
        }
        BeginListen(self);

        session.Send(BuddyPacket.Accept(self));
        session.Send(BuddyPacket.UpdateInfo(self));
        if (self.Info.Online) {
            session.Send(BuddyPacket.NotifyOnline(self));
        }
    }

    public void ReceiveAccept(long entryId) {
        if (!buddies.TryGetValue(entryId, out Buddy? self)) {
            logger.Debug("Could not find entry:{EntryId} for ReceiveAccept", entryId);
            return;
        }

        // DB written by sender as single transaction.
        self.SetType(BuddyType.Default);
        if (session.PlayerInfo.GetOrFetch(self.Info.CharacterId, out PlayerInfo? info)) {
            self.Info.Update(UpdateField.All, info);
        }

        BeginListen(self);

        session.Send(BuddyPacket.UpdateInfo(self));
        session.Send(BuddyPacket.NotifyAccept(self));
        session.Send(BuddyPacket.NotifyOnline(self));
    }

    public void SendDecline(long entryId) {
        if (!buddies.TryGetValue(entryId, out Buddy? self) || self.Type != BuddyType.InRequest) {
            logger.Debug("Could not find entry:{EntryId} to Decline", entryId);
            session.Send(BuddyPacket.Decline(entryId, s_buddy_err_miss_id));
            return;
        }

        try {
            using GameStorage.Request db = session.GameStorage.Context();
            long receiverId = self.Info.CharacterId;
            BuddyEntry? other = db.GetBuddy(receiverId, session.CharacterId);
            if (other == null) {
                logger.Warning("Decline without paired entry");
                session.Send(BuddyPacket.Decline(entryId, s_buddy_err_miss_id));
                return;
            }

            if (!db.RemoveBuddy(self, other)) {
                session.Send(BuddyPacket.Decline(entryId, s_buddy_err_unknown));
                return;
            }
            buddies.TryRemove(entryId, out _);

            session.Send(BuddyPacket.Decline(self));
            BuddyResponse response = Notify(new BuddyRequest {
                ReceiverId = receiverId,
                Decline = new BuddyRequest.Types.Decline { EntryId = other.Id },
            });
            KeepNotice(db, response, receiverId, BuddyNoticeType.Declined, other.Id);
        } catch (SystemException ex) {
            logger.Warning(ex, "Decline failed");
            session.Send(BuddyPacket.Decline(entryId, s_buddy_err_unknown));
        }
    }

    public void ReceiveDecline(long entryId) {
        // DB written by sender as single transaction.
        if (!buddies.TryRemove(entryId, out Buddy? self)) {
            logger.Debug("Could not find entry:{EntryId} for ReceiveDecline", entryId);
            return;
        }

        session.Send(BuddyPacket.Remove(self));
    }

    public void SendBlock(long entryId, string name, string message) {
        if (name.Length > Constant.CharacterNameLengthMax || message.Length > Constant.BuddyMessageLengthMax) {
            session.Send(BuddyPacket.Block(error: s_buddy_err_unknown));
            return;
        }
        if (blocked.Count >= Constants.MaxBlockCount) {
            session.Send(BuddyPacket.Block(name: name, error: s_buddy_err_max_block));
            return;
        }
        if (entryId == 0) {
            // entryId is 0 when blocking by |name|, in any case (names are looked up without case).
            if (SameName(name, session.PlayerName)) {
                session.Send(BuddyPacket.Block(name: name, error: s_buddy_err_my_id_ex));
                return;
            }
            // We still search for entryId to convert the existing buddy entry.
            foreach (Buddy value in buddies.Values) {
                if (SameName(value.Info.Name, name)) {
                    entryId = value.Id;
                }
            }
            // Already blocked: the client only asks for players it has not blocked, so a repeat is dropped.
            if (blocked.Values.Any(value => SameName(value.Info.Name, name))) {
                logger.Information("{Name} is already blocked", name);
                return;
            }
        } else {
            if (blocked.TryGetValue(entryId, out _)) {
                logger.Information("Could not find entry:{EntryId} to Block", entryId);
                return;
            }
        }

        try {
            long receiverId = 0;
            Buddy? self = null;
            if (buddies.TryGetValue(entryId, out Buddy? existing)) {
                receiverId = existing.Info.CharacterId;
                // The row is written first: the entry in memory changes only once it is kept.
                BuddyEntry row = existing;
                row.Type = BuddyType.Blocked;
                row.Message = message;
                // Its own context: a failed update stays queued in the context it ran in.
                using GameStorage.Request updateDb = session.GameStorage.Context();
                if (updateDb.UpdateBuddy(row)) {
                    buddies.TryRemove(entryId, out _);
                    EndListen(existing);
                    existing.SetType(BuddyType.Blocked);
                    existing.Message = message;
                    self = existing;
                    blocked[self.Id] = self;
                    session.Send(BuddyPacket.Block(self.Id, self.Info.Name, self.Message));
                    session.Send(BuddyPacket.UpdateInfo(self));
                } else if (!RowGone(existing.Id)) {
                    // A write that failed on a row that is still there (or that could not be asked): the block is
                    // refused and the entry stays as it was, so a second (Blocked) row is never made beside it.
                    logger.Warning("Block: entry {EntryId} could not be updated; refused", existing.Id);
                    session.Send(BuddyPacket.Block(error: s_buddy_err_unknown));
                    return;
                } else {
                    // The entry had no row any more: a new block row is made below, as for a player not on the list.
                    buddies.TryRemove(entryId, out _);
                    EndListen(existing);
                    logger.Warning("Block: entry {EntryId} had no row; a new one is made", existing.Id);
                }
            }

            using GameStorage.Request db = session.GameStorage.Context();
            if (self == null) {
                if (receiverId == 0) {
                    receiverId = db.GetCharacterId(name);
                }
                if (receiverId == default) {
                    session.Send(BuddyPacket.Block(error: s_buddy_err_miss_id));
                    return;
                }
                if (receiverId == session.CharacterId) {
                    session.Send(BuddyPacket.Block(name: name, error: s_buddy_err_my_id_ex));
                    return;
                }
                if (blocked.Values.Any(value => value.Info.CharacterId == receiverId)) {
                    logger.Information("{Name} is already blocked", name);
                    return;
                }

                BuddyEntry? selfEntry = db.CreateBuddy(session.CharacterId, receiverId, BuddyType.Blocked, message);
                if (selfEntry == null || !session.PlayerInfo.GetOrFetch(selfEntry.BuddyId, out PlayerInfo? selfInfo)) {
                    session.Send(BuddyPacket.Block(error: s_buddy_err_unknown));
                    return;
                }

                self = new Buddy(selfEntry, selfInfo);
                blocked[self.Id] = self;
                session.Send(BuddyPacket.Block(self.Id, self.Info.Name, self.Message));
                session.Send(BuddyPacket.Append(self));
            }

            // Delete entry for existing buddy if it exists.
            BuddyEntry? other = db.GetBuddy(receiverId, session.CharacterId);
            if (other == null) {
                return;
            }

            if (other.Type != BuddyType.Blocked && db.RemoveBuddy(other)) {
                BuddyResponse _ = Notify(new BuddyRequest {
                    ReceiverId = receiverId,
                    Block = new BuddyRequest.Types.Block { SenderId = session.CharacterId },
                });
            }
        } catch (SystemException ex) {
            logger.Warning(ex, "Block failed");
            session.Send(BuddyPacket.Block(error: s_buddy_err_unknown));
        }
    }

    // Whether the entry's row is gone, asked in a context of its own (the update's context still tracks the entry).
    // A lookup that fails counts as a row that is there.
    private bool RowGone(long entryId) {
        try {
            using GameStorage.Request db = session.GameStorage.Context();
            return db.GetBuddy(entryId) == null;
        } catch (SystemException ex) {
            logger.Warning(ex, "Block: entry {EntryId} could not be looked up", entryId);
            return false;
        }
    }

    public void ReceiveBlock(long senderId) {
        if (!TryGet(senderId, out Buddy? buddy)) {
            logger.Debug("Could not find sender:{SenderId} for ReceiveBlock", senderId);
            return;
        }

        if (buddies.TryRemove(buddy.Id, out _)) {
            EndListen(buddy);

            session.Send(BuddyPacket.Remove(buddy));
        }
    }

    public void Unblock(long entryId) {
        if (!blocked.TryRemove(entryId, out Buddy? self)) {
            logger.Debug("Could not find entry:{EntryId} to Unblock", entryId);
            session.Send(BuddyPacket.Unblock(entryId, s_buddy_err_miss_id));
            return;
        }

        bool removed;
        try {
            using GameStorage.Request db = session.GameStorage.Context();
            removed = db.RemoveBuddy(self);
        } catch (SystemException ex) {
            logger.Warning(ex, "Unblock failed");
            removed = false;
        }
        if (!removed) {
            // Still blocked in storage: kept in memory too, and the client keeps its entry (it acts on result 0 only).
            blocked[entryId] = self;
            session.Send(BuddyPacket.Unblock(entryId, s_buddy_err_unknown));
            return;
        }

        session.Send(BuddyPacket.Unblock(self));
        session.Send(BuddyPacket.Remove(self));
    }

    public void SendDelete(long entryId) {
        if (!buddies.TryGetValue(entryId, out Buddy? self) || self.Type != BuddyType.Default) {
            logger.Debug("Could not find entry:{EntryId} to Delete", entryId);
            session.Send(BuddyPacket.Remove(entryId, s_buddy_err_miss_id));
            return;
        }

        try {
            using GameStorage.Request db = session.GameStorage.Context();
            long receiverId = self.Info.CharacterId;
            BuddyEntry? other = db.GetBuddy(receiverId, session.CharacterId);
            if (other == null) {
                logger.Warning("Delete without paired entry");
                session.Send(BuddyPacket.Remove(entryId, s_buddy_err_miss_id));
                return;
            }

            if (!db.RemoveBuddy(self, other)) {
                session.Send(BuddyPacket.Remove(entryId, s_buddy_err_unknown));
                return;
            }

            if (!buddies.TryRemove(entryId, out self)) {
                return;
            }
            EndListen(self);
            session.Send(BuddyPacket.Remove(self));
            BuddyResponse response = Notify(new BuddyRequest {
                ReceiverId = receiverId,
                Delete = new BuddyRequest.Types.Delete { EntryId = other.Id },
            });
            KeepNotice(db, response, receiverId, BuddyNoticeType.Removed, other.Id);
        } catch (SystemException ex) {
            logger.Warning(ex, "Delete failed");
            session.Send(BuddyPacket.Remove(entryId, s_buddy_err_unknown));
        }
    }

    public void ReceiveDelete(long entryId) {
        // DB written by sender as single transaction.
        if (!buddies.TryRemove(entryId, out Buddy? self)) {
            logger.Debug("Could not find entry:{EntryId} for ReceiveDelete", entryId);
            return;
        }

        EndListen(self);
        session.Send(BuddyPacket.Remove(self));
    }

    public void UpdateBlock(long entryId, string name, string message) {
        if (name.Length > Constant.CharacterNameLengthMax || message.Length > Constant.BuddyMessageLengthMax) {
            session.Send(BuddyPacket.UpdateBlock(error: s_buddy_err_unknown));
            return;
        }
        if (!blocked.TryGetValue(entryId, out Buddy? self)) {
            session.Send(BuddyPacket.UpdateBlock(error: s_buddy_err_unknown));
            return;
        }

        try {
            using GameStorage.Request db = session.GameStorage.Context();
            // The row is written first: a refused write keeps the old memo here and tells the client, which had no reply.
            string before = self.Message;
            self.Message = message;
            if (!db.UpdateBuddy(self)) {
                self.Message = before;
                session.Send(BuddyPacket.UpdateBlock(error: s_buddy_err_unknown));
                return;
            }

            session.Send(BuddyPacket.UpdateBlock(self.Id, self.Info.Name, self.Message));
        } catch (SystemException ex) {
            logger.Warning(ex, "UpdateBlock failed");
            session.Send(BuddyPacket.UpdateBlock(error: s_buddy_err_unknown));
        }
    }

    public void SendCancel(long entryId) {
        if (!buddies.TryGetValue(entryId, out Buddy? self) || self.Type != BuddyType.OutRequest) {
            logger.Debug("Could not find entry:{EntryId} to Cancel", entryId);
            session.Send(BuddyPacket.Cancel(entryId, s_buddy_err_miss_id));
            return;
        }

        try {
            using GameStorage.Request db = session.GameStorage.Context();
            long receiverId = self.Info.CharacterId;
            BuddyEntry? other = db.GetBuddy(receiverId, session.CharacterId);
            if (other == null) {
                logger.Warning("Cancel without paired entry");
                session.Send(BuddyPacket.Cancel(entryId, s_buddy_err_miss_id));
                return;
            }

            if (!db.RemoveBuddy(self, other)) {
                session.Send(BuddyPacket.Cancel(entryId, s_buddy_err_unknown));
                return;
            }

            // The rows are gone and the client drops its entry on result 0: the list drops it too, else a block by name
            // later took this dead entry (its row update failed unseen) and the unblock then found no row.
            buddies.TryRemove(entryId, out Buddy? _);
            EndListen(self);
            session.Send(BuddyPacket.Cancel(self));
            BuddyResponse _ = Notify(new BuddyRequest {
                ReceiverId = receiverId,
                Cancel = new BuddyRequest.Types.Cancel { EntryId = other.Id },
            });
        } catch (SystemException ex) {
            logger.Warning(ex, "Cancel failed");
            session.Send(BuddyPacket.Cancel(entryId, s_buddy_err_unknown));
        }
    }

    public void ReceiveCancel(long entryId) {
        // DB written by sender as single transaction.
        if (!buddies.TryRemove(entryId, out Buddy? self)) {
            logger.Debug("Could not find entry:{EntryId} for ReceiveCancel", entryId);
            return;
        }

        session.Send(BuddyPacket.Remove(self));
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private bool TryGet(long buddyId, [NotNullWhen(true)] out Buddy? result) {
        foreach (Buddy buddy in buddies.Values) {
            if (buddyId == buddy.Info.CharacterId) {
                result = buddy;
                return true;
            }
        }
        foreach (Buddy buddy in blocked.Values) {
            if (buddyId == buddy.Info.CharacterId) {
                result = buddy;
                return true;
            }
        }

        result = null;
        return false;
    }

    #region PlayerInfo Events
    private void BeginListen(Buddy buddy) {
        if (buddy.Type != BuddyType.Default) {
            return;
        }
        // Clean up previous token if necessary
        if (buddy.TokenSource != null) {
            logger.Warning("BeginListen called on Buddy {Id} that was already listening", buddy.Id);
            EndListen(buddy);
        }

        buddy.TokenSource = CancellationTokenSource.CreateLinkedTokenSource(tokenSource.Token);
        CancellationToken token = buddy.TokenSource.Token;
        var listener = new PlayerInfoListener(UpdateField.Buddy, (type, info) => SyncUpdate(token, buddy.Id, type, info));
        session.PlayerInfo.Listen(buddy.Info.CharacterId, listener);
    }

    private void EndListen(Buddy buddy) {
        buddy.TokenSource?.Cancel();
        buddy.TokenSource?.Dispose();
        buddy.TokenSource = null;
    }

    private bool SyncUpdate(CancellationToken cancel, long id, UpdateField type, IPlayerInfo info) {
        if (cancel.IsCancellationRequested || !buddies.TryGetValue(id, out Buddy? buddy)) {
            return true;
        }

        bool wasOnline = buddy.Info.Online;
        buddy.Info.Update(type, info);

        session.Send(BuddyPacket.UpdateInfo(buddy));
        if (buddy.Info.Online != wasOnline) {
            session.Send(BuddyPacket.NotifyOnline(buddy));
        }
        return false;
    }
    #endregion
}
