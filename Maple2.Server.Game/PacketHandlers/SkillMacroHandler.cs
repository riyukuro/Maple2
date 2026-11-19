using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class SkillMacroHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.SkillMacro;

    private enum Command : byte {
        Load = 0,
        Update = 1,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();

        switch (command) {
            case Command.Load:
                HandleLoad(session);
                return;
            case Command.Update:
                HandleUpdate(session, packet);
                return;
        }
    }

    private void HandleLoad(GameSession session) {
        session.Config.LoadMacros();
    }

    private void HandleUpdate(GameSession session, IByteReader packet) {
        int count = packet.ReadInt();
        if (!SkillMacroRules.IsTabCount(count)) {
            return;
        }

        // Nothing is stored unless every tab passes what the client's dialog itself keeps to.
        var macros = new List<SkillMacro>();
        for (int i = 0; i < count; i++) {
            string name = packet.ReadUnicodeString();
            long keyId = packet.ReadLong();
            int skillCount = packet.ReadInt();
            if (!SkillMacroRules.IsNewName(name, macros.Select(macro => macro.Name).ToList())
                || !SkillMacroRules.IsSkillCount(skillCount, packet.Available)) {
                return;
            }

            var skills = new HashSet<int>();
            int previous = 0;
            for (int j = 0; j < skillCount; j++) {
                int skillId = packet.ReadInt();
                if (!SkillMacroRules.IsNextSkill(previous, skillId)) {
                    return;
                }
                skills.Add(skillId);
                previous = skillId;
            }
            macros.Add(new SkillMacro(name, keyId, skills));
        }

        session.Config.UpdateMacros(macros);
    }
}
