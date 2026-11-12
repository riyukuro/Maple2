using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class UserEnvPacket {
    private enum Command : byte {
        AddTitle = 0,
        UpdateTitles = 1,
        LoadTitles = 2,
        ItemCollects = 3,
        InteractedObjects = 4,
        GatheringCounts = 8,
        MasteryRewardsClaimed = 9,
        FamePoints = 10,
        FamePoint = 11,
        FameLogs = 12,
        FameLog = 13,
    }

    public static ByteWriter AddTitle(int titleId) {
        var pWriter = Packet.Of(SendOp.UserEnv);
        pWriter.Write<Command>(Command.AddTitle);
        pWriter.WriteInt(titleId);
        return pWriter;
    }

    public static ByteWriter UpdateTitle(int objectId, int titleId) {
        var pWriter = Packet.Of(SendOp.UserEnv);
        pWriter.Write<Command>(Command.UpdateTitles);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(titleId);
        return pWriter;
    }

    public static ByteWriter LoadTitles(ISet<int> titles) {
        var pWriter = Packet.Of(SendOp.UserEnv);
        pWriter.Write<Command>(Command.LoadTitles);
        pWriter.WriteInt(titles.Count);
        foreach (int title in titles) {
            pWriter.WriteInt(title);
        }

        return pWriter;
    }

    public static ByteWriter ItemCollects(IDictionary<int, byte> itemCollects) {
        var pWriter = Packet.Of(SendOp.UserEnv);
        pWriter.Write<Command>(Command.ItemCollects);
        pWriter.WriteInt(itemCollects.Count);
        foreach ((int itemId, byte quantity) in itemCollects) {
            pWriter.WriteInt(itemId);
            pWriter.WriteByte(quantity);
        }

        return pWriter;
    }

    public static ByteWriter InteractedObjects(ISet<int> interactedObjects) {
        var pWriter = Packet.Of(SendOp.UserEnv);
        pWriter.Write<Command>(Command.InteractedObjects);
        pWriter.WriteInt(interactedObjects.Count);
        foreach (int id in interactedObjects) {
            pWriter.WriteInt(id);
        }

        return pWriter;
    }

    public static ByteWriter GatheringCounts(IDictionary<int, int> gatheringCounts) {
        var pWriter = Packet.Of(SendOp.UserEnv);
        pWriter.Write<Command>(Command.GatheringCounts);
        pWriter.WriteInt(gatheringCounts.Count);
        foreach ((int recipeId, int count) in gatheringCounts) {
            pWriter.WriteInt(recipeId);
            pWriter.WriteInt(count);
        }

        return pWriter;
    }

    public static ByteWriter LoadClaimedRewards(IDictionary<int, bool> claimedRewards) {
        var pWriter = Packet.Of(SendOp.UserEnv);
        pWriter.Write<Command>(Command.MasteryRewardsClaimed);
        pWriter.WriteInt(claimedRewards.Count);
        foreach ((int rewardId, bool isClaimed) in claimedRewards) {
            pWriter.WriteInt(rewardId);
            pWriter.WriteBool(isClaimed);
        }

        return pWriter;
    }

    // The alliance fame points (CSceneStateGame::OnUserEnv modes 10 and 11): the client's GetFamePoints, by the
    // alliance id (AllianceType), which the alliance mission window shows. 10 replaces them all, 11 sets one.
    public static ByteWriter FamePoints(IDictionary<AllianceType, int> famePoints) {
        var pWriter = Packet.Of(SendOp.UserEnv);
        pWriter.Write<Command>(Command.FamePoints);
        pWriter.WriteInt(famePoints.Count);
        foreach ((AllianceType alliance, int points) in famePoints) {
            pWriter.WriteShort((short) alliance);
            pWriter.WriteInt(points);
        }

        return pWriter;
    }

    public static ByteWriter FamePoint(AllianceType alliance, int points) {
        var pWriter = Packet.Of(SendOp.UserEnv);
        pWriter.Write<Command>(Command.FamePoint);
        pWriter.WriteShort((short) alliance);
        pWriter.WriteInt(points);

        return pWriter;
    }

    // The fame log (CSceneStateGame::OnUserEnv modes 12 and 13): the fame board's history, each entry a log id of
    // table/famelog.xml and its time in epoch seconds. 12 replaces the list, 13 adds one entry and shows the
    // client's fame update notice.
    public static ByteWriter FameLogs(IDictionary<int, long> fameLogs) {
        var pWriter = Packet.Of(SendOp.UserEnv);
        pWriter.Write<Command>(Command.FameLogs);
        pWriter.WriteInt(fameLogs.Count);
        foreach ((int logId, long time) in fameLogs.OrderBy(entry => entry.Value).ThenBy(entry => entry.Key)) {
            pWriter.WriteInt(logId);
            pWriter.WriteLong(time);
        }

        return pWriter;
    }

    public static ByteWriter FameLog(int logId, long time) {
        var pWriter = Packet.Of(SendOp.UserEnv);
        pWriter.Write<Command>(Command.FameLog);
        pWriter.WriteInt(logId);
        pWriter.WriteLong(time);

        return pWriter;
    }
}
