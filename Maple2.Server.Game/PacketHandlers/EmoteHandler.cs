using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class EmoteHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Emote;

    private enum Command : byte {
        Learn = 1,
        Use = 2,
        Favorite = 4,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Learn:
                HandleLearn(session, packet);
                return;
            case Command.Use:
                HandleUse(session, packet);
                return;
            case Command.Favorite:
                HandleFavorite(session, packet);
                return;
        }
    }

    private void HandleLearn(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();

        Item? item = session.Item.Inventory.Get(itemUid);
        if (item?.Metadata.Skill == null || !item.IsEmote()) {
            session.Send(EmotePacket.Error(EmoteError.s_dynamic_action_item_invalid));
            return;
        }

        int emoteId = item.Metadata.Skill.Id;
        if (session.Player.Value.Unlock.Emotes.Contains(emoteId)) {
            session.Send(EmotePacket.Error(EmoteError.s_dynamic_action_already_learn));
            return;
        }

        // Now that we know this item is valid and is an unlearned emote, try to consume it.
        if (!session.Item.Inventory.Consume(itemUid, 1)) {
            session.Send(EmotePacket.Error(EmoteError.s_dynamic_action_item_invalid));
            return;
        }

        Unlock unlock = session.Player.Value.Unlock;
        unlock.LearnEmote(emoteId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        session.Send(EmotePacket.Learn(unlock.GetEmote(emoteId)));
    }

    // Client 2525: uidynamicactiondialog.cpp:356-371 / uicoupleactiondialog.cpp:320-336 ("onFavorite")
    // write u8=4, i32 emote id, i32 level, bool favorite. The client marks its own entry before it sends and
    // reads no answer; the mark is kept so the next emote list carries it. An emote not held takes no mark.
    private static void HandleFavorite(GameSession session, IByteReader packet) {
        int emoteId = packet.ReadInt();
        packet.ReadInt(); // level
        bool favorite = packet.ReadBool();
        session.Player.Value.Unlock.SetEmoteFavorite(emoteId, favorite);
    }

    private void HandleUse(GameSession session, IByteReader packet) {
        int emoteId = packet.ReadInt();
        string aniKey = packet.ReadUnicodeString();

        bool caught = session.Field?.HideAndSeek?.IsCaught(session.CharacterId) == true;
        if (!UseAllowed(emoteId, session.Player.Value.Unlock.Emotes.Contains(emoteId), session.Field?.MapId ?? 0, caught)) {
            return;
        }
        // The client sends the emote skill's own emotion name (its <kinds emotion>, stateskill.cpp:1906-1909, its
        // only Use sender), and the emotion condition counts that name; a use naming another emotion is dropped.
        if (session.SkillMetadata?.TryGet(emoteId, 1, out SkillMetadata? skill) == true && !KeyMatches(skill.Property.Emotion, aniKey)) {
            Logger.Debug("Dropped emote {EmoteId} sent with another emotion {AniKey}", emoteId, aniKey);
            return;
        }

        session.ConditionUpdate(ConditionType.emotion, codeString: aniKey);
        session.Buffs.TriggerEvent(session.Player, session.Player, session.Player, EventConditionType.OnEmote, skillId: emoteId);
    }

    // The two cheer emotions (90210001 Applaud, 90210002 Glowstick) are not learned: the client gives them to the
    // player only on the performance map (field 2000064) and to a hider caught in Hide and Seek (its Info "death" flag;
    // CSkillSystem::IsEmotionLearned). The server takes them there only, learned or not; every other emotion must be
    // learned.
    public static bool UseAllowed(int emoteId, bool learned, int mapId, bool caughtHider) {
        if (emoteId is ApplaudEmoteId or GlowstickEmoteId) {
            return mapId == Constant.PerformanceMapId || caughtHider;
        }
        return learned;
    }

    public static bool KeyMatches(string? emotion, string aniKey) => string.Equals(emotion ?? string.Empty, aniKey, StringComparison.OrdinalIgnoreCase);

    public const int ApplaudEmoteId = 90210001;
    public const int GlowstickEmoteId = 90210002;
}
