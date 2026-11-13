using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

public class Guild : IByteSerializable {
    public byte Capacity = Validators.GuildRules.DefaultCapacity;

    public required long Id { get; init; }
    public required string Name;
    public required long LeaderAccountId;
    public required long LeaderCharacterId;
    public required string LeaderName;

    public string Emblem = string.Empty;
    public string Notice = string.Empty;
    public long CreationTime;
    public AchievementInfo AchievementInfo;
    public GuildFocus Focus;
    // CGuildInfo +0x154: the guild is listed in the guild search. A new CGuildInfo starts with 1.
    public bool Listed = true;
    public int Experience;
    public int Funds;
    public int HouseRank;
    public int HouseTheme;
    // The guild championship: the record (World-owned; the channels hold a copy) and the queue state of now
    // (CGuildInfo m_pvpState: 0 none, 1 registered, 2 matched), which is not stored.
    public GuildPvp Pvp = new();
    public GuildPvpState PvpState;

    public readonly ConcurrentDictionary<long, GuildMember> Members;
    public required IList<GuildRank> Ranks { get; init; }
    public required IList<GuildBuff> Buffs { get; init; }
    public required IList<GuildEvent> Events { get; init; }

    // The time the event was opened, 0 when it never was. Under the list's lock, as its writers are.
    public int EventOpenTime(int eventId) {
        lock (Events) {
            return Events.FirstOrDefault(entry => entry.Id == eventId).Value;
        }
    }

    // Sets the open time of an event the guild lists. False when it lists no such event.
    public bool SetEvent(GuildEvent opened) {
        lock (Events) {
            for (int i = 0; i < Events.Count; i++) {
                if (Events[i].Id == opened.Id) {
                    Events[i] = opened;
                    return true;
                }
            }
        }
        return false;
    }
    public required IList<GuildPoster> Posters { get; init; }
    public required IList<GuildNpc> Npcs { get; init; }
    // The guild storage: what guild-fund shop products put in, what the master gives out (Guild 109). One row per
    // item id and rarity, written under the list's lock (the World's guild lock, the channel's gRPC thread).
    public required IList<RewardItem> Storage { get; init; }
    // The gift log (Guild 112), oldest first; the client keeps 50 rows.
    public required IList<GuildStorageLog> StorageLog { get; init; }

    public int StoredAmount(int itemId, short rarity) {
        lock (Storage) {
            return Storage.FirstOrDefault(entry => entry.ItemId == itemId && entry.Rarity == rarity).Amount;
        }
    }

    // Sets the stored amount of an item; 0 removes its row.
    public void SetStored(int itemId, short rarity, int amount) {
        lock (Storage) {
            for (int i = 0; i < Storage.Count; i++) {
                if (Storage[i].ItemId != itemId || Storage[i].Rarity != rarity) {
                    continue;
                }
                if (amount > 0) {
                    Storage[i] = new RewardItem(itemId, rarity, amount);
                } else {
                    Storage.RemoveAt(i);
                }
                return;
            }
            if (amount > 0) {
                Storage.Add(new RewardItem(itemId, rarity, amount));
            }
        }
    }

    public RewardItem[] StorageSnapshot() {
        lock (Storage) {
            return Storage.ToArray();
        }
    }

    public void AddStorageLog(GuildStorageLog entry) {
        lock (StorageLog) {
            StorageLog.Add(entry);
            while (StorageLog.Count > Validators.GuildRules.StorageLogCapacity) {
                StorageLog.RemoveAt(0);
            }
        }
    }

    public GuildStorageLog[] StorageLogSnapshot() {
        lock (StorageLog) {
            return StorageLog.ToArray();
        }
    }

    [SetsRequiredMembers]
    public Guild(long id, string name, long leaderAccountId, long leaderCharacterId, string leaderName) {
        Id = id;
        Name = name;
        LeaderAccountId = leaderAccountId;
        LeaderCharacterId = leaderCharacterId;
        LeaderName = leaderName;

        Members = new ConcurrentDictionary<long, GuildMember>();
        Ranks = new List<GuildRank>();
        Buffs = new List<GuildBuff>();
        Events = new List<GuildEvent>();
        Posters = new List<GuildPoster>();
        Npcs = new List<GuildNpc>();
        Storage = new List<RewardItem>();
        StorageLog = new List<GuildStorageLog>();
    }

