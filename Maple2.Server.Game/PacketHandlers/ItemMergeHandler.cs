using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemMergeHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemMerge;

    private enum Command : byte {
        Info = 0,
        Stage = 1,
        SelectCrystal = 2,
        Empower = 3,
        Choice = 4,
        Revert = 6,
        SelectMaterial = 7,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Info:
                HandleInfo(session);
                break;
            case Command.Stage:
                HandleStage(session, packet);
                break;
            case Command.SelectCrystal:
                HandleSelectCrystal(session, packet);
                break;
            case Command.Empower:
                HandleEmpower(session, packet);
                break;
            case Command.Choice:
                HandleChoice(session, packet);
                break;
            case Command.Revert:
                HandleRevert(session, packet);
                break;
            case Command.SelectMaterial:
                HandleSelectMaterial(session, packet);
                break;
        }
    }

    // Client UIItemMergeOptionDialog::RequestInfo, sent when the dialog has loaded (no payload).
    // The dialog takes no item until the answer arrives: nothing pending, or the merge result that
    // still waits for the player's choice.
    private static void HandleInfo(GameSession session) {
        session.ItemMerge.Info();
    }

    private void HandleStage(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();

        session.ItemMerge.Stage(itemUid);
    }

    private void HandleSelectCrystal(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        long catalystUid = packet.ReadLong();

        session.ItemMerge.SelectCrystal(itemUid, catalystUid);
    }

    private void HandleEmpower(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        long catalystUid = packet.ReadLong();

        session.ItemMerge.Empower(itemUid, catalystUid);

    }

    // Client UIItemMergeOptionDialog::RequestChoice: a bool, true to keep the new option.
    private void HandleChoice(GameSession session, IByteReader packet) {
        bool keepNew = packet.ReadBool();

        session.ItemMerge.Choose(keepNew);
    }

    // Client UIItemMergeRevertDialog: undo the merge options of an item with a revert item.
    // The revert item comes first, then the item to clear (uiitemmergerevertdialog.cpp:156).
    private void HandleRevert(GameSession session, IByteReader packet) {
        long revertItemUid = packet.ReadLong();
        long itemUid = packet.ReadLong();
        Logger.Debug("ItemMerge revert of {ItemUid} with {RevertItemUid} not implemented", itemUid, revertItemUid);
    }

    // Client UIItemMergeOptionDialog::RequestMaterial: the material picked for the item.
    private void HandleSelectMaterial(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        long materialUid = packet.ReadLong();
        Logger.Debug("ItemMerge material {MaterialUid} for {ItemUid} not implemented", materialUid, itemUid);
    }
}
