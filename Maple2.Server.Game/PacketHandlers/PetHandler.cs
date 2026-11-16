using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
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
        AttackState = 15, // UIGameMenuDialog::OnPetAttack: the client flips its pet attack flag and sends the new value
        ViewOther = 16, // UIPopUpMenu::SendRequestPet: the pet popup of another player's pet
        Evolve = 17,
        EvolvePoints = 18,
    }

    // The pet window makes its compose tab only with PetBattle on and its evolution tab only with PetEvolution01 on
    // (uipetsystemdialog.cpp:339/369); those tabs are the only senders of Fusion (tabcontentspetcompose.cpp:790) and
    // Evolve / EvolvePoints (tabcontentspetevolution.cpp:912/937).
    public const string ComposeFeature = "PetBattle";
    public const string EvolutionFeature = "PetEvolution01";

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        string? feature = command switch {
            Command.Fusion => ComposeFeature,
            Command.Evolve or Command.EvolvePoints => EvolutionFeature,
            _ => null,
        };
        if (feature != null && !session.ServerTableMetadata.FeatureTable.IsOn(feature)) {
            return;
        }

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
            case Command.AttackState:
                HandleAttackState(session, packet);
                return;
            case Command.ViewOther:
                HandleViewOther(session, packet);
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
        // The home's pvp row forbids a summon (s_ugcmap_fun_pvp_ffa_forbiden_summon_pet, MyPC::FUN_14152aa90): the
        // client asks it before a summon and a switch.
        if (HomeRowCheck.Blocks(session, HomePermission.Pvp)) {
            return;
        }
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
        if (HomeRowCheck.Blocks(session, HomePermission.Pvp)) {
            return;
        }
        // The client asks only its own state and the field's pet flag before a switch (mypc.cpp FUN_14152b8d0),
        // so the server weighs the next pet first: one that is not in the pet bag, or that the field or the
        // character's level refuses, leaves the summoned pet out instead of putting it away for nothing.
        if (!CanSwitchTo(session, petUid)) {
            return;
        }

        session.Pet.Dispose();
        SummonPet(session, petUid);
        if (session.Pet == null) {
            // Nothing came out after all (no spawn point): the old pet's stats leave the owner's totals.
            session.Player.Stats.Refresh();
        }
    }

    internal static bool CanSwitchTo(GameSession session, long petUid) {
        if (session.Field == null) {
            return false;
        }

        lock (session.Item) {
            Item? next = session.Item.Inventory.Get(petUid, InventoryType.Pets);
            return next != null && !session.PcBang.Refuses(next)
                && PetRules.CanSummon(session.Field.Metadata.Limit.Pet, next.Metadata.Limit.Level, session.Player.Value.Character.Level);
        }
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
        if (!PetRules.IsPotionCount(count)) {
            return;
        }

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

    // Mode 12 (TabContentsPetCompose::SendComposeRequest): the pet gains exp from the picked pets and pet exp
    // books (uid and count per entry, one entry per uid). The window has made its checks already; the server
    // makes them again in the order the entries come, pays the meso, binds a pet with a bind rule (the window
    // asked first, s_msg_transfer_bind_pet_compose) and answers mode 10 (exp), mode 11 (level, when it
    // changed) and mode 12 (today's fusion count, which lowers the exp of later pet fodders).
    private void HandleFusion(GameSession session, IByteReader packet) {
        long petUid = packet.ReadLong();
        short count = packet.ReadShort();
        var entries = new List<(long Uid, int Amount)>();
        for (int i = 0; i < count; i++) {
            long uid = packet.ReadLong();
            int amount = packet.ReadInt();
            entries.Add((uid, amount));
        }
        if (entries.Count is 0 or > PetFusionRules.MaxFodders || entries.Select(entry => entry.Uid).Distinct().Count() != entries.Count) {
            return;
        }

        Character character = session.Player.Value.Character;
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        int maxLevel = constants.PetMaxLevel;
        PetExpTable table = session.TableMetadata.PetExpTable;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        FusionPlan? checkedPlan = PlanFusion(session, petUid, entries, now);
        if (checkedPlan == null) {
            return;
        }
        Item pet = checkedPlan.Pet;
        int dailyCount = checkedPlan.DailyCount;
        int petFodders = checkedPlan.PetFodders;
        long gain = checkedPlan.Gain;
        long cost = checkedPlan.Cost;
        List<(long Uid, int Amount)> plan = checkedPlan.Taken;

        // KMS2 order: the meso, then the fodders. The meso is held in memory; the wallet, the fodders' rows and the
        // pet's row with its new level and exp are stored in one transaction, and the bag and the pet follow once it
        // is committed. Before, the meso and the fodders were taken and the exp added in memory, left to the next
        // session save, so a crash before it gave all of it back with the exp gone.
        long sends = session.Currency.MesoSends;
        if (!session.Currency.TryTakeMeso(cost, notify: false)) {
            session.Send(PetPacket.Error(PetError.s_err_lack_meso));
            return;
        }
        Item fused = pet;
        bool taken = true;
        var held = new List<int>();
        (int Level, long Exp) Fused() => PetFusionRules.Apply(table, fused.Pet!.Level, fused.Pet.Exp, fused.Rarity, fused.Metadata.Limit.Level, gain, maxLevel);
        int oldLevel = 0;
        bool stored = session.TrySavePurchase(db => {
            // The checks again, with the bag held until the rows are written: a fodder or the pet may have changed
            // since the request was checked (the lock was let go for the save).
            lock (session.Item) {
                if (PlanFusion(session, petUid, entries, now) is not { } again || !again.SameAs(checkedPlan)) {
                    return false;
                }
                foreach ((long uid, int amount) in plan) {
                    if (!session.Item.Inventory.SaveConsumed(db, uid, amount)) {
                        return false;
                    }
                }
                if (!SavePetAs(session, db, fused, item => (item.Pet!.Level, item.Pet.Exp) = ((short, long)) Fused())) {
                    return false;
                }
            }
            // Under the account lock, so no session save between the commit and memory writes the old rows.
            db.AfterCommit(() => {
                // KMS2 order: the meso, then the fodders.
                session.Currency.NotifyMeso(quests: false);
                foreach ((long uid, int amount) in plan) {
                    taken &= session.Item.Inventory.ConsumeStored(uid, amount, held);
                }
                lock (session.Item) {
                    oldLevel = fused.Pet!.Level;
                    (int level, long exp) = Fused();
                    fused.Pet.Level = (short) level;
                    fused.Pet.Exp = exp;
                    character.PetFusionCount = dailyCount + petFodders;
                    character.PetFusionTime = now;
                }
            });
            return true;
        });
        if (!stored) {
            session.Currency.ReturnHeldMeso(cost, sends);
            session.Send(PetPacket.Error(PetError.s_common_error_unknown));
            return;
        }
        session.Quest?.UpdateMeso();
        session.Item.Inventory.UpdateHeld(held);
        if (!taken) {
            Logger.Error("Pet fusion of {PetUid}: a stored fodder was not in the bag of {CharacterId}", fused.Uid, session.CharacterId);
        }
        session.Item.Bind(fused);

        session.Send(PetPacket.Fusion(session.Player.ObjectId, fused));
        if (fused.Pet!.Level != oldLevel) {
            session.Send(PetPacket.LevelUp(session.Player.ObjectId, fused));
            if (session.Pet?.Pet.Uid == fused.Uid) {
                // The summoned pet's stats count in the owner's totals.
                session.Player.Stats.Refresh();
                PetManager.SendItemUpdate(session, fused);
            }
        }
        session.Send(PetPacket.FusionCount(character.PetFusionCount));
        // "Use 3 pets as fusion catalysts" (70600026): each pet taken as a fodder; exp books are no pets.
        // 90000795 names the fused pet's item id as its code.
        if (petFodders > 0) {
            session.ConditionUpdate(ConditionType.pet_enchant, counter: petFodders, codeLong: fused.Id);
        }
        // "Acquire 10,000 pet experience through fusion" (70600033): the exp the fodders gave.
        if (gain > 0) {
            session.ConditionUpdate(ConditionType.pet_enchant_exp, counter: gain);
        }
    }

    // A checked fusion request: the pet, the rows it takes (a pet fodder whole, a book by its count), the exp they
    // give, the meso it costs and the daily count it starts from.
    private sealed record FusionPlan(Item Pet, short Level, long Exp, List<(long Uid, int Amount)> Taken, long Gain, long Cost, int PetFodders, int DailyCount) {
        public bool SameAs(FusionPlan other) => ReferenceEquals(Pet, other.Pet) && Level == other.Level && Exp == other.Exp
            && Taken.SequenceEqual(other.Taken) && Gain == other.Gain && Cost == other.Cost && PetFodders == other.PetFodders && DailyCount == other.DailyCount;
    }

    // The server's checks of a fusion request (null: refused), under the item lock.
    private static FusionPlan? PlanFusion(GameSession session, long petUid, List<(long Uid, int Amount)> entries, long now) {
        Character character = session.Player.Value.Character;
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        int maxLevel = constants.PetMaxLevel;
        PetExpTable table = session.TableMetadata.PetExpTable;
        int petFodders = 0;
        long gain = 0;
        long cost = 0;
        var plan = new List<(long Uid, int Amount)>();
        lock (session.Item) {
            Item? pet = FindOwnPet(session, petUid);
            if (pet?.Pet == null || !session.ItemMetadata.TryGetPet(pet.Metadata.Property.PetId, out PetMetadata? petMetadata)) {
                return null;
            }

            if (!PetFusionRules.IsTarget(petMetadata.Type, pet.Pet.Level, maxLevel)) {
                return null;
            }

            long summonedUid = session.Pet?.Pet.Uid ?? 0;
            int dailyCount = PetFusionRules.DailyCount(character.PetFusionCount, character.PetFusionTime, PetFusionRules.LastResetTime());
            int bonusLimit = constants.DailyPetEnchantMaxCount;
            int saleRate = PetComposeSaleRate(session);

            int units = 0;
            foreach ((long uid, int amount) in entries) {
                Item? fodder = session.Item.Inventory.Get(uid);
                int? fodderType = fodder?.Pet != null && session.ItemMetadata.TryGetPet(fodder.Metadata.Property.PetId, out PetMetadata? fodderMetadata)
                    ? fodderMetadata.Type : null;
                PetFusionRules.FodderKind kind = fodder == null ? PetFusionRules.FodderKind.None : PetFusionRules.KindOf(fodder, fodderType);
                if (PetFusionRules.CheckFodder(pet, fodder, kind, amount, summonedUid, now) != PetFusionRules.FodderError.None) {
                    return null;
                }

                for (int i = 0; i < amount; i++) {
                    (int predicted, _) = PetFusionRules.Apply(table, pet.Pet.Level, pet.Pet.Exp, pet.Rarity, pet.Metadata.Limit.Level, gain, maxLevel);
                    if (PetFusionRules.CheckUnit(units, predicted, maxLevel) != PetFusionRules.FodderError.None) {
                        return null;
                    }

                    if (kind == PetFusionRules.FodderKind.Pet) {
                        (long baseExp, long bonusExp) = PetFusionRules.PetExp(pet, fodder!, dailyCount + petFodders, bonusLimit);
                        gain += baseExp + bonusExp;
                        petFodders++;
                    } else {
                        gain += PetFusionRules.BookExp(fodder!.Metadata.Function);
                    }
                    units++;
                }
                cost += PetFusionRules.ApplySale(PetFusionRules.UnitCost(pet, fodder!, kind), saleRate) * amount;
                plan.Add((fodder!.Uid, kind == PetFusionRules.FodderKind.Pet ? fodder.Amount : amount));
            }
            return new FusionPlan(pet, pet.Pet.Level, pet.Pet.Exp, plan, gain, cost, petFodders, dailyCount);
        }
    }

    // Writes the pet's row as change leaves it, in db's transaction, and puts the pet back as it was: memory takes the
    // change once that transaction is committed (the caller's AfterCommit).
    internal static bool SavePetAs(GameSession session, GameStorage.Request db, Item pet, Action<Item> change) {
        lock (session.Item) {
            ItemPet before = pet.Pet!.Clone();
            change(pet);
            try {
                return db.SaveItems(session.CharacterId, pet);
            } finally {
                pet.Pet.Name = before.Name;
                pet.Pet.Exp = before.Exp;
                pet.Pet.EvolvePoints = before.EvolvePoints;
                pet.Pet.Level = before.Level;
                pet.Pet.RenameRemaining = before.RenameRemaining;
            }
        }
    }

    // The rate of the running PetComposeSale events in 1/10000 (the highest, as CGameRule::GetPetComposeSaleRate
    // reads it), 0 without one.
    private static int PetComposeSaleRate(GameSession session) {
        return EventSaleRules.HighestRate(session.FindEvent(GameEventType.PetComposeSale));
    }

    // Mode 15: the game menu's pet attack button, shown for a pet whose row has type 1 (feature 0x97). The
    // client flips its own flag (CPetManager::SendRequestPet, petmanager.cpp:1118), shows it on the button and reads
    // nothing back. Off, the summoned battle pet stops fighting (FieldPet.Battles).
    private void HandleAttackState(GameSession session, IByteReader packet) {
        session.PetAttackOff = !packet.ReadBool();
    }

    // Mode 16: "pet info" in the popup of another player's pet (uipartydialog.cpp:3397). The body is the owner's
    // character id and the uid of the pet item the owner has summoned; for its own pet the client opens its pet
    // window and sends nothing. The answer (mode 16: owner object id, item id, uid, rarity, item) opens the
    // other-pet window (CPetManager::OnOtherPetInfo). Only a pet summoned in the sender's field is shown.
    private void HandleViewOther(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        long petUid = packet.ReadLong();
        if (session.Field == null || !session.Field.TryGetPlayerById(characterId, out FieldPlayer? owner)) {
            return;
        }

        Item? pet = owner.Session.Pet?.Pet;
        if (pet == null || !PetRules.IsShownPet(pet.Uid, petUid)) {
            return;
        }

        session.Send(PetPacket.PetInfo(owner.ObjectId, pet));
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
            session.ConditionUpdate(ConditionType.pet_evolution_by_rank, codeLong: nextRarity);
            if (session.Pet?.Pet.Uid == pet.Uid) {
                // The summoned pet's stats count in the owner's totals.
                session.Player.Stats.Refresh();
                PetManager.SendItemUpdate(session, pet);
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

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        EvolvePlan? checkedPlan = PlanEvolvePoints(session, petUid, fodderUids, now);
        if (checkedPlan == null) {
            return;
        }
        Item pet = checkedPlan.Pet;
        long total = checkedPlan.Total;
        List<Item> fodders = checkedPlan.Fodders;

        long cost = PetEvolveRules.Cost(fodders.Select(fodder => fodder.Rarity));
        // KMS2 order: the meso, then the fodders. As a fusion: the meso held in memory, the wallet, the fodders' rows
        // and the pet's row with its points stored in one transaction, memory after the commit. Before, all of it was
        // left to the next session save.
        long sends = session.Currency.MesoSends;
        if (!session.Currency.TryTakeMeso(cost, notify: false)) {
            session.Send(PetPacket.Error(PetError.s_err_lack_meso));
            return;
        }
        Item evolving = pet;
        int points = (int) Math.Min(total, int.MaxValue);
        int before = 0;
        bool taken = true;
        var held = new List<int>();
        bool stored = session.TrySavePurchase(db => {
            // The checks again, with the bag held until the rows are written (as a fusion).
            lock (session.Item) {
                if (PlanEvolvePoints(session, petUid, fodderUids, now) is not { } again || !again.SameAs(checkedPlan)) {
                    return false;
                }
                foreach (Item fodder in fodders) {
                    if (!session.Item.Inventory.SaveConsumed(db, fodder.Uid, fodder.Amount)) {
                        return false;
                    }
                }
                if (!SavePetAs(session, db, evolving, item => item.Pet!.EvolvePoints = points)) {
                    return false;
                }
            }
            db.AfterCommit(() => {
                // KMS2 order: the meso, then the fodders.
                session.Currency.NotifyMeso(quests: false);
                foreach (Item fodder in fodders) {
                    taken &= session.Item.Inventory.ConsumeStored(fodder.Uid, fodder.Amount, held);
                }
                lock (session.Item) {
                    before = evolving.Pet!.EvolvePoints;
                    evolving.Pet.EvolvePoints = points;
                }
            });
            return true;
        });
        if (!stored) {
            session.Currency.ReturnHeldMeso(cost, sends);
            session.Send(PetPacket.Error(PetError.s_common_error_unknown));
            return;
        }
        session.Quest?.UpdateMeso();
        session.Item.Inventory.UpdateHeld(held);
        if (!taken) {
            Logger.Error("Pet evolution points of {PetUid}: a stored fodder was not in the bag of {CharacterId}", evolving.Uid, session.CharacterId);
        }
        session.Send(PetPacket.EvolvePoints(session.Player.ObjectId, evolving));
        PetManager.SendItemUpdate(session, evolving);
        // 23100387-23100390 "Gain {0} evolution point(s) with a normal pet": the points gained, by the pet's rarity.
        session.ConditionUpdate(ConditionType.pet_evolution_point_by_rank, counter: points - before, codeLong: evolving.Rarity);
    }

    // A checked evolution points request: the pet, its fodders (each taken whole) and the points it ends with.
    private sealed record EvolvePlan(Item Pet, int Points, List<Item> Fodders, List<int> Amounts, long Total) {
        public bool SameAs(EvolvePlan other) => ReferenceEquals(Pet, other.Pet) && Points == other.Points && Total == other.Total
            && Fodders.Count == other.Fodders.Count && Fodders.Zip(other.Fodders).All(pair => ReferenceEquals(pair.First, pair.Second))
            && Amounts.SequenceEqual(other.Amounts);
    }

    // The server's checks of an evolution points request (null: refused), under the item lock.
    private static EvolvePlan? PlanEvolvePoints(GameSession session, long petUid, List<long> fodderUids, long now) {
        var fodders = new List<Item>();
        lock (session.Item) {
            Item? pet = FindOwnPet(session, petUid);
            if (pet?.Pet == null || !IsEvolveTarget(session, pet)) {
                return null;
            }

            int need = PetEvolveRules.Need(session.TableMetadata.PetEvolutionTable, pet.Metadata.Limit.Level, pet.Rarity);
            long summonedUid = session.Pet?.Pet.Uid ?? 0;
            long total = pet.Pet.EvolvePoints;
            foreach (long uid in fodderUids) {
                Item? fodder = session.Item.Inventory.Get(uid, InventoryType.Pets);
                if (fodders.Any(taken => taken.Uid == uid)) {
                    return null;
                }
                if (PetEvolveRules.CheckFodder(pet, fodder, summonedUid, total, need, now) != PetEvolveRules.FodderError.None) {
                    return null;
                }
                fodders.Add(fodder!);
                total += PetEvolveRules.FodderPoints(fodder!.Pet!.EvolvePoints);
            }
            return new EvolvePlan(pet, pet.Pet.EvolvePoints, fodders, fodders.Select(fodder => fodder.Amount).ToList(), total);
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

    // A pet binds to the character that first summons it; KMS2 sends BindItem after the field pet and before the
    // summon answer. The client asks first for any pet with a binding rule and no binding (MyPC summon,
    // s_msg_transfer_bind_pet_summon; CItemTransfer::HasBindingRule is the rule's bind bit), so a bind-on-loot pet
    // that is not bound yet binds too, not only the bind-pet ones.
    internal static void BindOnSummon(GameSession session, Item pet) {
        if (pet.Transfer?.Bind(session.Player.Value.Character) == true) {
            session.Send(ItemBindPacket.Bind(pet));
        }
    }

    internal static void SummonPet(GameSession session, long petUid, bool fieldEntry = false) {
        if (session.Field == null || session.Pet != null) {
            return;
        }

        lock (session.Item) {
            Item? pet = session.Item.Inventory.Get(petUid, InventoryType.Pets);
            if (pet == null || session.PcBang.Refuses(pet)) {
                return;
            }
            if (!PetRules.CanSummon(session.Field.Metadata.Limit.Pet, pet.Metadata.Limit.Level, session.Player.Value.Character.Level)) {
                return;
            }

            FieldPet? fieldPet = session.Field.SpawnPet(pet, session.Player.Position, session.Player.Rotation, player: session.Player);
            if (fieldPet == null) {
                return;
            }
            // A field entry's summon comes at the arrival spot, above the ground the client lands the owner on: the pet
            // goes to the owner's first spot on the ground (FieldPet.OwnerLanded).
            if (fieldEntry) {
                fieldPet.AwaitOwnerLanding(session.Player.Position, Environment.TickCount64);
            }

            session.Field.Broadcast(FieldPacket.AddPet(fieldPet));
            BindOnSummon(session, pet);
            session.Field.Broadcast(ProxyObjectPacket.AddPet(fieldPet));
            session.Pet = new PetManager(session, fieldPet);
            session.Pet.Load(summoned: true);
            // The pet's stats join the owner's totals.
            session.Player.Stats.Refresh();
            fieldPet.AddPetEffects();
        }
    }
}
