using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class SkillMacroPacket {
    private enum Command : byte {
        Load = 0,
        Init = 2,
    }

    // The answer to the macro dialog's load (c2s mode 0): the dialog reads mode 0 only
    // (UISkillCompactControlDialog::vfunc_79, uiskillcompactcontroldialog.cpp:500) and builds its tabs from it. Before
    // it the dialog has no tabs, and its close then saves an empty list.
    public static ByteWriter Load(ICollection<SkillMacro> skillMacros) {
        return Write(Command.Load, skillMacros);
    }

    // The tabs at login: CSkillSystem reads mode 2 only (CSkillSystem::FUN_141767ae0, skillslot.cpp:2530). Same body.
    public static ByteWriter Init(ICollection<SkillMacro> skillMacros) {
        return Write(Command.Init, skillMacros);
    }

    private static ByteWriter Write(Command command, ICollection<SkillMacro> skillMacros) {
        var pWriter = Packet.Of(SendOp.SkillMacro);
        pWriter.Write<Command>(command);
        pWriter.WriteInt(skillMacros.Count);
        foreach (SkillMacro macro in skillMacros) {
            pWriter.WriteClass<SkillMacro>(macro);
        }

        return pWriter;
    }
}
