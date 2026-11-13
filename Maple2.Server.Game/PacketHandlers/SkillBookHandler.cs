using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

public class SkillBookHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestSkillBookTree;

    private enum Command : byte {
        Load = 0,
        Save = 1,
        Rename = 2,
        Expand = 4,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required BanWordStorage BanWordStorage { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Load:
                HandleLoad(session);
                return;
            case Command.Save:
                HandleSave(session, packet);
                return;
            case Command.Rename:
                HandleRename(session, packet);
                return;
            case Command.Expand:
                HandleExpand(session, packet);
                return;
        }
    }

    private void HandleLoad(GameSession session) {
        session.Config.Skill.LoadSkillBook();
    }

    private void HandleSave(GameSession session, IByteReader packet) {
        long activeSkillTab = packet.ReadLong();
        long savedSkillTab = packet.ReadLong();
        // The client's job rank number (0 every rank, 1 base job, 2 awakening; skillpointsimulator.cpp GetUsedPoints),
        // not the server's SkillRank. The reply echoes it: the client commits that rank of its simulator
        // (uiskillbookautodistributedialog.cpp RequestTree and the mode 1 reply).
        int clientRank = packet.ReadInt();
        SkillRank? rank = JobRules.ResetRank(clientRank);
        if (rank == null) {
            return;
        }
        SkillRank ranksToSave = rank.Value;

        // Switching Active Tab
        if (savedSkillTab == 0) {
            session.Config.Skill.SaveSkillTab(activeSkillTab, ranksToSave, clientRank);
            return;
        }

        int tabCount = packet.ReadInt();
        if (!SkillBookRules.TabCountAllowed(tabCount, Constant.MaxSkillTabCount)) {
            return;
        }
        for (int i = 0; i < tabCount; i++) {
            var skillTab = packet.ReadClass<SkillTab>();
            if (skillTab.Id != savedSkillTab) continue;

            session.Config.Skill.SaveSkillTab(activeSkillTab, ranksToSave, clientRank, skillTab);
            return;
        }
    }

    private void HandleRename(GameSession session, IByteReader packet) {
        (long skillTabId, string skillTabName) = SkillBookRules.ReadRename(packet);
        if (!SkillBookRules.NameAllowed(skillTabName)) {
            return;
        }
        if (SkillBookRules.NameBanned(skillTabName, BanWordStorage.ContainsBannedWord)) {
            session.Send(NoticePacket.MessageBox(StringCode.s_ban_check_err_any));
            return;
        }

        SkillTab? skillTab = session.Config.Skill.GetSkillTab(skillTabId);
        if (skillTab == null) {
            return;
        }

        skillTab.Name = skillTabName;
        session.Send(SkillBookPacket.Rename(skillTab));
    }

    private void HandleExpand(GameSession session, IByteReader packet) {
        (int dialogId, bool useCoupon) = SkillBookRules.ReadExpand(packet);
        if (dialogId != SkillBookRules.DialogId) {
            return;
        }

        session.Config.Skill.ExpandSkillTabs(useCoupon);
    }
}
