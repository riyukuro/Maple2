using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Game.Dungeon;

public class MiniGameUserRecord : IUserContentRecord {
    public long CharacterId { get; init; }
    public Dictionary<DungeonRewardType, int> Rewards { get; init; } = [];
    public ICollection<RewardItem> RewardItems { get; init; } = [];
    public int ClearedRounds { get; set; }
    public required int MinRound { get; init; }
    public required int TotalRounds { get; init; }
    public required bool ShowResult { get; init; }
    // The game_name of the start_mini_game that made the record.
    public string GameName { get; init; } = string.Empty;
    // The game's MiniGameGiveReward has run for this player: one reward and one clear per game.
    public bool RewardGiven { get; set; }

    public MiniGameUserRecord(long characterId) {
        CharacterId = characterId;
        var enumList = new List<DungeonRewardType>((DungeonRewardType[]) System.Enum.GetValues(typeof(DungeonRewardType)));
        // The guild raid lines (4, 5) belong to a guild raid clear only.
        foreach (DungeonRewardType type in enumList.Where(type => type < DungeonRewardType.GuildFund)) {
            Rewards[type] = 0;
        }
    }

    public void Add(RewardRecord record) {
        Rewards[DungeonRewardType.Exp] += (int) record.Exp;
        Rewards[DungeonRewardType.Meso] += (int) record.Meso;
        Rewards[DungeonRewardType.Prestige] += (int) record.PrestigeExp;

        foreach (RewardItem item in record.Items) {
            RewardItems.Add(item);
        }
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(ClearedRounds);
        writer.WriteInt(TotalRounds);

        writer.WriteInt(Rewards.Count);
        foreach ((DungeonRewardType type, int value) in Rewards) {
            writer.Write<DungeonRewardType>(type);
            writer.WriteInt(value);
        }

        writer.WriteInt(RewardItems.Count);
        foreach (RewardItem item in RewardItems) {
            // DungeonRewardInfo::Read (commonfiles/commonstruct_wa.cpp:337): item id, count, then rank.
            writer.WriteInt(item.ItemId);
            writer.WriteInt(item.Amount);
            writer.WriteInt(item.Rarity);
            writer.WriteBool(item.Unknown1);
            writer.WriteBool(item.Unknown2);
            writer.WriteBool(item.Unknown3);
        }
    }
}
