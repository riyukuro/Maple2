using System.Collections.Generic;
using Maple2.Database.Model.Ranking;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Packet;

namespace Maple2.Server.Web.Controllers;

// The answers to the boss boards of irrq.aspx. The ranking dialog's boss boards (7, 8, 9, 0xf and 0x11) have one
// tab per dungeon room with a BossRanking, and the request's ranking id is that BossRanking. Each tab has three
// modes: the clears per character (0x48, the own rank 0x47), the first clears (0x51) and the fastest clears (0x5b).
// The rows come from the clears the game kept (boss-clear); no season is kept.
public static class BossRanking {
    // 0x48 (with a search name, the row of that character, none when it has no clear), 0x51 and 0x5b.
    public static ByteWriter Board(GameStorage.Request db, GameRankingType type, int rankingId, string userName) {
        switch (type) {
            case GameRankingType.BossFirstClears:
                return InGameRankPacket.BossClears(type, db.GetBossFirstClears(rankingId));
            case GameRankingType.BossShortestClears:
                return InGameRankPacket.BossClears(type, db.GetBossShortestClears(rankingId));
        }
        if (string.IsNullOrEmpty(userName)) {
            return InGameRankPacket.BossClearCount(GameRankingType.BossClearCount, rankingId, db.GetBossClearCounts(rankingId));
        }

        BossClearCountRankInfo? info = db.GetBossClearCount(rankingId, userName);
        return InGameRankPacket.BossClearCount(GameRankingType.BossClearCount, rankingId, info != null ? new List<BossClearCountRankInfo> { info } : []);
    }

    // 0x47: the character's rank on the clear count board, 0 without a clear.
    public static ByteWriter Personal(GameStorage.Request db, int rankingId, long characterId) {
        return InGameRankPacket.PersonalRank(GameRankingType.BossClearCountPersonal, db.GetBossClearCount(rankingId, characterId)?.Rank ?? 0);
    }

    // The world boss board (0xb) has one tab per world boss room with a BossRanking, from the same kept clears (a
    // world boss room's clear also keeps the party's damage to its bosses): the clears per character (0x70, the own
    // rank 0x6f), the first clears (0x79, the party damage as their point) and the clears with the most damage
    // (0x84, the own rank 0x83: the place of the character's best clear).
    public static ByteWriter WorldBossBoard(GameStorage.Request db, GameRankingType type, int rankingId, string userName) {
        switch (type) {
            case GameRankingType.WorldBossFirstClears:
                return InGameRankPacket.WorldBossClears(type, db.GetBossFirstClears(rankingId));
            case GameRankingType.WorldBossDamage:
                return InGameRankPacket.WorldBossDamage(type, db.GetWorldBossDamageClears(rankingId));
        }
        if (string.IsNullOrEmpty(userName)) {
            return InGameRankPacket.BossClearCount(GameRankingType.WorldBossClearCount, rankingId, db.GetBossClearCounts(rankingId));
        }

        BossClearCountRankInfo? info = db.GetBossClearCount(rankingId, userName);
        return InGameRankPacket.BossClearCount(GameRankingType.WorldBossClearCount, rankingId,
            info != null ? new List<BossClearCountRankInfo> { info } : []);
    }

    // 0x6f: the character's rank on the clear count board; 0x83: its rank on the damage board. 0 without a clear.
    public static ByteWriter WorldBossPersonal(GameStorage.Request db, GameRankingType type, int rankingId, long characterId) {
        int rank = type == GameRankingType.WorldBossDamagePersonal
            ? db.GetWorldBossDamageRank(rankingId, characterId)
            : db.GetBossClearCount(rankingId, characterId)?.Rank ?? 0;
        return InGameRankPacket.PersonalRank(type, rank);
    }

    // The fame challenge board (0xc) has one tab per fame challenge room with a BossRanking, with the same three
    // modes from the same kept clears: the clears per character (0x8e, the own rank 0x8d), the first clear of each
    // character (0x97) and the fastest clear of each character (0xa2, the own rank 0xa1). Its rooms are for one
    // player, so a row is one character.
    public static ByteWriter FameChallengeBoard(GameStorage.Request db, GameRankingType type, int rankingId, string userName) {
        switch (type) {
            case GameRankingType.FameChallengeFirstClears:
                return InGameRankPacket.FameChallengeClears(type, db.GetFameChallengeFirstClears(rankingId));
            case GameRankingType.FameChallengeShortestClears:
                return InGameRankPacket.FameChallengeClears(type, db.GetFameChallengeShortestClears(rankingId));
        }
        if (string.IsNullOrEmpty(userName)) {
            return InGameRankPacket.BossClearCount(GameRankingType.FameChallengeClearCount, rankingId, db.GetBossClearCounts(rankingId));
        }

        BossClearCountRankInfo? info = db.GetBossClearCount(rankingId, userName);
        return InGameRankPacket.BossClearCount(GameRankingType.FameChallengeClearCount, rankingId,
            info != null ? new List<BossClearCountRankInfo> { info } : []);
    }

    // 0x8d: the character's rank on the clear count board; 0xa1: its rank on the fastest clear board. 0 without a clear.
    public static ByteWriter FameChallengePersonal(GameStorage.Request db, GameRankingType type, int rankingId, long characterId) {
        int rank = type == GameRankingType.FameChallengeShortestClearPersonal
            ? db.GetFameChallengeShortestClearRank(rankingId, characterId)
            : db.GetBossClearCount(rankingId, characterId)?.Rank ?? 0;
        return InGameRankPacket.PersonalRank(type, rank);
    }
}
