using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;
using static Maple2.Server.World.Service.World;

namespace Maple2.Server.Game.PacketHandlers;

public class HomeBankHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestHomeBank;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        byte kind = packet.ReadByte();
        long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var home = session.Field as HomeFieldManager;
        bool premium = HomeConvenienceRules.IsPremium(session.Player.Value.Account.PremiumTime, time);
        if (!HomeConvenienceRules.IsBankKindAllowed(kind, premium, home != null && home.OwnerId == session.AccountId)) {
            return;
        }
        // Both buttons share the one cooltime: the client checks it before either request and stores the answer's time.
        if (!HomeConvenienceRules.IsReady(session.Player.Value.Character.StorageCooldown, Constants.HomeBankCallCooltime, time)) {
            return;
        }

        session.Player.Value.Character.StorageCooldown = time;
        session.Send(HomeBank(time));
    }

    private static ByteWriter HomeBank(long time) {
        var pWriter = Packet.Of(SendOp.HomeBank);
        pWriter.WriteLong(time);

        return pWriter;
    }
}
