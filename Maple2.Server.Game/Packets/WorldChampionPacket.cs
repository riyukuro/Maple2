using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class WorldChampionPacket {
    public const byte SeasonMode = 8;
    public const byte NextSeasonMode = 10;
    public const byte NoSeasonMode = 11;

    // Mode 8 (UIWorldChampionRewardDialog::OnSeasonRewardPacket, uiworldchampionrewarddialog.cpp:253-267): the season
    // id, its begin and end (unix seconds), the step count, the step that is out, the field, and the reward content
    // row of each step (the client asserts the count is the step count). A step past the count shows the season cleared.
    public static ByteWriter Season(WorldChampionTable.Season season, int step) {
        var pWriter = Packet.Of(SendOp.WorldChampion);
        pWriter.WriteByte(SeasonMode);
        pWriter.WriteInt(season.Id);
        pWriter.WriteLong(new DateTimeOffset(season.Begin).ToUnixTimeSeconds());
        pWriter.WriteLong(new DateTimeOffset(season.End).ToUnixTimeSeconds());
        pWriter.WriteByte((byte) season.Steps.Count);
        pWriter.WriteByte((byte) Math.Clamp(step, 0, byte.MaxValue));
        pWriter.WriteInt(season.MapId);
        pWriter.WriteInt(season.Steps.Count);
        foreach (WorldChampionTable.Step row in season.Steps) {
            pWriter.WriteInt(row.RewardId);
        }

        return pWriter;
    }

    // Mode 10 (OnNextSeasonPacket, :270-274): the next season's begin, unix seconds, shown with
    // s_world_champion_msg_next_season.
    public static ByteWriter NextSeason(DateTime begin) {
        var pWriter = Packet.Of(SendOp.WorldChampion);
        pWriter.WriteByte(NextSeasonMode);
        pWriter.WriteLong(new DateTimeOffset(begin).ToUnixTimeSeconds());

        return pWriter;
    }

    // Mode 11 has no body: the client shows s_world_champion_msg_not_found_season
    // (uiworldchampionrewarddialog.cpp:248).
    public static ByteWriter NoSeason() {
        var pWriter = Packet.Of(SendOp.WorldChampion);
        pWriter.WriteByte(NoSeasonMode);

        return pWriter;
    }
}
