using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Tools.Extensions;
using Z.EntityFramework.Plus;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public Guild? GetGuild(long guildId) {
            return LoadGuild(guildId, string.Empty);
        }

        public Guild? GetGuild(string guildName) {
            return LoadGuild(0, guildName);
        }

        // The guild search: listed guilds that share a focus bit with the request (focusMask 0 = any focus),
        // and whose name holds the given text (empty = any name).
        public IList<GuildSearchEntry> SearchGuilds(int focusMask, string name, int limit) {
            IQueryable<Model.Guild> query = Context.Guild.Where(guild => guild.Listed);
            if (focusMask != 0) {
                query = query.Where(guild => ((int) guild.Focus & focusMask) != 0);
            }
            if (name.Length > 0) {
                query = query.Where(guild => guild.Name.Contains(name));
            }

            return query.OrderBy(guild => guild.Id)
                .Take(limit)
                .Join(Context.Character, guild => guild.LeaderId, character => character.Id,
                    (guild, character) => new {
                        guild.Id,
                        guild.Name,
                        guild.Emblem,
                        MemberCount = Context.GuildMember.Count(member => member.GuildId == guild.Id),
                        guild.Experience,
                        character.AccountId,
                        CharacterId = character.Id,
                        LeaderName = character.Name,
                    })
                .AsEnumerable()
                .Select(row => new GuildSearchEntry(row.Id, row.Name, row.Emblem, row.MemberCount, row.Experience,
                    row.AccountId, row.CharacterId, row.LeaderName))
                .ToList();
        }

        public bool GuildExists(long guildId = 0, string guildName = "") {
            return Context.Guild.Any(guild => guild.Id == guildId || guild.Name == guildName);
        }

        public IList<GuildMember> GetGuildMembers(IPlayerInfoProvider provider, long guildId) {
            return Context.GuildMember.Where(member => member.GuildId == guildId)
                .AsEnumerable()
                .Select(member => {
                    PlayerInfo? info = provider.GetPlayerInfo(member.CharacterId);
                    if (info == null) {
                        return null;
                    }
                    var result = new GuildMember {
                        GuildId = member.GuildId,
                        Info = info,
                        Message = member.Message,
                        Rank = member.Rank,
                        WeeklyContribution = member.WeeklyContribution,
                        TotalContribution = member.TotalContribution,
                        DailyDonationCount = member.DailyDonationCount,
                        JoinTime = member.CreationTime.ToEpochSeconds(),
                        CheckinTime = member.CheckinTime.ToEpochSeconds(),
                        DonationTime = member.DonationTime.ToEpochSeconds(),
                    };
                    foreach ((int raidId, int value) in member.Raids ?? new Dictionary<int, int>()) {
                        result.Raids[raidId] = value;
                    }
                    return result;
                })
                .WhereNotNull()
                .ToList();
        }

        public Guild? CreateGuild(string name, long leaderId) {
            BeginTransaction();

            var guild = new Model.Guild {
                Name = name,
                LeaderId = leaderId,
                HouseRank = 1,
                HouseTheme = 1,
                Ranks = [
                    new Model.GuildRank {Name = "Master", Permission = GuildPermission.All},
                    new Model.GuildRank {Name = "Jr. Master", Permission = GuildPermission.Default},
                    new Model.GuildRank {Name = "Member 1", Permission = GuildPermission.Default},
                    new Model.GuildRank {Name = "Member 2", Permission = GuildPermission.Default},
                    new Model.GuildRank {Name = "New Member 1", Permission = GuildPermission.Default},
                    new Model.GuildRank {Name = "New Member 2", Permission = GuildPermission.Default},
                ],
                Buffs = [
                    new Model.GuildBuff {Id = 1, Level = 1},
                    new Model.GuildBuff {Id = 2, Level = 1},
                    new Model.GuildBuff {Id = 3, Level = 1},
                    new Model.GuildBuff {Id = 4, Level = 1},
                    new Model.GuildBuff {Id = 10001, Level = 1},
                    new Model.GuildBuff {Id = 10002, Level = 1},
                    new Model.GuildBuff {Id = 10003, Level = 1},
                    new Model.GuildBuff {Id = 10004, Level = 1},
                    new Model.GuildBuff {Id = 10005, Level = 1},
                ],
                Posters = [],
                Npcs = [],
            };
            Context.Guild.Add(guild);
            if (!SaveChanges()) {
                Rollback();
                return null;
            }

            var guildLeader = new Model.GuildMember {
                GuildId = guild.Id,
                CharacterId = leaderId,
                Rank = 0,
            };
            Context.GuildMember.Add(guildLeader);
            RemoveGuildApplicationsOf(leaderId);
            if (!SaveChanges()) {
                Rollback();
                return null;
            }

            return Commit() ? LoadGuild(guild.Id, string.Empty) : null;
        }

        // A character who becomes a member has no open join request left. The rows are removed by the save that
        // inserts the member, so both happen or neither.
        private void RemoveGuildApplicationsOf(long characterId) {
            Context.GuildApplication.RemoveRange(Context.GuildApplication.Where(app => app.ApplicantId == characterId));
        }

        public GuildMember? CreateGuildMember(long guildId, PlayerInfo info) {
            var member = new Model.GuildMember {
                GuildId = guildId,
                CharacterId = info.CharacterId,
                Rank = 5,
            };
            Context.GuildMember.Add(member);
            RemoveGuildApplicationsOf(info.CharacterId);
            if (!SaveChanges()) {
                return null;
            }

            return new GuildMember {
                GuildId = member.GuildId,
                Info = info,
                Rank = member.Rank,
                JoinTime = member.CreationTime.ToEpochSeconds(),
            };
        }

        public bool SaveGuild(Guild guild) {
            // Don't save guild if it was disbanded.
            if (!Context.Guild.Any(model => model.Id == guild.Id)) {
                return false;
            }

            BeginTransaction();

            Context.Guild.Update(guild);
            SaveGuildMembers(guild.Id, guild.Members.Values);

            return Commit();
        }

        public bool DeleteGuild(long guildId) {
            BeginTransaction();

            int count = Context.Guild.Where(guild => guild.Id == guildId).Delete();
            if (count == 0) {
                Rollback();
                return false;
            }

            Context.GuildMember.Where(member => member.GuildId == guildId).Delete();
            Context.GuildApplication.Where(app => app.GuildId == guildId).Delete();

            return Commit();
        }

        public bool DeleteGuildMember(long guildId, long characterId) {
            int count = Context.GuildMember.Where(member => member.GuildId == guildId && member.CharacterId == characterId).Delete();
            return SaveChanges() && count > 0;
        }

        public bool CreateGuildExpelNotice(long ownerId, GuildExpelNotice notice) {
            Context.GuildExpelNotice.Add(new Model.GuildExpelNotice {
                OwnerId = ownerId,
                Name = notice.Name,
                Time = notice.Time,
            });
            return Context.TrySaveChanges();
        }

        // The character's kept guild expels in the order they were kept; they are deleted as they are read.
        public IList<GuildExpelNotice> TakeGuildExpelNotices(long ownerId) {
            List<Model.GuildExpelNotice> rows = Context.GuildExpelNotice
                .Where(notice => notice.OwnerId == ownerId)
                .OrderBy(notice => notice.Id)
                .ToList();
            if (rows.Count == 0) {
                return Array.Empty<GuildExpelNotice>();
            }

            Context.GuildExpelNotice.RemoveRange(rows);
            if (!Context.TrySaveChanges()) {
                return Array.Empty<GuildExpelNotice>();
            }
            return rows.Select(row => new GuildExpelNotice(row.Name, row.Time)).ToList();
        }

        // The guild a character is a member of, 0 for none.
        public long GetGuildId(long characterId) {
            return Context.GuildMember.Where(member => member.CharacterId == characterId)
                .Select(member => member.GuildId)
                .FirstOrDefault();
        }

        public int GetGuildMemberCount(long guildId) {
            return Context.GuildMember.Count(member => member.GuildId == guildId);
        }

        public GuildApplicationRow? CreateGuildApplication(long guildId, long applicantId) {
            var application = new Model.GuildApplication {
                GuildId = guildId,
                ApplicantId = applicantId,
            };
            Context.GuildApplication.Add(application);
            if (!SaveChanges()) {
                return null;
            }

            return ToRow(application);
        }

        // The open join requests sent to a guild, oldest first.
        public IList<GuildApplicationRow> GetGuildApplications(long guildId) {
            return Context.GuildApplication.Where(app => app.GuildId == guildId)
                .OrderBy(app => app.Id)
                .AsEnumerable()
                .Select(ToRow)
                .ToList();
        }

        // The open join requests of a character, oldest first.
        public IList<GuildApplicationRow> GetGuildApplicationsOf(long characterId) {
            return Context.GuildApplication.Where(app => app.ApplicantId == characterId)
                .OrderBy(app => app.Id)
                .AsEnumerable()
                .Select(ToRow)
                .ToList();
        }

        public GuildApplicationRow? GetGuildApplication(long guildId, long characterId) {
            Model.GuildApplication? application = Context.GuildApplication
                .FirstOrDefault(app => app.GuildId == guildId && app.ApplicantId == characterId);
            return application == null ? null : ToRow(application);
        }

        private static GuildApplicationRow ToRow(Model.GuildApplication application) {
            return new GuildApplicationRow(application.Id, application.GuildId, application.ApplicantId,
                application.CreationTime.ToEpochSeconds());
        }

        public bool DeleteGuildApplication(long applicationId) {
            int count = Context.GuildApplication.Where(app => app.Id == applicationId).Delete();
            return SaveChanges() && count > 0;
        }

        public bool DeleteGuildApplications(long characterId) {
            int count = Context.GuildApplication.Where(app => app.ApplicantId == characterId).Delete();
            return SaveChanges() && count > 0;
        }

        public bool SaveGuildMembers(long guildId, ICollection<GuildMember> members) {
            Dictionary<long, GuildMember> saveMembers = members
                .ToDictionary(member => member.CharacterId, member => member);
            IEnumerable<Model.GuildMember> existingMembers = Context.GuildMember
                .Where(member => member.GuildId == guildId)
                .Select(member => new Model.GuildMember {
                    CharacterId = member.CharacterId,
                });

            foreach (Model.GuildMember member in existingMembers) {
                if (saveMembers.Remove(member.CharacterId, out GuildMember? gameMember)) {
                    Context.GuildMember.Update(gameMember);
                } else {
                    Context.GuildMember.Remove(member);
                }
            }
            Context.GuildMember.AddRange(saveMembers.Values.Select<GuildMember, Model.GuildMember>(member => member));

            return SaveChanges();
        }

        public bool SaveGuildMember(GuildMember member) {
            // Not Find: that would track the stored row next to the one Update attaches, under the same key.
            if (!Context.GuildMember.Any(row => row.GuildId == member.GuildId && row.CharacterId == member.CharacterId)) {
                return false;
            }

            Context.GuildMember.Update(member);
            return SaveChanges();
        }

        // Note: GuildMembers must be loaded separately.
        private Guild? LoadGuild(long guildId, string guildName) {
            IQueryable<Model.Guild> query = guildId > 0
                ? Context.Guild.Where(guild => guild.Id == guildId)
                : Context.Guild.Where(guild => guild.Name == guildName);
            return query
                .Join(Context.Character, guild => guild.LeaderId, character => character.Id,
                    (guild, character) => new Tuple<Model.Guild, Model.Character>(guild, character))
                .AsEnumerable()
                .Select(entry => {
                    Model.Guild guild = entry.Item1;
                    Character character = entry.Item2;
                    return new Guild(guild.Id, guild.Name, character.AccountId, character.Id, character.Name) {
                        Emblem = guild.Emblem,
                        Notice = guild.Notice,
                        CreationTime = guild.CreationTime.ToEpochSeconds(),
                        Focus = guild.Focus,
                        Listed = guild.Listed,
                        Experience = guild.Experience,
                        Funds = guild.Funds,
                        HouseRank = guild.HouseRank,
                        HouseTheme = guild.HouseTheme,
                        Ranks = guild.Ranks.Select((rank, i) => new GuildRank {
                            Id = (byte) i,
                            Name = rank.Name,
                            Permission = rank.Permission,
                        }).ToArray(),
                        Buffs = guild.Buffs.Select(skill => new GuildBuff {
                            Id = skill.Id,
                            Level = skill.Level,
                            ExpiryTime = skill.ExpiryTime,
                        }).ToList(),
                        Posters = guild.Posters.Select(poster => new GuildPoster {
                            Id = poster.Id,
                            Picture = poster.Picture,
                            OwnerId = poster.OwnerId,
                            OwnerName = poster.OwnerName,
                        }).ToList(),
                        Npcs = guild.Npcs.Select(npc => new GuildNpc {
                            Type = npc.Type,
                            Level = npc.Level,
                        }).ToList(),
                    };
                })
                .FirstOrDefault();
        }
    }
}
