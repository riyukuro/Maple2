using Maple2.Database.Storage;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

// A character's rank duel record as of now: the stored record, started over when the PvP season changed.
public static class RankDuelStanding {
    public static int CurrentSeason(TableMetadataStorage tableMetadata) {
        return RankDuelRules.SeasonAt(tableMetadata.SeasonDataTable.Pvp, DateTime.Now);
    }

    public static RankDuelRecord Load(GameStorage.Request db, long characterId, int season) {
        RankDuelRecord record = db.GetRankDuelRecord(characterId);
        record.StartSeason(season);
        return record;
    }

    // Pvp 22 and 23, as KMS2 sends them at each game server entry.
    public static void Send(GameSession session) {
        RankDuelRecord record;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            record = Load(db, session.CharacterId, CurrentSeason(session.TableMetadata));
        }
        session.Send(PvpPacket.RankDuelOwnRecord(record));
        session.Send(PvpPacket.RankDuelRecords(record));
    }

    public static int Rating(GameSession session) {
        using GameStorage.Request db = session.GameStorage.Context();
        return Load(db, session.CharacterId, CurrentSeason(session.TableMetadata)).Rating;
    }
}
