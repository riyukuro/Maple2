using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class SystemShopHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.SystemShop;

    private enum Command : byte {
        Arena = 3,
        Fishing = 4,
        Mentee = 6,
        Mentor = 7,
        Item = 10,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Arena:
                HandleArena(session, packet);
                break;
            case Command.Fishing:
                HandleFishing(session, packet);
                break;
            case Command.Mentee:
                HandleMentee(session, packet);
                break;
            case Command.Mentor:
                HandleMentor(session, packet);
                break;
            case Command.Item:
                HandleItem(session, packet);
                break;
            default:
                HandleOther(session, packet, command);
                break;
        }
    }

    private void HandleArena(GameSession session, IByteReader packet) {
        bool openShop = packet.ReadBool();
        if (!openShop) {
            session.Shop.ClearActiveShop();
            return;
        }

        if (!session.NpcMetadata.TryGet(Constants.SystemShopNPCIDHonorToken, out NpcMetadata? npc)) {
            return;
        }

        session.Shop.Load(npc.Basic.ShopId, Constants.SystemShopNPCIDHonorToken);
        session.Send(SystemShopPacket.Arena());
    }

    private void HandleFishing(GameSession session, IByteReader packet) {
        bool openShop = packet.ReadBool();
        if (!openShop) {
            session.Shop.ClearActiveShop();
            return;
        }
        if (!session.NpcMetadata.TryGet(Constants.SystemShopNPCIDFishing, out NpcMetadata? npc)) {
            return;
        }

        session.Shop.Load(npc.Basic.ShopId, npc.Id);
        session.Send(SystemShopPacket.Fishing());
    }

    private void HandleMentee(GameSession session, IByteReader packet) {
        bool openShop = packet.ReadBool();
        if (!openShop) {
            session.Shop.ClearActiveShop();
            return;
        }

        if (!session.NpcMetadata.TryGet(Constants.SystemShopNPCIDMentee, out NpcMetadata? npc)) {
            return;
        }

        session.Shop.Load(npc.Basic.ShopId, npc.Id);
        session.Send(SystemShopPacket.Mentee());
    }

    private void HandleMentor(GameSession session, IByteReader packet) {
        bool openShop = packet.ReadBool();
        if (!openShop) {
            session.Shop.ClearActiveShop();
            return;
        }

        if (!session.NpcMetadata.TryGet(Constants.SystemShopNPCIDMentor, out NpcMetadata? npc)) {
            return;
        }

        session.Shop.Load(npc.Basic.ShopId, npc.Id);
        session.Send(SystemShopPacket.Mentor());
    }

    private void HandleItem(GameSession session, IByteReader packet) {
        bool openShop = packet.ReadBool();
        if (!openShop) {
            session.Shop.ClearActiveShop();
            return;
        }

        // An item id, not a held item: the bag sends a shop item it holds (invenslot.cpp:855-866), but the Meret Market
        // home product's Coin Shop button (uimeratmarketdialog.cpp:4145) and the gacha box dialog (uiitemgachaboxdialog.cpp:422)
        // send the gacha's coin item, which the player need not hold. The shop is the item's own (CItemData::GetShopId).
        int itemId = packet.ReadInt();
        if (!session.ItemMetadata.TryGet(itemId, out ItemMetadata? metadata) || metadata.Property.ShopId == 0) {
            return;
        }

        session.Shop.Load(metadata.Property.ShopId);
        session.Send(SystemShopPacket.Item());
    }

    // Client CNetworkService::SendSystemShop (networkservice.cpp:530) writes the shop kind
    // (uishopdialog.cpp:713 sort kind, unit_141347b10.cpp:490 tab) and 1 to open or 0 to close.
    // The fork knows no NPC for the other kinds; closing still clears the active shop.
    private void HandleOther(GameSession session, IByteReader packet, Command kind) {
        bool openShop = packet.ReadBool();
        if (!openShop) {
            session.Shop.ClearActiveShop();
            return;
        }
        Logger.Debug("System shop kind {Kind} opened by {CharacterId}: no shop for this kind", (byte) kind, session.CharacterId);
    }
}
