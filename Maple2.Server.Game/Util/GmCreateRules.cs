using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// Server limits of the client's GM create windows (0x15 item, 0x24 NPC). The windows have none of
// their own, and a GM client can send any value.
public static class GmCreateRules {
    // The windows need the flag of the matching chat command on top of the GM check.
    public const AdminPermissions ItemPermission = AdminPermissions.SpawnItem;
    public const AdminPermissions NpcPermission = AdminPermissions.SpawnNpc;

    public const int MinRarity = 1;
    public const int MaxRarity = 6;
    // One request of a currency id (meso, meret, exp orb, ...) gives this much at most. The totals are
    // 64-bit, the packet's amount is a signed int: this keeps one click far from both ends and keeps an
    // exp orb from running the level-up loop over the whole table more than once.
    public const int MaxCurrencyAmount = 100_000_000;
    // NPCs per request, and GM-spawned NPCs alive in one field instance (window and `npc` command
    // together). Nothing removes them before the instance ends, so the total is what bounds the field.
    public const int MaxNpcPerRequest = 50;
    public const int MaxNpcPerField = 200;

    public enum ItemVerdict {
        Refuse, // bad amount or rank: nothing happens
        Full, // no room: inventory-full notice
        Currency, // Amount goes to the wallet
        Stack, // one stack of Amount
        Singles, // Amount items, one slot each
    }

    public readonly record struct ItemPlan(ItemVerdict Verdict, int Amount);

    // What a create request gives. rank outside 1-6 and amount <= 0 are refused, not clamped.
    public static ItemPlan PlanItem(int amount, int rarity, bool currency, int slotMax, int freeSlots) {
        if (amount <= 0 || rarity < MinRarity || rarity > MaxRarity) {
            return new ItemPlan(ItemVerdict.Refuse, 0);
        }
        if (currency) {
            return new ItemPlan(ItemVerdict.Currency, Math.Min(amount, MaxCurrencyAmount));
        }
        if (slotMax > 1) {
            return new ItemPlan(ItemVerdict.Stack, Math.Min(amount, slotMax));
        }
        if (freeSlots <= 0) {
            return new ItemPlan(ItemVerdict.Full, 0);
        }
        return new ItemPlan(ItemVerdict.Singles, Math.Min(amount, freeSlots));
    }

    // How many NPCs a request may spawn when `alive` GM-spawned NPCs stand in the field. 0 = refuse.
    public static int PlanNpc(int requested, int alive) {
        int room = MaxNpcPerField - Math.Max(0, alive);
        if (room <= 0) {
            return 0;
        }
        return Math.Min(Math.Clamp(requested, 1, MaxNpcPerRequest), room);
    }
}
