using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Linq;
using Maple2.Database.Model.Ranking;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Maple2.Server.Web.Controllers;

// maview pages of the guild window's ranking tab. Its PvP button opens "maview:/Rank/GuildPVP?tp=realtime"
// (UIGuildDialog::OpenPvpRankPage, uiguilddialog.cpp:3016, m_strPvpRankUrl at :278) in the window's browser 0x4e6a; the
// maview prefix is the ServerEnter maview url (CWebBrowserService::ExpandUrlPrefix), and the url carries nothing
// else, so the page is the same for every player. The page is plain HTML the browser shows as it is; the client reads
// nothing back. Built from the guild championship board the ranking dialog shows (GuildPvpRanking: the guilds of
// the current seasondata_guildpvp season by score, then fewer losses). Chosen here: tp=realtime and any other tp
// show the current season (the client sends only realtime); the page's look; no server call outside this one.
//
// The other ranking pages the KR client opens, each from the same boards the in-game ranking dialog shows:
// - /Rank/Guild and /Rank/Guild?tp=realtime: the guild window's ranking tab (when the in-game ranking is off) and the
//   town guild ranking boards (interactobject 50000001-50000003 <webOpen>); the guilds by trophies.
// - /Rank/Character: the town character ranking boards (50000004-50000006); the characters by trophies.
// - /Rank/PVP: the rank duel dialog's ranking button (uirankpvpdialog) and the town boards (50000013-50000015); the
//   current PvP season's duelists by rating.
// - /rank/darkstream: the dark stream result window and its board object (19000018); the scores of every job.
// - /Rank/Architect?tp=realtime: the town architect boards (50000010-50000012); the homes by this month's commends.
// Routes match without regard to case, so /rank/darkstream and /Rank/DarkStream are the same page. Chosen here: tp
// is not read (each page is the current board), and a page is kept for a minute: the trophy boards add up every
// character's or guild's trophies.
[Route("/Rank/")]
public class RankController : ControllerBase {
    private readonly GameStorage gameStorage;
    private readonly TableMetadataStorage tableMetadata;
    private readonly IMemoryCache cache;

    private static readonly TimeSpan PageCacheDuration = TimeSpan.FromMinutes(1);

    public RankController(GameStorage gameStorage, TableMetadataStorage tableMetadata, IMemoryCache cache) {
        this.gameStorage = gameStorage;
        this.tableMetadata = tableMetadata;
        this.cache = cache;
    }

    [HttpGet("GuildPVP")]
    public IResult GetGuildPvp() {
        string page = GuildPvpPage(gameStorage, tableMetadata.SeasonDataTable.GuildPvp, DateTime.Now);
        return Html(page);
    }

    [HttpGet("Guild")]
    public IResult GetGuild() => Html(Cached("Guild", () => {
        using GameStorage.Request db = gameStorage.Context();
        return GuildPage(db.GetGuildTrophyRankings());
    }));

    [HttpGet("Character")]
    public IResult GetCharacter() => Html(Cached("Character", () => {
        using GameStorage.Request db = gameStorage.Context();
        return CharacterPage(db.GetTrophyRankings());
    }));

    [HttpGet("PVP")]
    public IResult GetPvp() {
        int season = RankDuelRules.SeasonAt(tableMetadata.SeasonDataTable.Pvp, DateTime.Now);
        return Html(Cached($"PVP_{season}", () => {
            if (season == 0) {
                return PvpPage(0, []);
            }
            using GameStorage.Request db = gameStorage.Context();
            return PvpPage(season, db.GetRankDuelRankings(season));
        }));
    }

    [HttpGet("DarkStream")]
    public IResult GetDarkStream() => Html(Cached("DarkStream", () => {
        using GameStorage.Request db = gameStorage.Context();
        return DarkStreamPage(db.GetDarkStreamRankings(0));
    }));

