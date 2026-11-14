using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Maple2.Server.Web.Controllers;

// maview pages of the guild window's ranking tab. Its PvP button opens "maview:/Rank/GuildPVP?tp=realtime"
// (UIGuildDialog::OpenPvpRankPage, uiguilddialog.cpp:3016, m_strPvpRankUrl at :278) in the window's browser 0x4e6a; the
// maview prefix is the ServerEnter maview url (CWebBrowserService::ExpandUrlPrefix), and the url carries nothing
// else, so the page is the same for every player. The page is plain HTML the browser shows as it is; the client reads
// nothing back. Built from the guild championship board the ranking dialog shows (GuildPvpRanking: the guilds of
// the current seasondata_guildpvp season by score, then fewer losses). Chosen here: tp=realtime and any other tp
// show the current season (the client sends only realtime); the page's look; no server call outside this one.
[Route("/Rank/")]
public class RankController : ControllerBase {
    private readonly GameStorage gameStorage;
    private readonly TableMetadataStorage tableMetadata;

    public RankController(GameStorage gameStorage, TableMetadataStorage tableMetadata) {
        this.gameStorage = gameStorage;
        this.tableMetadata = tableMetadata;
    }

    [HttpGet("GuildPVP")]
    public IResult GetGuildPvp([FromQuery] string? tp) {
        string page = GuildPvpPage(gameStorage, tableMetadata.SeasonDataTable.GuildPvp, DateTime.Now);
        return Results.Content(page, "text/html; charset=utf-8");
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
