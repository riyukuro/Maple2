namespace Maple2.Model.Enum;

public enum GameRankingType {
    PersonalGuildTrophy = 12,
    GuildTrophy = 14,
    PersonalTrophy = 22,
    Trophy = 24,
    DarkDescentPersonal = 31,
    DarkDescentPersonalPreviousSeason = 32,
    DarkDescent = 33,
    DarkDescentPreviousSeason = 34,
    PvpPersonal = 41,
    PvpPersonalPreviousSeason = 42,
    Pvp = 43,
    PvpPreviousSeason = 44,
    UgcPersonal = 61,
    UgcPersonalPreviousSeason = 62,
    Ugc = 63,
    UgcPreviousSeason = 64,
    // The boss boards (the ranking dialog's boards 7, 8, 9, 0xf and 0x11; the request's ranking id is the room's
    // BossRanking): the clears per character and its own rank, the first clears, the fastest clears.
    BossClearCountPersonal = 71,
    BossClearCount = 72,
    BossFirstClears = 81,
    BossShortestClears = 91,
    // The world boss board (the ranking dialog's board 0xb; the ranking id is the room's BossRanking): the clears per
    // character and its own rank, the first clears, the clears by the damage dealt and its own rank there.
    WorldBossClearCountPersonal = 111,
    WorldBossClearCount = 112,
    WorldBossFirstClears = 121,
    WorldBossDamagePersonal = 131,
    WorldBossDamage = 132,
    ArcadePersonal = 101,
    ArcadePersonalPreviousSeason = 102,
    Arcade = 103,
    ArcadePreviousSeason = 104,
    // The fame challenge board (the ranking dialog's board 0xc; the ranking id is the room's BossRanking): the
    // clears per character and its own rank, the first clears, the fastest clears and its own rank there.
    FameChallengeClearCountPersonal = 141,
    FameChallengeClearCount = 142,
    FameChallengeFirstClears = 151,
    FameChallengeShortestClearPersonal = 161,
    FameChallengeShortestClears = 162,
    ColosseumPersonal = 201,
    ColosseumPersonalPreviousSeason = 202,
    Colosseum = 203,
    ColosseumPreviousSeason = 204,
}
