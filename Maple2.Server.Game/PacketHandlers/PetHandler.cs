using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class PetHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestPet;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required BanWordStorage BanWordStorage { private get; init; }
    // ReSharper restore All
    #endregion

    private enum Command : byte {
        Summon = 0,
        UnSummon = 1,
        Switch = 3,
        Rename = 4,
        UpdatePotionConfig = 5,
        UpdateLootConfig = 6,
        Fusion = 12, // OnPetEnchant, s_msg_transfer_bind_pet_compose
        ToggleAuto = 15, // the client flips its pet auto flag and sends the new value
        Unknown16 = 16,
        Evolve = 17,
        EvolvePoints = 18,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();

        switch (command) {
            case Command.Summon:
                HandleSummon(session, packet);
                return;
            case Command.UnSummon:
                HandleUnSummon(session, packet);
                return;
            case Command.Switch:
                HandleSwitch(session, packet);
                return;
            case Command.Rename:
                HandleRename(session, packet);
                return;
            case Command.UpdatePotionConfig:
                HandleUpdatePotionConfig(session, packet);
                return;
            case Command.UpdateLootConfig:
                HandleUpdateLootConfig(session, packet);
                return;
            case Command.Fusion:
                HandleFusion(session, packet);
                return;
            case Command.ToggleAuto:
                HandleToggleAuto(session, packet);
                return;
            case Command.Unknown16:
                HandleUnknown16(session, packet);
                return;
            case Command.Evolve:
                HandleEvolve(session, packet);
                return;
            case Command.EvolvePoints:
                HandleEvolvePoints(session, packet);
                return;
        }
    }

    private void HandleSummon(GameSession session, IByteReader packet) {
        long petUid = packet.ReadLong();
        SummonPet(session, petUid);
    }

    private void HandleUnSummon(GameSession session, IByteReader packet) {
        long petUid = packet.ReadLong();
        if (session.Pet?.Pet.Uid != petUid) {
            return;
        }

        session.Pet.Dispose();
        // The pet's stats leave the owner's totals.
        session.Player.Stats.Refresh();
    }

    private void HandleSwitch(GameSession session, IByteReader packet) {
        if (session.Pet == null) {
            return;
        }

        long petUid = packet.ReadLong();
        session.Pet.Dispose();
        SummonPet(session, petUid);
    }

    private void HandleRename(GameSession session, IByteReader packet) {
        if (session.Pet == null) {
            return;
        }

        string name = packet.ReadUnicodeString();
        // The client's rename box shows its own error for a name its filter refuses and never sends it.
        // The server's list can hold a word the client's does not: that name gets the filter's message box.
        if (PetRules.IsBannedName(name, BanWordStorage.ContainsBannedWord)) {
            session.Send(NoticePacket.MessageBox(StringCode.s_ban_check_err_any));
            return;
        }
        if (!PetRules.IsAllowedName(name, BanWordStorage.ContainsBannedWord)) {
            return;
        }
        session.Pet.Rename(name);
    }

    private void HandleUpdatePotionConfig(GameSession session, IByteReader packet) {
        if (session.Pet == null) {
            return;
        }

        byte count = packet.ReadByte();

        var config = new PetPotionConfig[count];
        for (int i = 0; i < count; i++) {
            config[i] = packet.Read<PetPotionConfig>();
        }

        session.Pet.UpdatePotionConfig(config);
    }

    private void HandleUpdateLootConfig(GameSession session, IByteReader packet) {
        if (session.Pet == null) {
            return;
        }

        var config = packet.Read<PetLootConfig>();
        session.Pet.UpdateLootConfig(config);
    }

    private void HandleFusion(GameSession session, IByteReader packet) {
        long petUid = packet.ReadLong();
        short count = packet.ReadShort();
        for (int i = 0; i < count; i++) {
            packet.ReadLong(); // fodder uid
            packet.ReadInt(); // count
        }
    }

    private void HandleToggleAuto(GameSession session, IByteReader packet) {
        packet.ReadBool(); // auto on
    }

    private void HandleUnknown16(GameSession session, IByteReader packet) {
        packet.ReadLong();
        packet.ReadLong();
    }

    // Mode 17 (TabContentsPetEvolution::OnPetEnchant): the pet has the points its evolution needs. It goes to
    // the next rarity in place; the answer (mode 17: owner, uid, rarity, item) refreshes the window and the
    // item, and the summoned pet's item when it is that pet (OnEvolutionSuccess).
    private void HandleEvolve(GameSession session, IByteReader packet) {
        long petUid = packet.ReadLong();
        if (session.Field == null) {
            return;
        }

        lock (session.Item) {
            Item? pet = FindOwnPet(session, petUid);
            if (pet?.Pet == null || !IsEvolveTarget(session, pet)) {
                return;
            }

            int need = PetEvolveRules.Need(session.TableMetadata.PetEvolutionTable, pet.Metadata.Limit.Level, pet.Rarity);
            int nextRarity = pet.Rarity + 1;
            int resultLevel = Lua.CalcPetEvolutionResultLevel(nextRarity, pet.Metadata.Limit.Level);
            if (!PetEvolveRules.CanEvolve(pet.Pet.EvolvePoints, need, resultLevel)) {
                return;
            }

            ItemStats? old = pet.Stats;
            pet.Rarity = nextRarity;
            pet.Stats = PetEvolveRules.EvolvedStats(session.Field.ItemStatsCalc.GetStats(pet), old);
            pet.Pet.Level = (short) resultLevel;
            pet.Pet.Exp = 0;
            pet.Pet.EvolvePoints = 0;

            session.Send(PetPacket.Evolve(session.Player.ObjectId, pet));
            if (session.Pet?.Pet.Uid == pet.Uid) {
                // The summoned pet's stats count in the owner's totals.
                session.Player.Stats.Refresh();
            }
        }
    }

    // Mode 18 (OnPetEnchant with fodders): the picked pets are used up for evolution points. The window has
    // made its checks already; the server makes them again in its order, pays calc_petEvolutionCost per
    // fodder and answers mode 18 (owner, points, uid), which sets the points (OnEvolutionPointUp).
    private void HandleEvolvePoints(GameSession session, IByteReader packet) {
        long petUid = packet.ReadLong();
        short count = packet.ReadShort();
        var fodderUids = new List<long>();
        for (int i = 0; i < count; i++) {
            fodderUids.Add(packet.ReadLong());
        }
        if (fodderUids.Count is 0 or > PetEvolveRules.MaxFodders) {
            return;
        }

        lock (session.Item) {
            Item? pet = FindOwnPet(session, petUid);
            if (pet?.Pet == null || !IsEvolveTarget(session, pet)) {
                return;
            }

            int need = PetEvolveRules.Need(session.TableMetadata.PetEvolutionTable, pet.Metadata.Limit.Level, pet.Rarity);
            long summonedUid = session.Pet?.Pet.Uid ?? 0;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long total = pet.Pet.EvolvePoints;
            var fodders = new List<Item>();
            foreach (long uid in fodderUids) {
                Item? fodder = session.Item.Inventory.Get(uid, InventoryType.Pets);
                if (fodders.Any(taken => taken.Uid == uid)) {
                    return;
                }
                if (PetEvolveRules.CheckFodder(pet, fodder, summonedUid, total, need, now) != PetEvolveRules.FodderError.None) {
                    return;
                }
                fodders.Add(fodder!);
                total += PetEvolveRules.FodderPoints(fodder!.Pet!.EvolvePoints);
            }

            long cost = PetEvolveRules.Cost(fodders.Select(fodder => fodder.Rarity));
            if (session.Currency.Meso < cost) {
                session.Send(PetPacket.Error(PetError.s_err_lack_meso));
                return;
            }

            foreach (Item fodder in fodders) {
                if (!session.Item.Inventory.Consume(fodder.Uid)) {
                    throw new InvalidOperationException($"Failed to consume evolution fodder: {fodder.Uid} after validating");
                }
            }
            session.Currency.Meso -= cost;
            pet.Pet.EvolvePoints = (int) Math.Min(total, int.MaxValue);
            session.Send(PetPacket.EvolvePoints(session.Player.ObjectId, pet));
        }
    }

    // The pet the evolution tab picked: in the pet inventory, or the summoned pet.
    private static Item? FindOwnPet(GameSession session, long petUid) {
        return session.Pet?.Pet.Uid == petUid ? session.Pet.Pet : session.Item.Inventory.Get(petUid, InventoryType.Pets);
    }

    private static bool IsEvolveTarget(GameSession session, Item pet) {
        if (!session.ItemMetadata.TryGetPet(pet.Metadata.Property.PetId, out PetMetadata? metadata)) {
            return false;
        }
        return PetEvolveRules.IsTarget(metadata.Type, pet.Pet!.Level, session.ServerTableMetadata.ConstantsTable.PetMaxLevel);
    }

    internal static void SummonPet(GameSession session, long petUid) {
        if (session.Field == null || session.Pet != null) {
            return;
        }

        lock (session.Item) {
            Item? pet = session.Item.Inventory.Get(petUid, InventoryType.Pets);
            if (pet == null) {
                return;
            }
            if (!PetRules.CanSummon(session.Field.Metadata.Limit.Pet, pet.Metadata.Limit.Level, session.Player.Value.Character.Level)) {
                return;
            }

            FieldPet? fieldPet = session.Field.SpawnPet(pet, session.Player.Position, session.Player.Rotation, player: session.Player);
            if (fieldPet == null) {
                return;
            }

            session.Field.Broadcast(FieldPacket.AddPet(fieldPet));
            session.Field.Broadcast(ProxyObjectPacket.AddPet(fieldPet));
            session.Pet = new PetManager(session, fieldPet);
            session.Pet.Load();
            // The pet's stats join the owner's totals.
            session.Player.Stats.Refresh();
        }
    }
}
