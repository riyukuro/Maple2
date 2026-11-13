using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Packets;

// FittingDoll 0x7B, read by CSceneStateGame::OnFittingDoll (client/ms2/scenestategame.cpp:12128).
public static class FittingDollPacket {
    private enum Command : byte {
        Load = 0,
        Remove = 1,
        PutOn = 6,
        PutOff = 7,
        Transform = 8,
        Error = 11,
    }

    // The doll and its items. The client keys the doll by its position's cube cell and puts it in place of the cube
    // there, now or when the cube loads (CubeMapInformationService::FUN_1410cb4c0).
    public static ByteWriter Load(FieldFittingDoll doll, ICollection<Item> items) {
        var pWriter = Packet.Of(SendOp.FittingDoll);
        pWriter.WriteByte((byte) Command.Load);
        pWriter.WriteInt(doll.ObjectId);
        pWriter.WriteLong(doll.Uid);
        pWriter.WriteInt(doll.Cube.ItemId); // the doll item, whose model and default look the client takes
        WriteVector(pWriter, doll.Position);
        WriteVector(pWriter, doll.Rotation);
        pWriter.WriteBool(items.Count > 0);
        if (items.Count > 0) {
            pWriter.WriteInt(items.Count);
            foreach (Item item in items) {
                WriteItem(pWriter, item);
            }
        }
        return pWriter;
    }

    public static ByteWriter Remove(FieldFittingDoll doll) {
        var pWriter = Packet.Of(SendOp.FittingDoll);
        pWriter.WriteByte((byte) Command.Remove);
        pWriter.WriteInt(doll.ObjectId);
        return pWriter;
    }

    public static ByteWriter PutOn(FieldFittingDoll doll, Item item) {
        var pWriter = Packet.Of(SendOp.FittingDoll);
        pWriter.WriteByte((byte) Command.PutOn);
        pWriter.WriteLong(doll.Uid);
        WriteItem(pWriter, item);
        return pWriter;
    }

    public static ByteWriter PutOff(FieldFittingDoll doll, EquipSlot slot) {
        var pWriter = Packet.Of(SendOp.FittingDoll);
        pWriter.WriteByte((byte) Command.PutOff);
        pWriter.WriteLong(doll.Uid);
        pWriter.WriteByte((byte) slot);
        return pWriter;
    }

    // The answer to a swap, to the one who asked: the client plays the swap pose on its own character and the doll.
    public static ByteWriter Transform(FieldFittingDoll doll, FittingDollRules.TransformResult result) {
        var pWriter = Packet.Of(SendOp.FittingDoll);
        pWriter.WriteByte((byte) Command.Transform);
        pWriter.WriteLong(doll.Uid);
        pWriter.WriteInt((int) result);
        return pWriter;
    }

    public static ByteWriter Error(FittingDollRules.Error error) {
        var pWriter = Packet.Of(SendOp.FittingDoll);
        pWriter.WriteByte((byte) Command.Error);
        pWriter.WriteInt((int) error);
        return pWriter;
    }

    private static void WriteVector(ByteWriter pWriter, Vector3 vector) {
        pWriter.WriteFloat(vector.X);
        pWriter.WriteFloat(vector.Y);
        pWriter.WriteFloat(vector.Z);
    }

    private static void WriteItem(ByteWriter pWriter, Item item) {
        pWriter.WriteInt(item.Id);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteByte((byte) item.Slot);
        pWriter.WriteInt(item.Rarity);
        pWriter.WriteClass<Item>(item);
    }
}
