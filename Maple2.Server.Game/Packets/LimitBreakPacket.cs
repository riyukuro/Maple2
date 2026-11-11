using Maple2.Model.Error;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class LimitBreakPacket {
    private enum Command : byte {
        StageItem = 0,
        LimitBreak = 1,
        Error = 2,

    }

    // UIUnlimitedEnchantDialog preview (uiunlimitedenchantdialog.cpp:223): item uid, the ingredient container
    // (commonstruct_wa.cpp:255), then the result item. There is no meso field; the client works out its fee
    // text from the ingredients.
    public static ByteWriter StageItem(Item item, Item upgradeItem, ICollection<IngredientInfo> catalysts) {
        var pWriter = Packet.Of(SendOp.LimitBreak);
        pWriter.Write<Command>(Command.StageItem);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteByte((byte) catalysts.Count);

        foreach (IngredientInfo ingredient in catalysts) {
            pWriter.WriteInt(0);
            pWriter.WriteInt(ItemTagMap.ToClient(ingredient.Tag));
            pWriter.WriteInt(ingredient.Amount);
        }

        pWriter.WriteClass<Item>(upgradeItem);
        return pWriter;
    }

    public static ByteWriter LimitBreak(Item item) {
        var pWriter = Packet.Of(SendOp.LimitBreak);
        pWriter.Write<Command>(Command.LimitBreak);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    public static ByteWriter Error(LimitBreakError error) {
        var pWriter = Packet.Of(SendOp.LimitBreak);
        pWriter.Write<Command>(Command.Error);
        pWriter.Write<LimitBreakError>(error);

        return pWriter;
    }
}
