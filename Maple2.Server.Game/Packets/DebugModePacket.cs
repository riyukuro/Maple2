using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// 0x87 DebugMode: what the client's debug windows read. Sent to GM accounts only.
// Client: UINpcControlDialog (uinpccontroldialog.cpp:165, kinds 5 to 8), UIDebugGuideDialog::OnPacket
// (uidebugguidedialog.cpp:296, kind 7), UIDialogCenterDialog (uidialogcenterdialog.cpp:285, kind 10).
public static class DebugModePacket {
    private const byte NPC_STATE = 5;
    private const byte NPC_SKILLS = 6;
    private const byte GUIDE_COMMANDS = 7;

    // Kind 5, the text of the NPC control window: int object id (dropped unless it is the window's NPC), the
    // current node, the headline, long target object id, bool tracking, int count x (key, int value), then
    // six longs the window labels PAP, MAP, NDD, CAP, HP and MHP.
    public static ByteWriter NpcState(int objectId, string node, string headline, long targetId, bool tracking,
                                      IReadOnlyList<(string Key, int Value)> extra, long physicalAtk, long magicalAtk, long defense, long criticalRate,
                                      long health, long maxHealth) {
        var pWriter = Packet.Of(SendOp.DebugMode);
        pWriter.WriteByte(NPC_STATE);
        pWriter.WriteInt(objectId);
        pWriter.WriteUnicodeString(node);
        pWriter.WriteUnicodeString(headline);
        pWriter.WriteLong(targetId);
        pWriter.WriteBool(tracking);
        pWriter.WriteInt(extra.Count);
        foreach ((string key, int value) in extra) {
            pWriter.WriteUnicodeString(key);
            pWriter.WriteInt(value);
        }
        pWriter.WriteLong(physicalAtk);
        pWriter.WriteLong(magicalAtk);
        pWriter.WriteLong(defense);
        pWriter.WriteLong(criticalRate);
        pWriter.WriteLong(health);
        pWriter.WriteLong(maxHealth);

        return pWriter;
    }

    // Kind 6, the skill list of the NPC control window: int count x (int skill id, int level).
    public static ByteWriter NpcSkills(IReadOnlyList<(int Id, int Level)> skills) {
        var pWriter = Packet.Of(SendOp.DebugMode);
        pWriter.WriteByte(NPC_SKILLS);
        pWriter.WriteInt(skills.Count);
        foreach ((int id, int level) in skills) {
            pWriter.WriteInt(id);
            pWriter.WriteInt(level);
        }

        return pWriter;
    }

    // Kind 7, read while the debug guide window exists: the name of who pressed the button, int count x
    // (command, argument). The client enters each line in its chat as if typed and prints who ran it.
    public static ByteWriter GuideCommands(string senderName, IReadOnlyList<(string Command, string Argument)> lines) {
        var pWriter = Packet.Of(SendOp.DebugMode);
        pWriter.WriteByte(GUIDE_COMMANDS);
        pWriter.WriteUnicodeString(senderName);
        pWriter.WriteInt(lines.Count);
        foreach ((string command, string argument) in lines) {
            pWriter.WriteUnicodeString(command);
            pWriter.WriteUnicodeString(argument);
        }

        return pWriter;
    }
}
