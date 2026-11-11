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

    // Client: CSkillSystem::FUN_141767ae0 reads mode 2 only, so the answer to the dialog's request is mode 2 too.
    public static ByteWriter Load(ICollection<SkillMacro> skillMacros) {
        return Init(skillMacros);
    }

    public static ByteWriter Init(ICollection<SkillMacro> skillMacros) {
        var pWriter = Packet.Of(SendOp.SkillMacro);
        pWriter.Write<Command>(Command.Init);
        pWriter.WriteInt(skillMacros.Count);
        foreach (SkillMacro macro in skillMacros) {
            pWriter.WriteClass<SkillMacro>(macro);
        }

        return pWriter;
    }


}
