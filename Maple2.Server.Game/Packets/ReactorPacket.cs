using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// CSceneStateGame::OnReactor. A reactor is a field object placed with a CreateReactor item.
public static class ReactorPacket {
    private enum Command : byte {
        Add = 0,
        Remove = 1,
        UpdateState = 3,
        Reward = 4,
        StartReact = 5,
    }

    // CReactor::FUN_140b79490. The client names a reactor "Reactor_<cube id>" when it checks that a cube is free,
    // so name is that string. stateIndex is the index into the reactor's state list, not a state id. characterIds
    // are the connected players; the client reads but does not use stateTime and the last int.
    public static ByteWriter Add(int objectId, string name, int reactorId, Vector3 position, Vector3 rotation,
                                 int stateIndex, long stateTime, ICollection<long> characterIds, int unknown = 0) {
        var pWriter = Packet.Of(SendOp.Reactor);
        pWriter.Write<Command>(Command.Add);
        pWriter.WriteInt(objectId);
        pWriter.WriteString(name);
        pWriter.WriteInt(reactorId);
        pWriter.Write<Vector3>(position);
        pWriter.Write<Vector3>(rotation);
        pWriter.WriteState(stateIndex, stateTime, characterIds, unknown);

        return pWriter;
    }

    public static ByteWriter Remove(int objectId) {
        var pWriter = Packet.Of(SendOp.Reactor);
        pWriter.Write<Command>(Command.Remove);
        pWriter.WriteInt(objectId);

        return pWriter;
    }

    // CReactor::FUN_140b79a10: the reactor plays the new state's start animation, then its idle animation.
    public static ByteWriter UpdateState(int objectId, int stateIndex, long stateTime, ICollection<long> characterIds, int unknown = 0) {
        var pWriter = Packet.Of(SendOp.Reactor);
        pWriter.Write<Command>(Command.UpdateState);
        pWriter.WriteInt(objectId);
        pWriter.WriteState(stateIndex, stateTime, characterIds, unknown);

        return pWriter;
    }

    // The reward notice lists each item and amount.
    public static ByteWriter Reward(ICollection<ReactorTable.Reward> rewards) {
        var pWriter = Packet.Of(SendOp.Reactor);
        pWriter.Write<Command>(Command.Reward);
        pWriter.WriteInt(rewards.Count);
        foreach (ReactorTable.Reward reward in rewards) {
            pWriter.WriteInt(reward.ItemId);
            pWriter.WriteShort((short) reward.Amount);
        }

        return pWriter;
    }

    // Puts the receiving player into the react state on the reactor.
    public static ByteWriter StartReact(int objectId) {
        var pWriter = Packet.Of(SendOp.Reactor);
        pWriter.Write<Command>(Command.StartReact);
        pWriter.WriteInt(objectId);

        return pWriter;
    }

    private static void WriteState(this IByteWriter writer, int stateIndex, long stateTime, ICollection<long> characterIds, int unknown) {
        writer.WriteInt(stateIndex);
        writer.WriteLong(stateTime);
        writer.WriteByte((byte) characterIds.Count);
        foreach (long characterId in characterIds) {
            writer.WriteLong(characterId);
        }
        writer.WriteInt(unknown);
    }
}
