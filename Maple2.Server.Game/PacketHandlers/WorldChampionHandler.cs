using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;
using static Maple2.Model.Error.MigrationError;
using WorldTimeEventRequest = Maple2.Server.World.Service.TimeEventRequest;

namespace Maple2.Server.Game.PacketHandlers;

public class WorldChampionHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.WorldChampion;

    public const byte OpenMode = 8;
    public const byte MoveMode = 9;

    private static readonly ILogger Logger = Log.Logger.ForContext<WorldChampionHandler>();

    public override void Handle(GameSession session, IByteReader packet) {
        byte mode = packet.ReadByte();
        switch (mode) {
            // The menu entry and the world map icon ask for the running season (uiworldchampionrewarddialog.cpp:277-282).
            case OpenMode:
                HandleOpen(session);
                return;
            // The season window's move button, after the client's own check that the player is not on the season's
            // field yet (s_world_champion_msg_current_field, :371-378); no body.
            case MoveMode:
                HandleMove(session);
                return;
        }
    }

    // With the WorldChampionMushroom feature off the client has no menu entry (gameeventmanagercli.cpp:936), and the
    // server runs no season: the window (the world map icon still asks) shows the "no season" notice.
    // The season on with the step the World server counts (a step past the count: cleared); else the next season's
    // begin, or the "no season" notice. When the World server does not answer, the season on shows its first step.
    private static void HandleOpen(GameSession session) {
        if (!WorldChampionRules.On(session.ServerTableMetadata.FeatureTable)) {
            session.Send(WorldChampionPacket.NoSeason());
            return;
        }
        WorldChampionTable table = session.ServerTableMetadata.WorldChampionTable;
        (int Season, int Step) on = State(session) ?? (WorldChampionRules.Current(table, DateTime.Now)?.Id ?? 0, 1);
        if (on.Season != 0 && table.Entries.TryGetValue(on.Season, out WorldChampionTable.Season? season)) {
            session.Send(WorldChampionPacket.Season(season, on.Step));
            return;
        }
        if (WorldChampionRules.Next(table, DateTime.Now) is { } next) {
            session.Send(WorldChampionPacket.NextSeason(next.Begin));
            return;
        }
        session.Send(WorldChampionPacket.NoSeason());
    }

    private static void HandleMove(GameSession session) {
        WorldChampionTable.Season? season = WorldChampionRules.On(session.ServerTableMetadata.FeatureTable)
            ? WorldChampionRules.Current(session.ServerTableMetadata.WorldChampionTable, DateTime.Now) : null;
        if (season == null) {
            session.Send(WorldChampionPacket.NoSeason());
            return;
        }
        if (session.Field.MapId == season.MapId) {
            return;
        }
        session.Send(session.PrepareField(season.MapId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(s_move_err_default));
    }

    // At login, the reward mails of the seasons ended in the reward days (constants worldChampionRewardDays) the
    // character dealt the season's damage (rewardDamage) in: the items of the row of the last step all channels
    // cleared (KR's rows 20000001-20000016 hold items only), with the season's systemMailID. Each season's mail goes
    // once (MailWorldChampionReward claims it with the mail); a season with no step cleared mails nothing.
    public static void MailRewards(GameSession session) {
        if (!WorldChampionRules.On(session.ServerTableMetadata.FeatureTable) || session.Field == null) {
            return;
        }
        List<WorldChampionTable.Season> due = WorldChampionRules.RewardDue(session.ServerTableMetadata.WorldChampionTable, DateTime.Now,
            session.ServerTableMetadata.ConstantsTable.WorldChampionRewardDays).ToList();
        if (due.Count == 0) {
            return;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        foreach (WorldChampionTable.Season season in due) {
            int step = db.GetWorldChampionSeason(season.Id)?.Step ?? 1;
            int rewardId = WorldChampionRules.RewardId(season, step);
            if (rewardId == 0) {
                continue;
            }
            List<Item> items = session.RewardContentItems(rewardId);
            if (items.Count == 0) {
                Logger.Warning("World champion season {Season}: reward {RewardId} has no items to mail", season.Id, rewardId);
                continue;
            }
            var mail = new Mail {
                Type = MailType.System,
                ReceiverId = session.CharacterId,
                Content = season.SystemMailId.ToString(), // an id of string/systemmailcontent
            };
            foreach (Item item in items) {
                mail.Items.Add(item);
            }
            Mail? sent = db.MailWorldChampionReward(season.Id, session.CharacterId, season.RewardDamage, mail);
            if (sent == null) {
                continue;
            }
            try {
                session.World.MailNotification(new MailNotificationRequest {
                    CharacterId = session.CharacterId,
                    MailId = sent.Id,
                });
            } catch { /* ignored */ }
        }
    }

    // The world map icon of the season on (MapWorldBoss.WorldChampionType): the npc of the step the World server
    // counts (the first when it does not answer) on the season's field, channel 0 (no channel listed); none with the
    // feature off, no season on, or the season cleared.
    public static MapWorldBoss? MapIcon(GameSession session) {
        if (!WorldChampionRules.On(session.ServerTableMetadata.FeatureTable)) {
            return null;
        }
        WorldChampionTable.Season? season = WorldChampionRules.Current(session.ServerTableMetadata.WorldChampionTable, DateTime.Now);
        if (season == null) {
            return null;
        }
        (int Season, int Step)? state = State(session);
        if (state != null && state.Value.Season != season.Id) {
            return null;
        }
        WorldChampionTable.Step? step = WorldChampionRules.StepOf(season, state?.Step ?? 1);
        if (step == null) {
            return null;
        }
        return new MapWorldBoss(step.NpcId, season.MapId, 0, new DateTimeOffset(season.Begin).ToUnixTimeSeconds(), MapWorldBoss.WorldChampionType);
    }

    // The World server's season and step, without damage; null when it does not answer.
    private static (int Season, int Step)? State(GameSession session) {
        try {
            Maple2.Server.World.Service.TimeEventResponse response = session.World.TimeEvent(new WorldTimeEventRequest {
                WorldChampion = new WorldTimeEventRequest.Types.WorldChampion(),
            });
            if (response.Error != 0 || response.WorldChampionInfo == null) {
                return null;
            }
            return (response.WorldChampionInfo.Season, response.WorldChampionInfo.Step);
        } catch (Exception ex) {
            Logger.Warning(ex, "World champion state failed");
            return null;
        }
    }
}
