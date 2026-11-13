using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// The GM item window's repeat tool (CCCompareItemPacket): the create window's request plus a repeat count. The server
// rolls the item's options that many times and sends them back through 0x101 ItemOptionSimulator; the client saves them
// to a file. GM only. No item is given.
public class ItemRepeatHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemRepeat;

    public override void Handle(GameSession session, IByteReader packet) {
        if (!GmCheck.Allow(session, OpCode, GmCreateRules.ItemPermission)) {
            return;
        }

        GmItemCreateRequest request = GmItemCreateRequest.Read(packet);
        int repeat = packet.ReadInt();
        if (session.Field == null) {
            return;
        }

        int rolls = GmCreateRules.PlanRepeat(repeat, request.Rarity);
        if (rolls == 0) {
            Logger.Warning("ItemRepeat: repeat {Repeat} rank {Rarity} refused, account {AccountId}", repeat, request.Rarity, session.AccountId);
            return;
        }

        Item? item = session.Field.ItemDrop.CreateItem(request.ItemId, request.Rarity);
        if (item == null) {
            Logger.Warning("ItemRepeat: unknown item {ItemId} from account {AccountId}", request.ItemId, session.AccountId);
            return;
        }

        session.Send(ItemOptionSimulatorPacket.Start());
        var batch = new List<ItemStats>(GmCreateRules.RepeatBatch);
        for (int i = 0; i < rolls; i++) {
            // The first roll is the one CreateItem made; an item with no option data rolls empty blocks.
            ItemStats? stats = i == 0 ? item.Stats : session.Field.ItemStatsCalc.GetStats(item);
            batch.Add(stats ?? ItemStats.Default);
            if (batch.Count == GmCreateRules.RepeatBatch) {
                session.Send(ItemOptionSimulatorPacket.Options(item.Id, item.Rarity, batch));
                batch.Clear();
            }
        }
        if (batch.Count > 0) {
            session.Send(ItemOptionSimulatorPacket.Options(item.Id, item.Rarity, batch));
        }
        session.Send(ItemOptionSimulatorPacket.End());
    }
}
