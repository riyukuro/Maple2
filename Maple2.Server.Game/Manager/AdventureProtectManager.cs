using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Manager;

// The adventurer protection state of the session's character (AdventureProtectRules): sent at the game server entry,
// and again when it changes (a level up, a quest completed, the character's age reached while online).
public sealed class AdventureProtectManager {
    private readonly GameSession session;
    private byte? sent;
    private bool ageScheduled;

    public AdventureProtectManager(GameSession session) {
        this.session = session;
    }

    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;

    public byte State {
        get {
            Character character = session.Player.Value.Character;
            int questId = Constants.AdventureProtectRequireQuest;
            bool questDone = questId > 0 && session.Quest.TryGetQuest(questId, out Quest? quest) && quest.State == QuestState.Completed;
            return AdventureProtectRules.State(character.Level, questDone, character.CreationTime, DateTime.Now.ToEpochSeconds(),
                Constants.AdventureProtectRequireLevel, questId, Constants.AdventureProtectCharCreateTime);
        }
    }

    public bool Cleared => State == AdventureProtectRules.Cleared;

    // The game server entry: the state as it is.
    public void Load() {
        Send(State);
    }

    // A level up, a quest completed, the age reached: the state again when it changed since the entry. Nothing
    // before the entry sent it.
    public void Update() {
        if (sent == null) {
            return;
        }
        byte state = State;
        if (state != sent) {
            Send(state);
        }
    }

    private void Send(byte state) {
        sent = state;
        session.Send(AdventureProtectPacket.State(state));
        if (ageScheduled) {
            return;
        }
        long remaining = AdventureProtectRules.AgeRemaining(session.Player.Value.Character.CreationTime, DateTime.Now.ToEpochSeconds(),
            Constants.AdventureProtectCharCreateTime);
        if (remaining > 0) {
            ageScheduled = true;
            session.Scheduler.Schedule(() => {
                ageScheduled = false;
                Update();
            }, TimeSpan.FromSeconds(remaining + 1));
        }
    }
}