    [SetsRequiredMembers]
    public Guild(long id, string name, GuildMember leader) : this(id, name, leader.AccountId, leader.CharacterId, leader.Name) { }

    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(Id);
        writer.WriteUnicodeString(Name);
        writer.WriteUnicodeString(Emblem);
        writer.WriteByte(Capacity);
        writer.WriteUnicodeString();
        writer.WriteUnicodeString(Notice);
        writer.WriteLong(LeaderAccountId);
        writer.WriteLong(LeaderCharacterId);
        writer.WriteUnicodeString(LeaderName);
        writer.WriteLong(CreationTime);
        // The championship (CGuildInfo::SetPvpState, then the score, wins and losses the guild page shows).
        GuildPvp pvp = Pvp;
        writer.WriteByte((byte) PvpState);
        writer.WriteInt(pvp.Score);
        writer.WriteInt(pvp.Wins);
        writer.WriteInt(pvp.Losses);
        writer.WriteInt();
        writer.WriteInt();
        writer.WriteInt();
        writer.WriteInt();
        writer.WriteInt();
        writer.WriteBool(Listed);
        writer.WriteInt((int) Focus);
        writer.WriteInt(Experience);
        writer.WriteInt(Funds);
        writer.WriteInt(); // KMS 2525 CGuildInfo::Read (0x1404cdda0): event fund, read after the fund
        writer.WriteBool(false);
        writer.WriteInt();

        writer.WriteByte((byte) Members.Count);
        foreach (GuildMember member in Members.Values) {
            writer.WriteClass<GuildMember>(member);
        }
        writer.WriteByte((byte) Ranks.Count);
        foreach (GuildRank rank in Ranks) {
            writer.WriteClass<GuildRank>(rank);
        }
        writer.WriteByte((byte) Buffs.Count);
        foreach (GuildBuff buff in Buffs) {
            writer.Write<GuildBuff>(buff);
        }
        // The list is written by the channel's gRPC thread (Guild 56) while a session thread reads it.
        lock (Events) {
            // The client reads the count as a byte: no more rows than the count says.
            int eventCount = Math.Min(Events.Count, byte.MaxValue);
            writer.WriteByte((byte) eventCount);
            for (int i = 0; i < eventCount; i++) {
                writer.Write<GuildEvent>(Events[i]);
            }
        }

        writer.WriteInt(HouseRank);
        writer.WriteInt(HouseTheme);

        writer.WriteInt(Posters.Count);
        foreach (GuildPoster poster in Posters) {
            writer.WriteClass<GuildPoster>(poster);
        }
        writer.WriteByte((byte) Npcs.Count);
        foreach (GuildNpc npc in Npcs) {
            writer.Write<GuildNpc>(npc);
        }

        // CGuildInfo::ReadNpcShop: a flag, then the shop list (count as a short). KMS2 sends the flag set with no shop.
        writer.WriteBool(true);
        writer.WriteShort();

        RewardItem[] storage = StorageSnapshot();
        writer.WriteInt(storage.Length);
        foreach (RewardItem item in storage) {
            WriteStorageItem(writer, item);
        }
        // The client's guild reader ends with the storage list; nothing follows it.
    }

    // The client's item entry (Guild 0 storage list, Guild 110, Guild 112): id, rarity, amount, 4 flags.
    public static void WriteStorageItem(IByteWriter writer, RewardItem item) {
        writer.WriteInt(item.ItemId);
        writer.WriteShort(item.Rarity);
        writer.WriteInt(item.Amount);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
    }
}

// One gift: from the master to a member (accounts and characters as the log sends them), the item and the time.
public record GuildStorageLog(long GiverAccountId, long GiverCharacterId, string GiverName, long ReceiverAccountId,
    long ReceiverCharacterId, string ReceiverName, int ItemId, short Rarity, int Amount, long Time);

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
public readonly record struct GuildBuff(int Id, int Level, long ExpiryTime);

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
public readonly record struct GuildEvent(int Id, int Value);

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
public readonly record struct GuildNpc(GuildNpcType Type, int Level);
