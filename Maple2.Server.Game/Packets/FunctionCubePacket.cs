using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class FunctionCubePacket {
    private enum Command : byte {
        SendCubes = 2,
        Add = 3,
        Furniture = 6,
        SuccessLifeSkill = 9,
        FailLifeSkill = 10,
        Feed = 11,
        NurturingError = 13,
    }

    public static ByteWriter SendCubes(ICollection<FieldFunctionInteract> cubes) {
        var pWriter = Packet.Of(SendOp.FunctionCube);
        pWriter.Write<Command>(Command.SendCubes);
        pWriter.WriteInt(cubes.Count);
        foreach (FieldFunctionInteract cube in cubes) {
            pWriter.WriteClass<InteractCube>(cube.InteractCube);
            pWriter.WriteByte();
        }
        return pWriter;
    }

    private const byte StateAdd = 0;
    private const byte StateUpdate = 1;

    public static ByteWriter AddFunctionCube(InteractCube interactCube) =>
        WriteFunctionCube(interactCube, StateAdd);

    public static ByteWriter UpdateFunctionCube(InteractCube interactCube) =>
        WriteFunctionCube(interactCube, StateUpdate);

    private static ByteWriter WriteFunctionCube(InteractCube interactCube, byte state) {
        var pWriter = Packet.Of(SendOp.FunctionCube);
        pWriter.Write<Command>(Command.Add);
        pWriter.WriteClass<InteractCube>(interactCube);
        pWriter.WriteByte(state); // 0 Add, 1 Update
        return pWriter;
    }

    public static ByteWriter UseFurniture(long characterId, InteractCube interactCube) {
        var pWriter = Packet.Of(SendOp.FunctionCube);
        pWriter.Write<Command>(Command.Furniture);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(interactCube.Id);
        pWriter.WriteBool(interactCube.State is InteractCubeState.InUse);
        return pWriter;
    }

    public static ByteWriter SuccessLifeSkill(long characterId, InteractCube interactCube) {
        var pWriter = Packet.Of(SendOp.FunctionCube);
        pWriter.Write<Command>(Command.SuccessLifeSkill);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(interactCube.Id);
        pWriter.WriteLong(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        pWriter.WriteInt(1); // count the client adds to its per-cube success tally; one gather = one success
        return pWriter;
    }

    public static ByteWriter FailLifeSkill(long characterId, InteractCube interactCube) {
        var pWriter = Packet.Of(SendOp.FunctionCube);
        pWriter.Write<Command>(Command.FailLifeSkill);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(interactCube.Id);
        pWriter.WriteLong(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return pWriter;
    }

    // Client 2525 CFunctionCubeManager packet reader (functioncubeobject.cpp:2554) and MyPC::ReadDailyRecords
    // (mypc.cpp:4060): long character id (the packet is dropped unless it is the reader's own), text cube id,
    // then the daily record: long mark time, int count x long (cubes fed today), int count x long (cube uids
    // played with today). 11 also shows the fed / played notice with the list sizes as the day's counts.
    public static ByteWriter Feed(long characterId, InteractCube cube, long markTime, IReadOnlyCollection<long> fedToday, IReadOnlyCollection<long> playedToday) {
        var pWriter = Packet.Of(SendOp.FunctionCube);
        pWriter.Write<Command>(Command.Feed);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(cube.Id);
        pWriter.WriteLong(markTime);
        pWriter.WriteInt(fedToday.Count);
        foreach (long uid in fedToday) {
            pWriter.WriteLong(uid);
        }
        pWriter.WriteInt(playedToday.Count);
        foreach (long uid in playedToday) {
            pWriter.WriteLong(uid);
        }
        return pWriter;
    }

    // Client 2525 functioncubeobject.cpp:2580: long character id, text cube id, int error. The nurturing cube
    // shows: 1 no feed item, 2 fed the day's maximum, 3 played the day's maximum, 4 already played with this one.
    public static ByteWriter NurturingError(long characterId, InteractCube cube, int error) {
        var pWriter = Packet.Of(SendOp.FunctionCube);
        pWriter.Write<Command>(Command.NurturingError);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(cube.Id);
        pWriter.WriteInt(error);
        return pWriter;
    }
}
