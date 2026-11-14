using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Dungeon;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class DungeonRoomPacket {
    private enum Command : byte {
        Load = 5,
        Update = 6,
        Modify = 7,
        DungeonResult = 10, // OnClosingPhoto (dungeonrecordcli.cpp:299); 11 only opens a dialog (:306)
        Error = 18,
        RankRewards = 19, // the whole map
        RankReward = 20, // one reward (OnAddReward, dungeonrecordcli.cpp:451)
        BoostState = 22, // OnBoostState (dungeonrecordcli.cpp:459)
        UnitedRewardReset = 23, // the search and reward windows' own handlers; the record manager skips it
    }

    public static ByteWriter Load(IDictionary<int, DungeonRecord> records) {
        var pWriter = Packet.Of(SendOp.RoomDungeon);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteInt(records.Count);
        foreach ((int dungeonId, DungeonRecord record) in records) {
            pWriter.WriteInt(dungeonId);
            pWriter.WriteClass<DungeonRecord>(record);
        }

        return pWriter;
    }

    public static ByteWriter Update(DungeonRecord record) {
        var pWriter = Packet.Of(SendOp.RoomDungeon);
        pWriter.Write<Command>(Command.Update);
        pWriter.WriteClass<DungeonRecord>(record);

        return pWriter;
    }

    public static ByteWriter Modify(DungeonRoomModify modifyType, int dungeonId = 0) {
        var pWriter = Packet.Of(SendOp.RoomDungeon);
        pWriter.Write<Command>(Command.Modify);
        pWriter.Write<DungeonRoomModify>(modifyType);
        pWriter.WriteInt(dungeonId);

        return pWriter;
    }

    public static ByteWriter DungeonResult(DungeonState result, Dictionary<long, DungeonUserResult> statistics) {
        var pWriter = Packet.Of(SendOp.RoomDungeon);
        pWriter.Write<Command>(Command.DungeonResult);
        pWriter.Write<DungeonState>(result);
        pWriter.WriteInt(statistics.Count);
        foreach (DungeonUserResult userResult in statistics.Values) {
            pWriter.WriteLong(userResult.CharacterId);
            pWriter.Write<DungeonMissionRank>(userResult.MissionRank);
            pWriter.Write<DungeonAccumulationRecordType>(userResult.RecordType);
            pWriter.WriteInt(userResult.Value);
        }

        return pWriter;
    }

    public static ByteWriter Error(DungeonRoomError error, int arg = 0) {
        var pWriter = Packet.Of(SendOp.RoomDungeon);
        pWriter.Write<Command>(Command.Error);
        pWriter.Write<DungeonRoomError>(error);
        pWriter.WriteInt(arg);

        return pWriter;
    }

    // The boost of the account's united reward reset (DungeonRecordManagerCli::OnBoostState): u64 expiry, i64 character
    // id. The client applies the rates (GvgPacket.DungeonRewardRates) only for that character until the expiry
    // (IsBoostActive, dungeonrecordcli.cpp:528); 0, 0 for none.
    public static ByteWriter BoostState(long expiry, long characterId) {
        var pWriter = Packet.Of(SendOp.RoomDungeon);
        pWriter.Write<Command>(Command.BoostState);
        pWriter.WriteLong(expiry);
        pWriter.WriteLong(characterId);

        return pWriter;
    }

    // The united weekly count was reset: no body. The open dungeon search window (UIDungeonSearchDialog::
    // OnUnitedRewardReset, uidungeonsearchdialog.cpp:625) or reward popup (uidungeonpopuprewarddialog.cpp:97) redraws
    // the united reward from record 1006 and shows s_dungeonSearch_success_united_reward_reset.
    public static ByteWriter UnitedRewardReset() {
        var pWriter = Packet.Of(SendOp.RoomDungeon);
        pWriter.Write<Command>(Command.UnitedRewardReset);

        return pWriter;
    }

    public static ByteWriter RankRewards(IDictionary<int, DungeonRankReward> rankRewards) {
        var pWriter = Packet.Of(SendOp.RoomDungeon);
        pWriter.Write<Command>(Command.RankRewards);
        pWriter.WriteInt(rankRewards.Count);
        foreach ((int id, DungeonRankReward reward) in rankRewards) {
            pWriter.WriteInt(id);
            pWriter.WriteClass<DungeonRankReward>(reward);
        }

        return pWriter;
    }

    // OnAddReward reads the reward alone (id, count, expiry) and keys it by its id.
    public static ByteWriter RankReward(DungeonRankReward reward) {
        var pWriter = Packet.Of(SendOp.RoomDungeon);
        pWriter.Write<Command>(Command.RankReward);
        pWriter.WriteClass<DungeonRankReward>(reward);

        return pWriter;
    }
}
