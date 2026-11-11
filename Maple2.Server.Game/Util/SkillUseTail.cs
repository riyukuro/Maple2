using Maple2.PacketLib.Tools;

namespace Maple2.Server.Game.Util;

// The end of the client's skill use request (Skill, sub-type 0), after the cooldown float:
// client tick, flag, item uid, cooldown key, attach flag and, when the flag is set, the attach id and node name.
public readonly record struct SkillUseTail(int ClientTick, bool Unknown, long ItemUid, long CooldownKey, bool IsHold, int HoldInt, string HoldString) {
    public static SkillUseTail Read(IByteReader packet) {
        int clientTick = packet.ReadInt();
        bool unknown = packet.ReadBool();
        long itemUid = packet.ReadLong();
        long cooldownKey = packet.ReadLong();
        bool isHold = packet.ReadBool();
        int holdInt = 0;
        string holdString = string.Empty;
        if (isHold) {
            holdInt = packet.ReadInt();
            holdString = packet.ReadUnicodeString().Trim('\0');
        }

        return new SkillUseTail(clientTick, unknown, itemUid, cooldownKey, isHold, holdInt, holdString);
    }
}
