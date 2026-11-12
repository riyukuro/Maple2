using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class PetPacket {
    private enum Command : byte {
        Summon = 0,
        UnSummon = 1,
        WarpSound = 2,
        Rename = 4,
        PotionConfig = 5,
        LootConfig = 6,
        LoadCollection = 7,
        AddCollection = 8,
        Load = 9,
        Fusion = 10,
        LevelUp = 11,
        FusionCount = 12,
        TrainingComplete = 13,
        AutoChanged = 15,
        PetInfo = 16,
        Evolve = 17,
        EvolvePoints = 18,
        Error = 19,
        MasterSnare = 20,
        Unknown21 = 21,
    }

    public static ByteWriter Summon(FieldPet pet) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.Summon);
        pWriter.WriteInt(pet.OwnerId);
        pWriter.WriteInt(pet.ObjectId);
        pWriter.WritePetItem(pet.Pet);

        return pWriter;
    }

    public static ByteWriter UnSummon(FieldPet pet) => UnSummon(pet.OwnerId, pet.Pet.Uid);

    // For the owner the client also prints "You dismissed your pet." (CPetManager::OnPetUnsummoned).
    public static ByteWriter UnSummon(int ownerObjectId, long petUid) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.UnSummon);
        pWriter.WriteInt(ownerObjectId);
        pWriter.WriteLong(petUid);

        return pWriter;
    }

    // Client reader: CPetManager::OnPetSummonSound (mode 2: int owner object id): the owner's client plays the
    // pet summon sound. KMS2 sends it to the owner when the pet is put next to them (the pet's control entry
    // of the same moment has the new position), not at the summon itself, where the field entry plays it.
    public static ByteWriter WarpSound(int ownerId) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.WarpSound);
        pWriter.WriteInt(ownerId);

        return pWriter;
    }

    public static ByteWriter Rename(int ownerId, ItemPet pet) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.Rename);
        pWriter.WriteInt(ownerId);
        pWriter.WriteProfile(pet);

        return pWriter;
    }

    public static ByteWriter UpdatePotionConfig(int ownerId, PetPotionConfig[] potionConfigs) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.PotionConfig);
        pWriter.WriteInt(ownerId);

        pWriter.WriteByte((byte) potionConfigs.Length);
        foreach (PetPotionConfig config in potionConfigs) {
            pWriter.Write<PetPotionConfig>(config);
        }

        return pWriter;
    }

    public static ByteWriter UpdateLootConfig(int ownerId, in PetLootConfig lootConfig) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.LootConfig);
        pWriter.WriteInt(ownerId);
        pWriter.Write<PetLootConfig>(lootConfig);

        return pWriter;
    }

    public static ByteWriter LoadCollection(IDictionary<int, short> collection) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.LoadCollection);
        pWriter.WriteInt(collection.Count);
        foreach ((int petId, short rarity) in collection) {
            pWriter.WriteInt(petId);
            pWriter.WriteShort(rarity);
        }

        return pWriter;
    }

    public static ByteWriter AddCollection(int petId, short rarity) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.AddCollection);
        pWriter.WriteInt(petId);
        pWriter.WriteShort(rarity);

        return pWriter;
    }

    public static ByteWriter Load(int ownerId, ItemPet pet, PetConfig config) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteInt(ownerId);
        pWriter.WriteProfile(pet);
        pWriter.WriteByte((byte) config.PotionConfig.Length);
        foreach (PetPotionConfig potionConfig in config.PotionConfig) {
            pWriter.Write<PetPotionConfig>(potionConfig);
        }
        pWriter.Write<PetLootConfig>(config.LootConfig);

        return pWriter;
    }

    // Client reader: CPetManager, pet mode 10 (int owner object id, long exp, long pet item uid): sets the
    // pet item's exp after a fusion.
    public static ByteWriter Fusion(int ownerId, Item pet) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.WriteByte((byte) Command.Fusion);
        pWriter.WriteInt(ownerId);
        pWriter.WriteLong(pet.Pet?.Exp ?? 0);
        pWriter.WriteLong(pet.Uid);

        return pWriter;
    }

    // Client reader: CPetManager, pet mode 11 (int owner object id, int level, long pet item uid): sets the
    // pet item's level and plays the level-up effect.
    public static ByteWriter LevelUp(int ownerId, Item pet) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.WriteByte((byte) Command.LevelUp);
        pWriter.WriteInt(ownerId);
        pWriter.WriteInt(pet.Pet?.Level ?? 1);
        pWriter.WriteLong(pet.Uid);

        return pWriter;
    }

    // Client reader: UIItemPetTrainingDialog::FUN_140e57cb0 (uiitempettrainingdialog.cpp:202), registered on this
    // opcode while the training dialog is open: the mode byte only; 13 plays the dialog's "complete" and locks its
    // slot. CPetManager has no case for 13.
    public static ByteWriter TrainingComplete() {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.WriteByte((byte) Command.TrainingComplete);

        return pWriter;
    }

    // Client reader: CPetManager, pet mode 12 (int count): today's pet fusion count (PC +0x11f4); refreshes
    // the fusion window.
    public static ByteWriter FusionCount(int count) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.WriteByte((byte) Command.FusionCount);
        pWriter.WriteInt(count);

        return pWriter;
    }

    // The client stores the bool as its pet auto flag, the one its own toggle request (mode 15) flips.
    public static ByteWriter AutoChanged(bool auto) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.AutoChanged);
        pWriter.WriteBool(auto);

        return pWriter;
    }

    // Answer to RequestPet 16 (CPetManager::OnOtherPetInfo): the owner's object id, the item id, uid and
    // rarity the client builds the item from, then the item; it opens the other-pet window.
    public static ByteWriter PetInfo(int ownerId, Item pet) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.WriteByte((byte) Command.PetInfo);
        pWriter.WriteInt(ownerId);
        pWriter.WriteInt(pet.Id);
        pWriter.WriteLong(pet.Uid);
        pWriter.WriteInt(pet.Rarity);
        pWriter.WriteClass<Item>(pet);

        return pWriter;
    }

    public static ByteWriter Evolve(int ownerId, Item pet) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.Evolve);
        pWriter.WriteInt(ownerId);
        pWriter.WriteLong(pet.Uid);
        pWriter.WriteByte((byte) pet.Rarity);
        pWriter.WriteClass<Item>(pet);

        return pWriter;
    }

    public static ByteWriter EvolvePoints(int ownerId, Item pet) {
        return EvolvePoints(ownerId, pet.Pet?.EvolvePoints ?? 0, pet.Uid);
    }

    // Client reader: CPetManager, pet mode 0x12 (int owner object id, int points, long pet item uid).
    public static ByteWriter EvolvePoints(int ownerId, int points, long petUid) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.WriteByte((byte) Command.EvolvePoints);
        pWriter.WriteInt(ownerId);
        pWriter.WriteInt(points);
        pWriter.WriteLong(petUid);

        return pWriter;
    }

    public static ByteWriter Error(PetError error) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.Error);
        pWriter.Write<PetError>(error);

        return pWriter;
    }

    public static ByteWriter MasterSnare(int itemId) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.MasterSnare);
        pWriter.WriteInt(itemId);

        return pWriter;
    }

    public static ByteWriter Unknown21(int ownerId, Item pet) {
        var pWriter = Packet.Of(SendOp.ResponsePet);
        pWriter.Write<Command>(Command.Unknown21);
        pWriter.WriteInt(ownerId);
        pWriter.WritePetItem(pet);

        return pWriter;
    }

    public static ByteWriter SyncTaming(int casterId, FieldPet pet) {
        var pWriter = Packet.Of(SendOp.SyncPetTamingPoint);
        pWriter.WriteInt(pet.ObjectId);
        pWriter.WriteInt(casterId);
        pWriter.WriteInt(pet.TamingPoint);

        return pWriter;
    }

    private static void WritePetItem(this IByteWriter writer, Item pet) {
        writer.WriteBool(true); // Not sure when false
        writer.WriteInt(pet.Id);
        writer.WriteLong(pet.Uid);
        writer.WriteInt(pet.Rarity);
        writer.WriteClass<Item>(pet);
    }

    private static void WriteProfile(this IByteWriter writer, ItemPet pet) {
        writer.WriteUnicodeString(pet.Name);
        writer.WriteLong(pet.Exp);
        writer.WriteInt();
        writer.WriteInt(pet.Level);
        writer.WriteShort(pet.RenameRemaining);
    }
}
