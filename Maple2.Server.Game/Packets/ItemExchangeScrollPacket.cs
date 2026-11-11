using Maple2.Model.Error;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class ItemExchangeScrollPacket {
    private enum Command : byte {
        SetTarget = 0,
        Error = 2,
        RefreshIngredients = 3,
    }

    // Client: UIItemExchangeScrollDialog::UpdateIngredientCounts (uiitemexchangescrolldialog.cpp:518): no
    // fields; the dialog counts its ingredients again. The reply to the GM fill buttons.
    public static ByteWriter RefreshIngredients() {
        var pWriter = Packet.Of(SendOp.ItemExchange);
        pWriter.Write<Command>(Command.RefreshIngredients);

        return pWriter;
    }

    // Client: UIItemExchangeScrollDialog::FUN_140f52ca0 (uiitemexchangescrolldialog.cpp:482), the reply to an
    // item dropped on the scroll's target slot (c2s kind 0): long scroll uid, long target item uid, then
    // CIngredientContainer (commonfiles/commonstruct_wa.cpp:255): byte count, count x (int item id, int tag,
    // int count). The dialog shows these as the cost for that target and keeps the target uid.
    public static ByteWriter SetTarget(long scrollUid, long targetUid, IReadOnlyList<ItemComponent> ingredients) {
        var pWriter = Packet.Of(SendOp.ItemExchange);
        pWriter.Write<Command>(Command.SetTarget);
        pWriter.WriteLong(scrollUid);
        pWriter.WriteLong(targetUid);
        pWriter.WriteByte((byte) ingredients.Count);
        foreach (ItemComponent ingredient in ingredients) {
            pWriter.WriteInt(ingredient.ItemId); // 0 when the ingredient is named by its tag
            pWriter.WriteInt(ItemTagMap.ToClient(ingredient.Tag));
            pWriter.WriteInt(ingredient.Amount);
        }

        return pWriter;
    }

    public static ByteWriter Error(ItemExchangeScrollError error) {
        var pWriter = Packet.Of(SendOp.ItemExchange);
        pWriter.Write<Command>(Command.Error);
        pWriter.Write<ItemExchangeScrollError>(error);

        return pWriter;
    }
}
