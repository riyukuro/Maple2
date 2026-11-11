using System;
using System.Collections.Generic;
using Maple2.Database.Model.Ranking;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Web.Packet;

public static class InGameRankPacket {
    public static ByteWriter Trophy(IList<TrophyRankInfo> rankInfos) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(GameRankingType.Trophy);
        pWriter.WriteInt(1); // Mode ?
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rankInfos.Count);
        foreach (TrophyRankInfo info in rankInfos) {
            pWriter.WriteInt(info.Rank);
            pWriter.WriteLong(info.CharacterId);
            pWriter.WriteUnicodeStringWithLength(info.Name);
            pWriter.WriteUnicodeStringWithLength(info.Profile);
            pWriter.WriteInt(info.Trophy.Total);
            pWriter.Write<AchievementInfo>(info.Trophy);
        }

        return pWriter;
    }

    public static ByteWriter PersonalRank(GameRankingType type, int rank) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rank);

        return pWriter;
    }

    public static ByteWriter GuildTrophy(IList<GuildTrophyRankInfo> rankInfos) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(GameRankingType.GuildTrophy);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rankInfos.Count);

        foreach (GuildTrophyRankInfo info in rankInfos) {
            pWriter.WriteInt(info.Rank);
            pWriter.WriteLong(info.GuildId);
            pWriter.WriteUnicodeStringWithLength(info.Name);
            pWriter.WriteUnicodeStringWithLength(info.Emblem);
            pWriter.WriteUnicodeStringWithLength(info.LeaderName);
            pWriter.WriteInt(info.Trophy.Total);
            pWriter.WriteInt(info.Trophy.Combat);
            pWriter.WriteInt(info.Trophy.Adventure);
            pWriter.WriteInt(info.Trophy.Lifestyle);
        }
        return pWriter;
    }

    // The Dark Stream board (0x21, and 0x22 for the season before), as the client's GameRanking::DarkStream reads
    // it: the header, then a count of rows, each a rank, the character id, the name, the profile image, two ints
    // the client reads but never uses (sent as 0), the score and the time the score was set.
    public static ByteWriter DarkStream(GameRankingType type, IList<DarkStreamRankInfo> rankInfos) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rankInfos.Count);
        foreach (DarkStreamRankInfo info in rankInfos) {
            pWriter.WriteInt(info.Rank);
            pWriter.WriteLong(info.CharacterId);
            pWriter.WriteUnicodeStringWithLength(info.Name);
            pWriter.WriteUnicodeStringWithLength(info.Profile);
            pWriter.WriteInt(0); // read, not used by the client
            pWriter.WriteInt(0); // read, not used by the client
            pWriter.WriteInt(info.Score);
            pWriter.WriteUnicodeStringWithLength(DateTimeOffset.FromUnixTimeSeconds(info.SetTime).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        return pWriter;
    }

    public static ByteWriter Pvp(GameRankingType type) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(0); // rank
        pWriter.WriteLong(0); // characterId
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // profile
        pWriter.WriteInt(); // score
        pWriter.WriteInt(); // rank tier

        return pWriter;
    }

    // The house commendation boards (0x3f, 0x40), as GameRanking::UgcMapCommendation reads them: the header, then a
    // count of rows, each a rank, the owner's character id (its portrait), name and profile image, the commendations,
    // an int the client does not use, the owner's account id and the home's name.
    public static ByteWriter Ugc(GameRankingType type, IList<HomeCommendRankInfo> rankInfos) {
        var pWriter = new ByteWriter();
        pWriter.WriteInt((int) type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rankInfos.Count);
        foreach (HomeCommendRankInfo info in rankInfos) {
            pWriter.WriteInt(info.Rank);
            pWriter.WriteLong(info.CharacterId);
            pWriter.WriteUnicodeStringWithLength(info.Name);
            pWriter.WriteUnicodeStringWithLength(info.Profile);
            pWriter.WriteInt(info.Commends);
            pWriter.WriteInt(0); // read, not shown
            pWriter.WriteLong(info.AccountId);
            pWriter.WriteUnicodeStringWithLength(info.HomeName);
        }

        return pWriter;
    }

    // The clear count boards, the boss one (0x48) and the fame challenge one (0x8e), as the client's
    // GameRanking::BossKillCount and GameRanking::BossRumbleCount read them (the same rows): the header, then a
    // count of rows, each a rank, the character id, the name, the profile image and three ints, of which the client
    // shows the second (the clears). The first goes out as the board's ranking id and the third as 0; the client
    // does not use them.
    public static ByteWriter BossClearCount(GameRankingType type, int rankingId, IList<BossClearCountRankInfo> rankInfos) {
        var pWriter = new ByteWriter();
        pWriter.WriteInt((int) type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rankInfos.Count);
        foreach (BossClearCountRankInfo info in rankInfos) {
            pWriter.WriteInt(info.Rank);
            pWriter.WriteLong(info.CharacterId);
            pWriter.WriteUnicodeStringWithLength(info.Name);
            pWriter.WriteUnicodeStringWithLength(info.Profile);
            pWriter.WriteInt(rankingId); // read, not used by the client
            pWriter.WriteInt(info.Clears);
            pWriter.WriteInt(0); // read, not used by the client
        }

        return pWriter;
    }

    // The first clears (0x51) and fastest clears (0x5b) of a boss board, as the client's GameRanking::BossKillRecord
    // reads them: the header, then a count of rows, each a rank, a long and an int the client does not use (the clear's
    // row id and the board's ranking id), the leader's character id, the clear time in seconds (shown as minutes and
    // seconds), the clear date, then the members: a count, and each member's character id, name, profile image,
    // level and job code (the client keeps the first 10 and shows the leader's name, else the first member's).
    public static ByteWriter BossClears(GameRankingType type, IList<BossClearRankInfo> rankInfos) {
        var pWriter = new ByteWriter();
        pWriter.WriteInt((int) type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rankInfos.Count);
        foreach (BossClearRankInfo info in rankInfos) {
            pWriter.WriteInt(info.Rank);
            pWriter.WriteLong(info.ClearId); // read, not used by the client
            pWriter.WriteLong(info.LeaderId);
            pWriter.WriteInt(info.RankingId); // read, not used by the client
            pWriter.WriteInt(info.Seconds);
            pWriter.WriteUnicodeStringWithLength(DateTimeOffset.FromUnixTimeSeconds(info.ClearTime).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            pWriter.WriteInt(info.Members.Count);
            foreach (BossClearMemberInfo member in info.Members) {
                pWriter.WriteLong(member.CharacterId);
                pWriter.WriteUnicodeStringWithLength(member.Name);
                pWriter.WriteUnicodeStringWithLength(member.Profile);
                pWriter.WriteInt(member.Level);
                pWriter.WriteInt((int) member.JobCode);
            }
        }

        return pWriter;
    }

    public static ByteWriter Arcade(GameRankingType type) {
        var pWriter = new ByteWriter();
        pWriter.Write<GameRankingType>(type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(1); // count

        pWriter.WriteInt(1); // rank
        pWriter.WriteLong(); // character id
        pWriter.WriteUnicodeStringWithLength(string.Empty); // name
        pWriter.WriteUnicodeStringWithLength(string.Empty); // profile
        pWriter.WriteInt(); // arcade id
        pWriter.WriteInt(); // season id
        pWriter.WriteInt((int) JobCode.None); // job code
        pWriter.WriteInt(); // level
        pWriter.WriteInt(); // score
        pWriter.WriteUnicodeStringWithLength(); // timestamp

        return pWriter;
    }

    // The first clears (0x97) and fastest clears (0xa2) of a fame challenge board, as the client's
    // GameRanking::BossRumbleRecord reads them: the header, then a count of rows, each a rank, a long the client does
    // not use (the clear's row id), the character id, its name and profile image, its level and job code, an int the
    // client does not use (the board's ranking id), the clear time in seconds (shown as minutes and seconds) and the
    // clear date. A row is one character (the board's rooms are for one player).
    public static ByteWriter FameChallengeClears(GameRankingType type, IList<BossClearRankInfo> rankInfos) {
        var pWriter = new ByteWriter();
        pWriter.WriteInt((int) type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rankInfos.Count);
        foreach (BossClearRankInfo info in rankInfos) {
            BossClearMemberInfo member = info.Members[0];
            pWriter.WriteInt(info.Rank);
            pWriter.WriteLong(info.ClearId); // read, not used by the client
            pWriter.WriteLong(member.CharacterId);
            pWriter.WriteUnicodeStringWithLength(member.Name);
            pWriter.WriteUnicodeStringWithLength(member.Profile);
            pWriter.WriteInt(member.Level);
            pWriter.WriteInt((int) member.JobCode);
            pWriter.WriteInt(info.RankingId); // read, not used by the client
            pWriter.WriteInt(info.Seconds);
            pWriter.WriteUnicodeStringWithLength(DateTimeOffset.FromUnixTimeSeconds(info.ClearTime).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        return pWriter;
    }

    // The Colosseum board (0xcb, 0xcc), read as the client's GameRanking::BossColosseum: per row the rank, the
    // character, its name and profile picture, the rounds cleared (shown as the points) and the run's seconds (shown
    // as minutes:seconds), and two time strings. The client reads the other fields but does not show them.
    public static ByteWriter Colosseum(GameRankingType type, IList<ColosseumRankInfo> rankInfos) {
        var pWriter = new ByteWriter();
        pWriter.WriteInt((int) type);
        pWriter.WriteInt(0);
        pWriter.WriteUnicodeStringWithLength(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        pWriter.WriteInt(rankInfos.Count);
        foreach (ColosseumRankInfo info in rankInfos) {
            string setTime = DateTimeOffset.FromUnixTimeSeconds(info.SetTime).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            pWriter.WriteInt(info.Rank);
            pWriter.WriteLong(info.CharacterId);
            pWriter.WriteUnicodeStringWithLength(info.Name);
            pWriter.WriteUnicodeStringWithLength(info.Profile);
            pWriter.WriteInt(0); // read, not shown
            pWriter.WriteInt(0); // read, not shown
            pWriter.WriteLong(0); // read, not shown
            pWriter.WriteInt(info.Rounds);
            pWriter.WriteInt(info.Seconds);
            pWriter.WriteInt(0); // read, not shown
            pWriter.WriteUnicodeStringWithLength(setTime); // read, not shown
            pWriter.WriteUnicodeStringWithLength(setTime); // read, not shown
        }

        return pWriter;
    }
}
