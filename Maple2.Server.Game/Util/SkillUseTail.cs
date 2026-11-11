using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers.Field;

namespace Maple2.Server.Game.Util;

// The end of the client's skill use request (Skill, sub-type 0), after the cooldown float:
// client tick, flag, item uid, cooldown key and, only when the client's HoldAttack feature (174) is on,
// the attach flag and, when the flag is set, the attach id and node name. The KR and NA client data turn the
// feature on; a client with it off ends the packet at the cooldown key.
public readonly record struct SkillUseTail(int ClientTick, bool Unknown, long ItemUid, long CooldownKey, bool IsHold, int HoldInt, string HoldString) {
    public static SkillUseTail Read(IByteReader packet) {
        int clientTick = packet.ReadInt();
        bool unknown = packet.ReadBool();
        long itemUid = packet.ReadLong();
        long cooldownKey = packet.ReadLong();
        bool isHold = packet.Remaining() > 0 && packet.ReadBool();
        int holdInt = 0;
        string holdString = string.Empty;
        if (isHold) {
            holdInt = packet.ReadInt();
            holdString = packet.ReadUnicodeString().Trim('\0');
        }

        return new SkillUseTail(clientTick, unknown, itemUid, cooldownKey, isHold, holdInt, holdString);
    }
}
