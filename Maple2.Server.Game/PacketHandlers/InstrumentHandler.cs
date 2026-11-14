using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class InstrumentHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.PlayInstrument;

    private enum Command : byte {
        StartImprovise = 0,
        Improvise = 1,
        StopImprovise = 2,
        StartScore = 3,
        StopScore = 4,
        JoinEnsemble = 5,
        LeaveEnsemble = 6,
        ComposeScore = 8,
        ViewScore = 10,
        StartPerform = 11,
        EndPerform = 12,
        Stage = 13,
        Fireworks = 14,
        Emote = 15,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required BanWordStorage BanWordStorage { private get; init; }
    // ReSharper restore All
    #endregion

    // With MusicInstrument off the client uses no instrument or score item (InvenSlot, invenslot.cpp:803) and its
    // instrument manager does not start (instrumentmanager.cpp:258), so commands 0-10 come only from a forged client.
    // The concert stage (11-15, UIMusicConcertDialog) is sent without the check and stays open.
    public const string Feature = "MusicInstrument";

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        if (command < Command.StartPerform && !session.ServerTableMetadata.FeatureTable.IsOn(Feature)) {
            return;
        }
        switch (command) {
            case Command.StartImprovise:
                HandleStartImprovise(session, packet);
                return;
            case Command.Improvise:
                HandleImprovise(session, packet);
                return;
            case Command.StopImprovise:
                HandleStopImprovise(session);
                return;
            case Command.StartScore:
                HandleStartScore(session, packet);
                return;
            case Command.StopScore:
                HandleStopScore(session);
                return;
            case Command.JoinEnsemble:
                HandleJoinEnsemble(session, packet);
                return;
            case Command.LeaveEnsemble:
                HandleLeaveEnsemble(session);
                return;
            case Command.ComposeScore:
                HandleComposeScore(session, packet);
                return;
            case Command.ViewScore:
                HandleViewScore(session, packet);
                return;
            case Command.StartPerform:
                HandleStartPerform(session);
                return;
            case Command.EndPerform:
                HandleEndPerform(session);
                return;
            case Command.Stage:
                HandleEnterExitStage(session);
                return;
            case Command.Fireworks:
                HandleFireworks(session);
                return;
            case Command.Emote:
                HandleEmote(session, packet);
                return;
        }
    }

    private void HandleStartImprovise(GameSession session, IByteReader packet) {
        if (session.Field == null || session.Instrument != null) {
            return;
        }

        long itemUid = packet.ReadLong();
        if (!TryUseInstrument(session, itemUid, Environment.TickCount64, false, out session.Instrument)) {
            return;
        }

        session.Instrument.Improvising = true;
        session.Field.Broadcast(InstrumentPacket.StartImprovise(session.Instrument));
    }

    private void HandleImprovise(GameSession session, IByteReader packet) {
        if (session.Field == null || session.Instrument?.Improvising != true) {
            return;
        }

        int note = packet.ReadInt();
        // The client checks the row before every note (CInstrumentManager::NoteOn): one set mid-play stops the notes.
        if (MusicBlocked(session)) {
            return;
        }
        session.Field.Broadcast(InstrumentPacket.Improvise(session.Instrument, note), session);
    }

    private void HandleStopImprovise(GameSession session) {
        if (session.Field == null || session.Instrument?.Improvising != true) {
            return;
        }

        session.Field.Broadcast(InstrumentPacket.StopImprovise(session.Instrument));
        session.Field.RemoveInstrument(session.Instrument.ObjectId, broadcast: false);
        session.Instrument = null;
    }

    private void HandleStartScore(GameSession session, IByteReader packet) {
        if (session.Field == null || session.Instrument != null) {
            return;
        }

        long itemUid = packet.ReadLong();
        long scoreUid = packet.ReadLong();
        if (!TryGetScore(session, scoreUid, out Item? score) || IsBlank(score)) {
            return;
        }
        if (!TryUseInstrument(session, itemUid, Environment.TickCount64, false, out session.Instrument)) {
            return;
        }
        session.Instrument.Score = score;

        session.ConditionUpdate(ConditionType.music_play_score, codeLong: score.Id, targetLong: session.Field.MapId);

        score.RemainUses--;
        session.Field.Broadcast(InstrumentPacket.StartScore(session.Instrument, score));
        session.Send(InstrumentPacket.RemainUses(score.Uid, score.RemainUses));
    }

    // The code is the instrument's category, as for music_play_instrument_mastery (whose trophies name the
    // categories 1 to 30), not the instrument id (1 to 96).
    internal static void ReportPlayTime(GameSession session, InstrumentMetadata instrument, long playedMs) {
        if (session.Field == null) {
            return;
        }
        session.ConditionUpdate(ConditionType.music_play_instrument_time, counter: playedMs / 1000, targetLong: session.Field.MapId, codeLong: instrument.Category);
    }

    private void HandleStopScore(GameSession session) {
        // The client sends this for a score it plays (alone or in an ensemble); an improvisation ends with 2 and
        // gives nothing.
        if (session.Field == null || session.Instrument == null || session.Instrument.Improvising) {
            return;
        }

        Item? score = session.Instrument.Score;
        int masteryValue = score?.Metadata.Music?.MasteryValue ?? 1;
        int masteryValueMax = score?.Metadata.Music?.MasteryValueMax ?? 1;

        // The prestige exp comes with the exp (ExperienceManager.AddExp): the reason s_msg_take_play_instrument_exp reads the
        // server table's Exp_MusicMasteryLv1 row, 250 in KR, the same for all four levels.
        // The time counts up to the score's own length: a stop that comes late gives no more than the whole score,
        // and a score without a length gives nothing.
        int scoreLength = score?.Music?.Length > 0 ? score.Music.Length : score?.Metadata.Music?.PlayTime ?? 0;
        long totalTickTime = InstrumentRules.PlayedMs(Environment.TickCount64 - session.Instrument.StartTick, scoreLength);
        // A running MasteryReward event for music in this map adds its share of the whole gain, doubled or not.
        session.Mastery[MasteryType.Music] += EventSaleRules.MasteryWithEvent(
            InstrumentRules.Mastery(totalTickTime, masteryValue, masteryValueMax) * session.Mastery.DoubleFactor(SpecialAttribute.PlayInstrumentDoubleMastery),
            session.FindEvent(GameEventType.MasteryReward), MasteryType.Music, session.Field.MapId);

        short masteryLevel = session.Mastery.GetLevel(MasteryType.Music);
        ExpType expType = masteryLevel switch {
            1 => ExpType.musicMastery1,
            2 => ExpType.musicMastery2,
            3 => ExpType.musicMastery3,
            4 => ExpType.musicMastery4,
            _ => ExpType.musicMastery1,
        };

        // Subtracting 500ms to account for the delay between the client and server and exp abuse.
        // Modifier is the number of 500ms ticks that have passed.
        // The music special effects held (type 10): more exp, some for a whole score only.
        float ticks = InstrumentRules.ExpTicks(totalTickTime);
        bool wholeScore = scoreLength > 0 && totalTickTime >= scoreLength;
        float expFactor = ticks <= 0 ? 1 : SpecialEffectRules.ExpFactor(
            session.Buffs.TakeSpecialEffects(SpecialEffectRules.Music, effect => SpecialEffectRules.MusicApplies(effect, wholeScore)));
        session.Exp.AddExp(expType, ticks * expFactor);

        ReportPlayTime(session, session.Instrument.Value, totalTickTime);
        if (session.Instrument.Ensemble) {
            session.ConditionUpdate(ConditionType.play_ensenble_time, counter: totalTickTime / 1000, targetLong: session.Field.MapId, codeLong: session.Field.MapId);
        }

        session.Field.RemoveInstrument(session.Instrument.ObjectId);
        session.Instrument = null;
    }

    // The client puts its player in the ensemble wait (pc state 0x80) as it sends 5 (CInstrumentManager, the
    // request 5 writer, instrumentmanager.cpp:763), and only Instrument 6 (the ensemble was cancelled: dialog
    // mode 1, state 0x7d) or a start takes it out. So a refused request, and a leader whose own instrument does not
    // start, get 6; a player already playing gets nothing, as 6 would end the play's state.
    private void HandleJoinEnsemble(GameSession session, IByteReader packet) {
        if (session.Field == null || session.Instrument != null) {
            return;
        }

        long itemUid = packet.ReadLong();
        long scoreUid = packet.ReadLong();
        if (session.Party.Party == null || !TryGetScore(session, scoreUid, out Item? score) || IsBlank(score) || MusicBlocked(session)) {
            CancelReady(session);
            return;
        }

        session.EnsembleReady = true;
        session.StagedInstrumentItem = session.Item.Inventory.Get(itemUid, InventoryType.FishingMusic);
        session.StagedScoreItem = score;

        if (session.Party.Party.LeaderCharacterId != session.CharacterId) {
            return;
        }

        // Store tick to ensure all members instruments have the same start tick
        long startTick = Environment.TickCount64;
        var started = new List<GameSession>();
        foreach ((long characterId, PartyMember member) in session.Party.Party.Members) {
            // The staged score was checked when the member got ready; it may have been played out since.
            if (!session.FindSession(characterId, out GameSession? memberSession) ||
                memberSession.Field == null ||
                !memberSession.EnsembleReady ||
                memberSession.Instrument != null ||
                memberSession.StagedInstrumentItem == null ||
                memberSession.StagedScoreItem == null ||
                !TryGetScore(memberSession, memberSession.StagedScoreItem.Uid, out Item? stagedScore) ||
                stagedScore != memberSession.StagedScoreItem ||
                memberSession.Player.Value.Character.MapId != session.Player.Value.Character.MapId ||
                memberSession.Player.Value.Character.RoomId != session.Player.Value.Character.RoomId) {
                continue;
            }

            if (!TryUseInstrument(memberSession, memberSession.StagedInstrumentItem.Uid, startTick, true, out memberSession.Instrument)) {
                continue;
            }
            started.Add(memberSession);
        }

        // An ensemble needs two players: a leader started alone plays the score solo. StartScore's flag is the
        // client's ensemble mark (CInstrumentManager::OnPerformStart: pc state 0x81 with it, 0x7f, a solo play,
        // without), so the leader's client shows a solo play, and the play counts as one (music_play_score, no
        // ensemble condition, no play_ensenble_time at its stop).
        bool ensemble = started.Count >= 2;
        foreach (GameSession memberSession in started) {
            Item memberScore = memberSession.StagedScoreItem!;
            FieldInstrument instrument = memberSession.Instrument!;
            instrument.Ensemble = ensemble;
            // The score in play: a player who enters the field is sent it, and it gives its own mastery at the stop.
            instrument.Score = memberScore;
            // One start per ready: the next ensemble needs a new request of the member.
            memberSession.EnsembleReady = false;
            memberScore.RemainUses--;
            memberSession.Field!.Broadcast(InstrumentPacket.StartScore(instrument, memberScore));
            memberSession.Send(InstrumentPacket.RemainUses(memberScore.Uid, memberScore.RemainUses));
            if (ensemble) {
                memberSession.ConditionUpdate(ConditionType.music_play_ensemble);
                memberSession.ConditionUpdate(ConditionType.music_play_ensemble_in, codeLong: memberSession.Field.MapId, targetLong: memberSession.Field.MapId);
            } else {
                memberSession.ConditionUpdate(ConditionType.music_play_score, codeLong: memberScore.Id, targetLong: memberSession.Field.MapId);
            }
        }
        if (!started.Contains(session)) {
            CancelReady(session);
        }
    }

    private void HandleLeaveEnsemble(GameSession session) {
        if (session.Field == null) {
            return;
        }

        CancelReady(session);
    }

    private static void CancelReady(GameSession session) {
        session.EnsembleReady = false;
        session.StagedInstrumentItem = null;
        session.StagedScoreItem = null;
        session.Send(InstrumentPacket.LeaveEnsemble());
    }

    private void HandleComposeScore(GameSession session, IByteReader packet) {
        long scoreUid = packet.ReadLong();
        bool usable = TryGetScore(session, scoreUid, out Item? score) && score.Music != null;
        bool composed = usable && score!.Music!.AuthorId != 0;
        if (composed) {
            Logger.Warning("CustomMusicScore {Uid} has already been composed", score!.Uid);
        }

        int length = packet.ReadInt();
        var instrument = packet.Read<Instrument>();
        string title = packet.ReadUnicodeString();
        string mml = packet.ReadString();

        // A refusal answers with 17, which unlocks the client's score writer (it stays locked otherwise).
        // The client times the mml with the category's percussion (an unknown category is refused before it sends).
        int noteLengthMax = score?.Metadata.Music?.NoteLengthMax ?? 0;
        InstrumentMetadata? category = TableMetadata.InstrumentTable.Entries.Values.FirstOrDefault(entry => entry.Category == (int) instrument);
        ComposeResult? refusal = InstrumentRules.ComposeRefusal(usable, composed, title,
            BanWordStorage.ContainsBannedWord, mml, noteLengthMax, length, category?.PercussionId ?? 0);
        if (refusal == null && category == null) {
            refusal = ComposeResult.Retry;
        }
        if (refusal != null || score?.Music == null) {
            session.Send(InstrumentPacket.ComposeResult(refusal ?? ComposeResult.Close));
            return;
        }

        score.Music.Length = length;
        score.Music.Instrument = instrument;
        score.Music.Title = title;
        score.Music.Author = session.PlayerName;
        score.Music.AuthorId = session.AccountId;
        score.Music.Mml = mml;

        session.Send(InstrumentPacket.ComposeScore(score));
    }

    private void HandleViewScore(GameSession session, IByteReader packet) {
        long scoreUid = packet.ReadLong();
        Item? score = session.Item.Inventory.Get(scoreUid, InventoryType.FishingMusic);
        if (score?.Music == null || score.Music.AuthorId == 0) {
            return;
        }

        session.Send(InstrumentPacket.ViewScore(score.Uid, score.Music.Mml));
    }

    private void HandleStartPerform(GameSession session) => session.Field?.PerformanceStage?.StartPerformance(session);

    private void HandleEndPerform(GameSession session) => session.Field?.PerformanceStage?.EndPerformance(session);

    private void HandleEnterExitStage(GameSession session) => session.Field?.PerformanceStage?.EnterExitStage(session);

    private void HandleFireworks(GameSession session) {
        if (session.Field?.PerformanceStage is null) {
            return;
        }

        // Any player in the concert dialog may send it (the client throttles it to one per 2 s); every client in
        // the field shows the effect over the sender (CInstrumentManager::OnPacket 14 picks the kind itself).
        session.Field.Broadcast(InstrumentPacket.Fireworks(session.Player.ObjectId));
    }

    private void HandleEmote(GameSession session, IByteReader packet) {
        if (session.Field?.MapId != Constant.PerformanceMapId) {
            return;
        }

        int skillId = packet.ReadInt();
        switch (skillId) {
            case 90210001: // Applaud
                break;
            case 90210002: // Glowstick
                break;
        }
    }

    private bool TryUseInstrument(GameSession session, long itemUid, long startTick, bool ensemble, [NotNullWhen(true)] out FieldInstrument? fieldInstrument) {
        Item? instrument = session.Item.Inventory.Get(itemUid, InventoryType.FishingMusic);
        if (instrument == null || instrument.Metadata.Function?.Type != ItemFunction.OpenInstrument || session.Field is null ||
            !InstrumentRules.IsPlayable(instrument.ExpiryTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) || MusicBlocked(session)) {
            fieldInstrument = null;
            return false;
        }

        if (!int.TryParse(instrument.Metadata.Function.Parameters, out int instrumentId)) {
            Logger.Warning("Invalid parameters for OpenInstrument:{Params}", instrument.Metadata.Function.Parameters);
            fieldInstrument = null;
            return false;
        }

        if (!TableMetadata.InstrumentTable.Entries.TryGetValue(instrumentId, out InstrumentMetadata? metadata)) {
            fieldInstrument = null;
            return false;
        }

        fieldInstrument = session.Field.SpawnInstrument(session.Player, metadata);
        fieldInstrument.StartTick = startTick;
        fieldInstrument.Ensemble = ensemble;
        return true;
    }

    // A home whose music row stops the player (HomePermissionRules.Blocks): the client sends no play there
    // (CInstrumentManager, the FUN_141bdb660(3) check before 0, 3 and 5), so the server refuses one too.
    internal static bool MusicBlocked(GameSession session) => HomeRowCheck.Blocks(session, HomePermission.Music);

    private static bool IsBlank(Item score) => InstrumentRules.IsBlankScore(score.Metadata.Music?.IsCustomNote == true, score.Music?.Author);

    private bool TryGetScore(GameSession session, long scoreUid, [NotNullWhen(true)] out Item? score) {
        score = session.Item.Inventory.Get(scoreUid, InventoryType.FishingMusic);
        if (score is not { RemainUses: > 0 } || score.IsExpired()) {
            return false;
        }

        return true;
    }
}
