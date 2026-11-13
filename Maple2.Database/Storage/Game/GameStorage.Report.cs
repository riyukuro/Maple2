using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public void ReportPlayer(long characterId, string playerName, long reporterCharacterId, string reporterName, string reason, PlayerReportFlag flag) {
            Context.PlayerReports.Add(new PlayerReport {
                ReporterCharacterId = reporterCharacterId,
                ReporterName = reporterName,
                CharacterId = characterId,
                PlayerName = playerName,
                Reason = reason,
                Category = ReportCategory.Player,
                CreateTime = DateTime.Now,
                ReportInfo = new PlayerReportInfo(flag.ToString()),
            });
            Context.TrySaveChanges();
        }

        public void ReportChat(long characterId, string playerName, long reporterCharacterId, string reporterName, string reason, string chatMessage, ChatReportFlag flag) {
            Context.PlayerReports.Add(new PlayerReport {
                ReporterCharacterId = reporterCharacterId,
                ReporterName = reporterName,
                CharacterId = characterId,
                PlayerName = playerName,
                Reason = reason,
                Category = ReportCategory.Chat,
                CreateTime = DateTime.Now,
                ReportInfo = new ChatReportInfo(flag.ToString(), chatMessage),
            });
            Context.TrySaveChanges();
        }

        public void ReportPoster(long characterId, string playerName, long reporterCharacterId, string reporterName, string reason, long posterId, string templateId, PosterReportFlag flag) {
            Context.PlayerReports.Add(new PlayerReport {
                ReporterCharacterId = reporterCharacterId,
                ReporterName = reporterName,
                CharacterId = characterId,
                PlayerName = playerName,
                Reason = reason,
                Category = ReportCategory.Poster,
                CreateTime = DateTime.Now,
                ReportInfo = new PosterReportInfo(flag.ToString(), posterId, templateId),
            });
            Context.TrySaveChanges();
        }

        public void ReportDesignItem(long characterId, string playerName, long reporterCharacterId, string reporterName, string reason, long listingId, DesignItemReportFlag flag) {
            Context.PlayerReports.Add(new PlayerReport {
                ReporterCharacterId = reporterCharacterId,
                ReporterName = reporterName,
                CharacterId = characterId,
                PlayerName = playerName,
                Reason = reason,
                Category = ReportCategory.ItemDesign,
                CreateTime = DateTime.Now,
                ReportInfo = new ItemReportInfo(flag.ToString(), listingId),
            });
            Context.TrySaveChanges();
        }

        public void ReportHome(long characterId, string playerName, long reporterCharacterId, string reporterName, string reason, long homeId, int mapId, int plotId, HomeReportFlag flag) {
            Context.PlayerReports.Add(new PlayerReport {
                ReporterCharacterId = reporterCharacterId,
                ReporterName = reporterName,
                CharacterId = characterId,
                PlayerName = playerName,
                Reason = reason,
                Category = ReportCategory.Home,
                CreateTime = DateTime.Now,
                ReportInfo = new HomeReportInfo(flag.ToString(), homeId, mapId, plotId),
            });
            Context.TrySaveChanges();
        }

        public void ReportPet(long characterId, string playerName, long reporterCharacterId, string reporterName, string reason, string petName, PetReportFlag flag) {
            Context.PlayerReports.Add(new PlayerReport {
                ReporterCharacterId = reporterCharacterId,
                ReporterName = reporterName,
                CharacterId = characterId,
                PlayerName = playerName,
                Reason = reason,
                Category = ReportCategory.Pet,
                CreateTime = DateTime.Now,
                ReportInfo = new PetReportInfo(flag.ToString(), petName),
            });
            Context.TrySaveChanges();
        }

        // The reports, newest first, at most count: the open ones, or every one with closed.
        public IList<PlayerReportEntry> GetPlayerReports(bool closed, int count) {
            return Context.PlayerReports.AsNoTracking()
                .Where(report => closed || report.CloseTime == null)
                .OrderByDescending(report => report.Id)
                .Take(count)
                .AsEnumerable()
                .Select(ToEntry)
                .ToList();
        }

        public PlayerReportEntry? GetPlayerReport(long id) {
            PlayerReport? report = Context.PlayerReports.AsNoTracking().FirstOrDefault(report => report.Id == id);
            return report == null ? null : ToEntry(report);
        }

        // Closes an open report; null when there is no such report, it is closed already, or the save failed.
        // On a relational DB the close is one update that tests CloseTime, so of two GMs closing the same report at
        // once only one changes the row and is told so; the other gets null.
        public PlayerReportEntry? ClosePlayerReport(long id, long gmAccountId) {
            DateTime? closeTime = DateTime.Now;
            if (Context.Database.IsRelational()) {
                try {
                    int rows = Context.PlayerReports
                        .Where(report => report.Id == id && report.CloseTime == null)
                        .ExecuteUpdate(update => update
                            .SetProperty(report => report.CloseTime, closeTime)
                            .SetProperty(report => report.ClosedBy, gmAccountId));
                    return rows == 1 ? GetPlayerReport(id) : null;
                } catch (Exception ex) {
                    Logger.LogError(ex, "Failed to close player report {ReportId}", id);
                    return null;
                }
            }

            // The context does not track by default; the close is written through a tracked read.
            PlayerReport? tracked = Context.PlayerReports.AsTracking().FirstOrDefault(report => report.Id == id);
            if (tracked == null || tracked.CloseTime != null) {
                return null;
            }
            tracked.CloseTime = closeTime;
            tracked.ClosedBy = gmAccountId;
            return Context.TrySaveChanges() ? ToEntry(tracked) : null;
        }

        private static PlayerReportEntry ToEntry(PlayerReport report) {
            string detail = report.ReportInfo switch {
                ChatReportInfo chat => $"{chat.Flag}: {chat.Message}",
                HomeReportInfo home => $"{home.Flag}, home {home.HomeId} map {home.MapId} plot {home.PlotId}",
                PetReportInfo pet => $"{pet.Flag}, pet {pet.PetName}",
                PosterReportInfo poster => $"{poster.Flag}, poster {poster.PosterId} template {poster.TemplateId}",
                ItemReportInfo item => $"{item.Flag}, listing {item.ListingId}",
                PlayerReportInfo player => player.Flag,
                _ => string.Empty,
            };
            return new PlayerReportEntry(report.Id, report.Category, report.CharacterId, report.PlayerName, report.ReporterCharacterId,
                report.ReporterName, report.Reason, detail, report.CreateTime, report.CloseTime, report.ClosedBy);
        }
    }
}
