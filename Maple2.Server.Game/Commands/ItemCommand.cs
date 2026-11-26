using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Tools.Extensions;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

public class ItemCommand : GameCommand {
    private const int MAX_RARITY = 6;
    private const int MAX_SOCKET = 5;

    private readonly GameSession session;
    private readonly ItemMetadataStorage itemStorage;

    public ItemCommand(GameSession session, ItemMetadataStorage itemStorage) : base(AdminPermissions.SpawnItem, "item", "Item spawning.") {
        this.session = session;
        this.itemStorage = itemStorage;

        var id = new Argument<int>("id", "Id of item to spawn.");
        var amount = new Option<int>(["--amount", "-a"], () => 1, "Amount of the item.");
        var rarity = new Option<int>(["--rarity", "-r"], () => 0, "Rarity of the item (default: the item's own).");
        var drop = new Option<bool>(["--drop"], "Drop item instead of adding to inventory");
        var rollMax = new Option<bool>(["--roll-max"], () => false, "Max roll stats.");
        var enchant = new Option<int>(["--enchant"], () => 0, "Enchant level 0..15 (gear only).");
        var limitBreak = new Option<int>(["--limit-break"], () => 0, "Limit break level 0..the item's maximum (gear only; no enchant with it).");
        var sockets = new Option<int>(["--socket"], () => -1, "Unlocked sockets 0..5 (gear only; -1 leaves the item's own).");
        var gems = new Option<int[]>(["--gem"], "A gem item id for an unlocked socket, in order, 0 for an empty one (repeat or list; gear only).");
        var stats = new Option<string[]>(["--stat"], "A bonus stat as clientStatId=value, in the random group (repeat; gear only).");
        var specials = new Option<string[]>(["--special"], "A bonus special ability as clientSpecialId=value, in the random group (repeat; gear only).");
        var bonusClear = new Option<bool>(["--bonus-clear"], () => false, "Clear the rolled random options, so only the --stat/--special lines stay.");

        AddArgument(id);
        AddOption(amount);
        AddOption(rarity);
        AddOption(drop);
        AddOption(rollMax);
        AddOption(enchant);
        AddOption(limitBreak);
        AddOption(sockets);
        AddOption(gems);
        AddOption(stats);
        AddOption(specials);
        AddOption(bonusClear);
        this.SetHandler<InvocationContext, int, int, int, bool, bool, int, int, int, int[], string[], string[], bool>(Handle,
            id, amount, rarity, drop, rollMax, enchant, limitBreak, sockets, gems, stats, specials, bonusClear);
    }

    // Every option of the item command: the spawning ones and the enhancement options the debug item browser sends.
    private void Handle(InvocationContext ctx, int itemId, int amount, int rarity, bool drop, bool rollMax,
                        int enchant, int limitBreak, int sockets, int[] gems, string[] stats, string[] specials, bool bonusClear) {
        if (session.Field is null) return;
        try {
            // No rarity given: the item's own, as every other source makes it (ItemDropManager.CreateItem). A fixed
            // rarity 1 made stackables of another rank (Chaos Onyx, rank 4) a row of their own that never stacks with
            // the ones from drops and rewards.
            rarity = rarity > 0 ? Math.Clamp(rarity, 1, MAX_RARITY) : -1;

            Item? firstItem = session.Field.ItemDrop.CreateItem(itemId, rarity, rollMax: rollMax);
            if (firstItem == null) {
                ctx.Console.Error.WriteLine($"Invalid Item: {itemId}");
                return;
            }

            // Every copy of a non-stackable item gets the same enhancements, not only the first.
            bool Enhance(Item item) => ApplyEnhancements(ctx, item, enchant, limitBreak, sockets, gems ?? Array.Empty<int>(),
                stats ?? Array.Empty<string>(), specials ?? Array.Empty<string>(), bonusClear);
            if (!Enhance(firstItem)) {
                ctx.ExitCode = 1;
                return;
            }

            if (firstItem.IsCurrency()) {
                firstItem.Amount = amount;
                ctx.ExitCode = GiveItem(ctx, firstItem) ? 0 : 1;
                return;
            }

            bool isNonStackable = firstItem.Metadata.Property.SlotMax == 1;

            if (drop) {
                if (isNonStackable) {
                    // Apply hard cap only when dropping non-stackable items
                    amount = Math.Clamp(amount, 1, 100);

                    session.Field.DropItem(session.Player, firstItem);
                    for (int i = 1; i < amount; i++) {
                        Item? additionalItem = session.Field.ItemDrop.CreateItem(itemId, rarity, rollMax: rollMax);
                        if (additionalItem == null) {
                            ctx.Console.Error.WriteLine($"Failed to create additional item {i + 1}/{amount}");
                            continue;
                        }
                        if (!Enhance(additionalItem)) {
                            continue;
                        }
                        session.Field.DropItem(session.Player, additionalItem);
                    }
                } else {
                    // Stackable items can be dropped as a single stack only up to SlotMax
                    firstItem.Amount = Math.Clamp(amount, 1, firstItem.Metadata.Property.SlotMax);
                    session.Field.DropItem(session.Player, firstItem);
                }
                ctx.ExitCode = 0;
                return;
            }

            if (isNonStackable) {
                int freeSlots = session.Item.Inventory.FreeSlots(firstItem.Inventory);
                if (freeSlots <= 0) {
                    session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
                    return;
                }
                if (!GiveItem(ctx, firstItem)) {
                    return;
                }

                amount = Math.Clamp(amount, 1, freeSlots);
                // don't need to recalculate free slots, since we are skipping one already
                for (int i = 1; i < amount; i++) {
                    Item? additionalItem = session.Field.ItemDrop.CreateItem(itemId, rarity, rollMax: rollMax);
                    if (additionalItem == null) {
                        ctx.Console.Error.WriteLine($"Failed to create additional item {i + 1}/{amount}");
                        continue;
                    }
                    if (!Enhance(additionalItem)) {
                        continue;
                    }
                    if (!GiveItem(ctx, additionalItem)) {
                        return;
                    }
                }
            } else {
                firstItem.Amount = amount;
                if (!GiveItem(ctx, firstItem)) {
                    return;
                }
            }


            ctx.ExitCode = 0;
        } catch (SystemException ex) {
            ctx.Console.Error.WriteLine(ex.Message);
            ctx.ExitCode = 1;
        }
    }

