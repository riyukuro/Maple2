using Maple2.Model;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class TutorialHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Tutorial;

    public override void Handle(GameSession session, IByteReader packet) {
        // CGuideActionClearTutorial: a guide step that clears the tutorial sends this with no fields.
        // The clear opens the job's tutorialClearOpenMaps and tutorialClearOpenTaxis (see TutorialRules); the newly
        // visited maps go to the client with FindFields. The client has no packet that adds a taxi station
        // mid-session, so the opened taxi shows from the next ServerEnter.
        Logger.Debug("Tutorial cleared by {CharacterId}", session.CharacterId);
        if (session.Field is null) {
            return;
        }
        Player player = session.Player.Value;
        if (!session.TableMetadata.JobTable.Entries.TryGetValue(player.Character.Job.Code(), out JobTable.Entry? entry)) {
            return;
        }
        JobTable.Tutorial tutorial = entry.Tutorial;
        List<int> added = TutorialRules.Clear(tutorial.StartField, session.Field.MapId, tutorial.OpenMaps, tutorial.OpenTaxis,
            player.Unlock.Maps, player.Unlock.Taxis);
        if (added.Count > 0) {
            session.Send(FindFieldsPacket.Add(added));
        }
    }
}