    [HttpGet("Architect")]
    public IResult GetArchitect() {
        (long begin, long end) = HomeCommendRules.MonthSpan(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), TimeZoneInfo.Local);
        return Html(Cached($"Architect_{begin}", () => {
            using GameStorage.Request db = gameStorage.Context();
            return ArchitectPage(DateTimeOffset.FromUnixTimeSeconds(begin).ToLocalTime(), db.GetHomeCommendRankings(begin, end));
        }));
    }

    private static IResult Html(string page) => Results.Content(page, "text/html; charset=utf-8");

    private string Cached(string key, Func<string> build) {
        return cache.GetOrCreate($"RankPage_{key}", entry => {
            entry.AbsoluteExpirationRelativeToNow = PageCacheDuration;
            return build();
        })!;
    }

    public const string NoTrophy = "No one has a trophy yet.";
    public const string NoGuildTrophy = "No guild has a trophy yet.";
    public const string NoPvpSeason = "There is no rank duel season now.";
    public const string NoDuel = "No one has dueled this season yet.";
    public const string NoDarkStream = "No one has a dark stream score yet.";
    public const string NoCommend = "No home has a commend this month yet.";

    private static readonly string[] TrophyHeads = ["Rank", "Trophies", "Combat", "Adventure", "Lifestyle"];

    private static IEnumerable<string> Trophies(AchievementInfo trophy) =>
        [trophy.Total.ToString(), trophy.Combat.ToString(), trophy.Adventure.ToString(), trophy.Lifestyle.ToString()];

    // The guilds by trophies: rank, guild, leader, the total and its three kinds.
    public static string GuildPage(IList<GuildTrophyRankInfo> rows) {
        return Page("Guild Ranking", null, NoGuildTrophy, ["Rank", "Guild", "Leader", "Trophies", "Combat", "Adventure", "Lifestyle"], 2,
            rows.Select(row => new[] { row.Rank.ToString(), row.Name, row.LeaderName }.Concat(Trophies(row.Trophy)).ToArray()));
    }

    // The characters by trophies: rank, name, the total and its three kinds.
    public static string CharacterPage(IList<TrophyRankInfo> rows) {
        return Page("Character Ranking", null, NoTrophy, ["Rank", "Name", .. TrophyHeads[1..]], 1,
            rows.Select(row => new[] { row.Rank.ToString(), row.Name }.Concat(Trophies(row.Trophy)).ToArray()));
    }

    // The duelists of the season by rating: rank, name, rating, best rating. Season 0 is no season.
    public static string PvpPage(int season, IList<RankDuelRankInfo> rows) {
        return season == 0
            ? Page("Rank Duel Ranking", NoPvpSeason, NoDuel, [], 0, null)
            : Page("Rank Duel Ranking", $"Season {season}", NoDuel, ["Rank", "Name", "Rating", "Best Rating"], 1,
                rows.Select(row => new[] { row.Rank.ToString(), row.Name, row.Rating.ToString(), row.BestRating.ToString() }));
    }

    // The dark stream scores of every job: rank, name, score, the date it was set.
    public static string DarkStreamPage(IList<DarkStreamRankInfo> rows) {
        return Page("Dark Stream Ranking", null, NoDarkStream, ["Rank", "Name", "Score", "Date"], 1,
            rows.Select(row => new[] {
                row.Rank.ToString(), row.Name, row.Score.ToString(),
                DateTimeOffset.FromUnixTimeSeconds(row.SetTime).ToLocalTime().ToString("yyyy-MM-dd"),
            }), numericTo: 2);
    }

    // The homes by this month's commends: rank, home, owner, commends.
    public static string ArchitectPage(DateTimeOffset month, IList<HomeCommendRankInfo> rows) {
        return Page("Architect Ranking", month.ToString("yyyy-MM"), NoCommend, ["Rank", "Home", "Owner", "Commends"], 2,
            rows.Select(row => new[] { row.Rank.ToString(), row.HomeName, row.Name, row.Commends.ToString() }));
    }

    // A page in the look of the guild championship page: a title, a note line, then a table, or the empty text when
    // there are no rows (or rows is null). The first column and the columns after textColumns (up to the column numericTo,
    // all by default) are numbers, right-aligned; every cell is HTML-encoded, the names being the players' own text.
    private static string Page(string title, string? note, string empty, string[] heads, int textColumns,
                               IEnumerable<string[]>? rows, int numericTo = int.MaxValue) {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(title).Append("</title><style>")
            .Append("body{margin:0;padding:12px;font-family:sans-serif;font-size:12px;background:#fff;color:#333}")
            .Append("h3{margin:0 0 4px}p{margin:0 0 8px}table{width:100%;border-collapse:collapse}")
            .Append("th,td{padding:4px 6px;border-bottom:1px solid #ddd;text-align:left}th{background:#f2f2f2}")
            .Append("td.n{text-align:right}</style></head><body><h3>").Append(title).Append("</h3>");
        if (note != null) {
            html.Append("<p>").Append(WebUtility.HtmlEncode(note)).Append("</p>");
        }
        List<string[]> list = rows?.ToList() ?? [];
        if (rows != null && list.Count > 0) {
            html.Append("<table><tr>");
            foreach (string head in heads) {
                html.Append("<th>").Append(head).Append("</th>");
            }
            html.Append("</tr>");
            foreach (string[] row in list) {
                html.Append("<tr>");
                for (int i = 0; i < row.Length; i++) {
                    bool numeric = i == 0 || (i > textColumns && i <= numericTo);
                    html.Append(numeric ? "<td class=\"n\">" : "<td>").Append(WebUtility.HtmlEncode(row[i])).Append("</td>");
                }
                html.Append("</tr>");
            }
            html.Append("</table>");
        } else if (rows != null || note == null) {
            html.Append("<p>").Append(empty).Append("</p>");
        }
        return html.Append("</body></html>").ToString();
    }

    // The page of the season running at now, from the guilds' standings in the database.
    public static string GuildPvpPage(GameStorage gameStorage, IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, DateTime now) {
        SeasonDataTable.Entry? season = GuildPvp.CurrentSeason(seasons, now);
        IList<GuildPvpRankRow> rows = [];
        if (season != null) {
            using GameStorage.Request db = gameStorage.Context();
            rows = GuildPvpRanking.Rows(db.GetGuildPvpStandings(season.Id), season);
        }
        return GuildPvpPage(season, rows);
    }

    public const string NoSeason = "There is no guild championship season now.";
    public const string NoGuild = "No guild has fought this season yet.";

    // The page: the season's dates and one table row per guild (rank, guild, leader, score, grade). Names are the
    // players' own text, so every one is HTML-encoded.
    public static string GuildPvpPage(SeasonDataTable.Entry? season, IList<GuildPvpRankRow> rows) {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Guild Championship</title><style>")
            .Append("body{margin:0;padding:12px;font-family:sans-serif;font-size:12px;background:#fff;color:#333}")
            .Append("h3{margin:0 0 4px}p{margin:0 0 8px}table{width:100%;border-collapse:collapse}")
            .Append("th,td{padding:4px 6px;border-bottom:1px solid #ddd;text-align:left}th{background:#f2f2f2}")
            .Append("td.n{text-align:right}</style></head><body><h3>Guild Championship</h3>");
        if (season == null) {
            html.Append("<p>").Append(NoSeason).Append("</p>");
        } else {
            html.Append("<p>Season ").Append(season.Id).Append(": ")
                .Append(season.StartTime.ToString("yyyy-MM-dd")).Append(" - ").Append(season.EndTime.ToString("yyyy-MM-dd")).Append("</p>");
            if (rows.Count == 0) {
                html.Append("<p>").Append(NoGuild).Append("</p>");
            } else {
                html.Append("<table><tr><th>Rank</th><th>Guild</th><th>Leader</th><th>Score</th><th>Grade</th></tr>");
                foreach (GuildPvpRankRow row in rows) {
                    html.Append("<tr><td class=\"n\">").Append(row.Rank)
                        .Append("</td><td>").Append(WebUtility.HtmlEncode(row.Standing.Name))
                        .Append("</td><td>").Append(WebUtility.HtmlEncode(row.Standing.LeaderName))
                        .Append("</td><td class=\"n\">").Append(row.Standing.Score)
                        .Append("</td><td class=\"n\">").Append(row.Grade)
                        .Append("</td></tr>");
                }
                html.Append("</table>");
            }
        }
        return html.Append("</body></html>").ToString();
    }
}