    // The enhancement options: a gear item only, each value checked against the server's own tables so an impossible
    // command is a chat line, never a broken item or a crash. The enchant's options come from the enchant table the
    // enchant manager uses (ItemEnchantManager.GetEnchant), the limit break's from the unlimited enchant table as the
    // manager applies them level by level, and a socket's gem must be one the client would offer (the gemstone upgrade
    // row fits the item's kind, as ItemSocketHandler.HandleEquipGemstone checks).
    private bool ApplyEnhancements(InvocationContext ctx, Item item, int enchant, int limitBreak, int sockets, int[] gems,
                                   string[] stats, string[] specials, bool bonusClear) {
        // A negative level or socket count is refused, not read as "not given" (only --socket -1 means the item's own).
        if (enchant < 0 || limitBreak < 0 || sockets < -1) {
            ctx.Console.Error.WriteLine("--enchant and --limit-break take 0 or more, --socket 0 or more (-1: the item's own).");
            return false;
        }
        bool wants = enchant > 0 || limitBreak > 0 || sockets >= 0 || gems.Length > 0 || stats.Length > 0 || specials.Length > 0 || bonusClear;
        if (!wants) {
            return true;
        }
        ItemMetadataProperty property = item.Metadata.Property;
        if (property.Type != 1) {
            ctx.Console.Error.WriteLine("--enchant, --limit-break, --socket, --gem, --stat, --special and --bonus-clear need a gear item (type 1).");
            return false;
        }
        if (enchant < 0 || enchant > ItemEnchantRules.MaxGrade) {
            ctx.Console.Error.WriteLine($"--enchant is 0..{ItemEnchantRules.MaxGrade}.");
            return false;
        }
        if (enchant > 0 && limitBreak > 0) {
            ctx.Console.Error.WriteLine("--enchant and --limit-break cannot be set together: a limit-broken item has no enchant.");
            return false;
        }
        if (enchant > 0) {
            if (!ItemEnchantRules.CanEnchant(session.ServerTableMetadata.EnchantLimitTable, property.Type, GearScoreRules.SubType(item.Id),
                    item.Metadata.Limit.Level, item.Metadata.Limit.EnableEnchant, enchant - 1, 0, item.ExpiryTime)) {
                ctx.Console.Error.WriteLine($"--enchant {enchant} is not possible for item {item.Id} (its slot, level or enchant limit refuses it).");
                return false;
            }
            ItemEnchant leveled = ItemEnchantManager.GetEnchant(session, item, enchant);
            if (leveled.Enchants != enchant) {
                ctx.Console.Error.WriteLine($"--enchant {enchant} has no row in the enchant table for item {item.Id} at rarity {item.Rarity}.");
                return false;
            }
            item.Enchant = leveled;
        }
        if (limitBreak > 0) {
            int max = property.LimitBreakMaxLevel;
            if (max <= 0) {
                ctx.Console.Error.WriteLine($"item {item.Id} has no limit break.");
                return false;
            }
            if (limitBreak > max) {
                ctx.Console.Error.WriteLine($"--limit-break {limitBreak} is past the item's maximum {max}.");
                return false;
            }
            // The game's limit break needs the item at its enchant maximum first (LimitBreakRules.IsItem), and keeps
            // that enchant object with Enchants 0 afterwards (ItemEnchantManager.LimitBreakEnchant); build the +15 the
            // enchant manager would have made, so the item carries the same enchant stats as a played one.
            if (!ItemEnchantRules.CanEnchant(session.ServerTableMetadata.EnchantLimitTable, property.Type, GearScoreRules.SubType(item.Id),
                    item.Metadata.Limit.Level, item.Metadata.Limit.EnableEnchant, ItemEnchantRules.MaxGrade - 1, 0, item.ExpiryTime)) {
                ctx.Console.Error.WriteLine($"--limit-break {limitBreak} needs an item that can reach +{ItemEnchantRules.MaxGrade} (its slot, level or enchant limit refuses it).");
                return false;
            }
            ItemEnchant? enchant15 = BuildEnchant(session, item, ItemEnchantRules.MaxGrade);
            if (enchant15 == null) {
                ctx.Console.Error.WriteLine($"the enchant table has no +{ItemEnchantRules.MaxGrade} row for item {item.Id} at rarity {item.Rarity}.");
                return false;
            }
            if (!session.ServerTableMetadata.UnlimitedEnchantOptionTable.Entries.TryGetValue(item.Type.Type, out Dictionary<int, UnlimitedEnchantOptionTable.Option>? levels)) {
                ctx.Console.Error.WriteLine($"the limit break table has no options for item {item.Id}'s slot {item.Type.Type}.");
                return false;
            }
            var limit = new ItemLimitBreak();
            for (int level = 1; level <= limitBreak; level++) {
                if (!levels.TryGetValue(level, out UnlimitedEnchantOptionTable.Option? option)) {
                    ctx.Console.Error.WriteLine($"the limit break table has no level {level} for item {item.Id}'s slot.");
                    return false;
                }
                foreach ((BasicAttribute attribute, int value) in option.Values) {
                    limit.BasicOptions[attribute] = limit.BasicOptions.TryGetValue(attribute, out BasicOption had)
                        ? had + new BasicOption(value) : new BasicOption(value);
                }
                foreach ((BasicAttribute attribute, float rate) in option.Rates) {
                    limit.BasicOptions[attribute] = limit.BasicOptions.TryGetValue(attribute, out BasicOption had)
                        ? had + new BasicOption(rate) : new BasicOption(rate);
                }
                foreach ((SpecialAttribute attribute, int value) in option.SpecialValues) {
                    limit.SpecialOptions[attribute] = limit.SpecialOptions.TryGetValue(attribute, out SpecialOption had)
                        ? had + new SpecialOption(value) : new SpecialOption(value);
                }
                foreach ((SpecialAttribute attribute, float rate) in option.SpecialRates) {
                    limit.SpecialOptions[attribute] = limit.SpecialOptions.TryGetValue(attribute, out SpecialOption had)
                        ? had + new SpecialOption(rate) : new SpecialOption(rate);
                }
            }
            limit.Level = limitBreak;
            item.Enchant = enchant15;
            item.Enchant.Enchants = 0;   // the game's limit-broken item: the +15 options stay, the level is 0
            item.LimitBreak = limit;
        }
        // The sockets the item's creation gave it: the itemsocket row of its socketDataId, or the same Lua rule
        // (earrings, necklaces and rings at level 50, grade 3 or more) the client applies without one.
        int maxSockets = Math.Min((int)(item.Socket?.MaxSlots ?? 0), MAX_SOCKET);
        if (sockets >= 0 || gems.Length > 0) {
            if (maxSockets <= 0) {
                ctx.Console.Error.WriteLine($"item {item.Id} has no sockets.");
                return false;
            }
            if (sockets < 0) sockets = Math.Min(gems.Length, maxSockets);
            if (sockets > maxSockets) {
                ctx.Console.Error.WriteLine($"--socket {sockets} is past the item's {maxSockets} sockets.");
                return false;
            }
            if (gems.Length > sockets) {
                ctx.Console.Error.WriteLine($"--gem has {gems.Length} ids for {sockets} unlocked socket(s).");
                return false;
            }
            var socket = new ItemSocket((byte)maxSockets, (byte)sockets);
            for (int i = 0; i < gems.Length; i++) {
                // 0 is an empty unlocked socket, so a later gem keeps its socket (the debug item browser sends one
                // id per unlocked socket).
                if (gems[i] == 0) continue;
                Item? gem = session.Field?.ItemDrop.CreateItem(gems[i]);
                if (gem == null || gem.Inventory != InventoryType.Gemstone) {
                    ctx.Console.Error.WriteLine($"--gem {gems[i]} is not a gem item.");
                    return false;
                }
                if (!session.TableMetadata.GemstoneUpgradeTable.Entries.TryGetValue(gem.Id, out GemstoneUpgradeTable.Entry? gemRow)
                    || !ItemSocketRules.GemFits(gemRow.EquipPart, ItemSocketRules.GemItemKind(property.SkinType, GearScoreRules.SubType(item.Id)))) {
                    ctx.Console.Error.WriteLine($"--gem {gems[i]} does not fit item {item.Id}'s sockets.");
                    return false;
                }
                socket.Sockets[i] = new ItemGemstone(gem.Id, null, (gem.Stats ?? ItemStats.Default).Clone());
            }
            item.Socket = socket;
        }
        if (bonusClear || stats.Length > 0 || specials.Length > 0) {
            item.Stats ??= new ItemStats();
            item.Stats[ItemStats.Type.Random] = new ItemStats.Option();
        }
        if (!ApplyOptionLines(ctx, item, stats, special: false)) return false;
        if (!ApplyOptionLines(ctx, item, specials, special: true)) return false;
        return true;
    }

