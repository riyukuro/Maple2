using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// Client reader: CCharacterAbilityManager::OnCharacterAbility (client/ms2/uicharacterabilitydialog.cpp:206).
public static class CharacterAbilityPacket {
    private enum Command : byte {
        Load = 0,
        Reset = 1,
        Learn = 2,
    }

    // Replaces the learned list. resetTime: the time of the last reset in seconds, 0 = never.
    // bonusPoints: points on top of the level and default ones.
    public static ByteWriter Load(IReadOnlyCollection<int> abilityIds, long resetTime, int bonusPoints) {
        var pWriter = Packet.Of(SendOp.CharacterAbility);
        pWriter.WriteByte((byte) Command.Load);
        pWriter.WriteInt(abilityIds.Count);
        foreach (int id in abilityIds) {
            pWriter.WriteInt(id);
        }
        pWriter.WriteLong(resetTime);
        pWriter.WriteInt(bonusPoints);

        return pWriter;
    }

    // No body; the client does nothing with it. The emptied list comes with Load.
    public static ByteWriter Reset() {
        var pWriter = Packet.Of(SendOp.CharacterAbility);
        pWriter.WriteByte((byte) Command.Reset);

        return pWriter;
    }

    public static ByteWriter Learn(int abilityId) {
        var pWriter = Packet.Of(SendOp.CharacterAbility);
        pWriter.WriteByte((byte) Command.Learn);
        pWriter.WriteInt(abilityId);

        return pWriter;
    }
}
