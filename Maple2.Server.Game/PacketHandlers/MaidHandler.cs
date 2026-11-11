using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

// The maid dialog's requests (UIMaidDialog, client/ms2/ui/uimaiddialog.cpp). Mode 4 saves the profile the owner fills
// in; modes 5-10 are the dialog's debug buttons, which the client shows in debug mode only, so they are for
// AdminPermissions.Debug. Each names the maid by its uid (the uid of the maid npc's FieldMaid).
public class MaidHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Maid;

    private enum Command : byte {
        Profile = 4,
        AffinityGrade = 5,
        AffinityUp = 6,
        AffinityReset = 7,
        MoodUp = 8,
        MoodDown = 9,
        Expiry = 10,
    }

    // The mood step of the debug buttons (the client sends none).
    private const int DebugMoodStep = 10;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    public required BanWordStorage BanWordStorage { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Profile:
                HandleProfile(session, packet);
                return;
            case Command.AffinityGrade:
            case Command.AffinityUp:
            case Command.AffinityReset:
            case Command.MoodUp:
            case Command.MoodDown:
            case Command.Expiry:
                HandleDebug(session, packet, command);
                return;
        }
    }

    private void HandleProfile(GameSession session, IByteReader packet) {
        long maidUid = packet.ReadLong();
        int npcObjectId = packet.ReadInt();
        var profile = new MaidProfile {
            Title = packet.ReadUnicodeString(),
            Name = packet.ReadUnicodeString(),
            BirthDay = packet.ReadUnicodeString(),
            Constellation = packet.ReadUnicodeString(),
            Height = packet.ReadUnicodeString(),
            Weight = packet.ReadUnicodeString(),
            Specialty = packet.ReadUnicodeString(),
            SpecialDetail = packet.ReadUnicodeString(),
            Like = packet.ReadUnicodeString(),
            Hate = packet.ReadUnicodeString(),
            Hobby = packet.ReadUnicodeString(),
            OwnerName = packet.ReadUnicodeString(),
            BelongTo = packet.ReadUnicodeString(),
        };
        if (MaidRules.ProfileTexts(profile).Any(BanWordStorage.ContainsBannedWord)) {
            return;
        }
        // The change and the save run on the field thread of the maid's field, which owns it: the maid is read and
        // the owner checked again there.
        FieldManager? field = session.Field;
        if (field?.GetMaid(maidUid) == null) {
            return;
        }
        field.Scheduler.Schedule(() => {
            Maid? maid = field.GetMaid(maidUid);
            if (maid == null) {
                return;
            }
            bool debug = session.Player.AdminPermissions.HasFlag(AdminPermissions.Debug);
            if (!MaidRules.CanEditProfile(maid, session.AccountId, debug)) {
                return;
            }
            maid.Profile = profile;
            if (field.SaveMaid(maid)) {
                int objectId = field.GetMaidNpcObjectId(maidUid);
                session.Send(UserMaidPacket.Property(objectId != 0 ? objectId : npcObjectId));
            }
        });
    }

    private void HandleDebug(GameSession session, IByteReader packet, Command command) {
        long maidUid = packet.ReadLong();
        byte flag = command == Command.Expiry ? packet.ReadByte() : (byte) 0;
        long seconds = command == Command.Expiry ? packet.ReadLong() : 0;
        if (!session.Player.AdminPermissions.HasFlag(AdminPermissions.Debug)) {
            return;
        }
        FieldManager? field = session.Field;
        if (field?.GetMaid(maidUid) == null) {
            return;
        }
        IReadOnlyDictionary<int, int> exps = TableMetadata.MaidTable.Exps ?? new Dictionary<int, int>();
        int affinityMax = Constants.MaidAffinityMax;
        field.Scheduler.Schedule(() => {
            Maid? maid = field.GetMaid(maidUid);
            if (maid == null || !session.Player.AdminPermissions.HasFlag(AdminPermissions.Debug)) {
                return;
            }
            MaidRules.ApplyDebug(maid, (MaidRules.DebugCommand) command, flag, seconds, exps, affinityMax, DebugMoodStep);
            field.SaveMaid(maid);
        });
    }
}