    // The enchant the enchant manager's successful grades would have made: each grade's increment added to the last,
    // through the target, as ItemEnchantManager.ApplyEnchantStats does level by level. Null when a grade has no row.
    private static ItemEnchant? BuildEnchant(GameSession session, Item item, int level) {
        var enchant = new ItemEnchant();
        for (int target = 1; target <= level; target++) {
            ItemEnchant step = ItemEnchantManager.GetEnchant(session, item, target);
            if (step.Enchants != target) {
                return null;
            }
            foreach ((BasicAttribute attribute, BasicOption option) in step.BasicOptions) {
                enchant.BasicOptions[attribute] = enchant.BasicOptions.TryGetValue(attribute, out BasicOption existing)
                    ? existing + option : option;
            }
        }
        enchant.Enchants = level;
        return enchant;
    }

    // A client stat or special id and its value ("31=5"): the client's ids are what its tooltip and the browser show
    // (BasicAttributeIds.TryServerStat; a special id is the SpecialAttribute's value), and the value is the int the
    // client prints (a rate in tenths/ thousandths by the client's stat table, as every item option is).
    private static bool ApplyOptionLines(InvocationContext ctx, Item item, string[] lines, bool special) {
        foreach (string line in lines) {
            int eq = line.IndexOf('=');
            if (eq <= 0 || !int.TryParse(line.AsSpan(0, eq), out int id)
                || !int.TryParse(line.AsSpan(eq + 1), out int value) || value < 0 || value > 1_000_000) {
                ctx.Console.Error.WriteLine($"--{(special ? "special" : "stat")} {line}: write clientId=value, a value from 0 to 1000000.");
                return false;
            }
            if (special) {
                if (id <= 0 || id >= 0xcf || !Enum.IsDefined((SpecialAttribute) id)) {
                    ctx.Console.Error.WriteLine($"--special {line}: {id} is not a special ability id (1..{0xce}).");
                    return false;
                }
                item.Stats![ItemStats.Type.Random].Special[(SpecialAttribute) id] = new SpecialOption(value);
            } else {
                if (!BasicAttributeIds.TryServerStat(id, out BasicAttribute attribute)) {
                    ctx.Console.Error.WriteLine($"--stat {line}: {id} is not a client stat id (0..{BasicAttributeIds.ClientStatCount - 1}).");
                    return false;
                }
                item.Stats![ItemStats.Type.Random].Basic[attribute] = new BasicOption(value);
            }
        }
        return true;
    }

    private bool GiveItem(InvocationContext ctx, Item item) {
        if (session.Item.Inventory.Add(item, true)) return true;
        session.Item.Inventory.Discard(item);
        ctx.Console.Error.WriteLine($"Failed to add item:{item.Id} to inventory");
        ctx.ExitCode = 1;
        return false;
    }
}
