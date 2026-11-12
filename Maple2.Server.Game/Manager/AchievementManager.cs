using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class AchievementManager {
    private const int BATCH_SIZE = 60;
    private readonly GameSession session;

    private readonly IDictionary<int, Achievement> accountValues;
    private readonly IDictionary<int, Achievement> characterValues;

    private readonly ILogger logger = Log.Logger.ForContext<AchievementManager>();

    public AchievementManager(GameSession session) {
        this.session = session;

        using GameStorage.Request db = session.GameStorage.Context();
        accountValues = new ConcurrentDictionary<int, Achievement>(db.GetAchievements(session.AccountId));
        characterValues = new ConcurrentDictionary<int, Achievement>(db.GetAchievements(session.CharacterId));
    }

    public void Load() {
        session.Send(AchievementPacket.Initialize());
        foreach (ImmutableList<Achievement> batch in accountValues.Values.Batch(BATCH_SIZE)) {
            session.Send(AchievementPacket.Load(batch));
        }
        foreach (ImmutableList<Achievement> batch in characterValues.Values.Batch(BATCH_SIZE)) {
            session.Send(AchievementPacket.Load(batch));
        }
    }

    public bool TryGetAchievement(int achievementId, [NotNullWhen(true)] out Achievement? achievement) {
        return accountValues.TryGetValue(achievementId, out achievement) || characterValues.TryGetValue(achievementId, out achievement);
    }

    /// <summary>
    /// Checks for any possible trophies under stated ConditionType. If there is a trophy that can have any progress updated to it, update or add to player if it hasn't existed yet.
    /// </summary>
    /// <param name="conditionType">TrophyConditionType to search metadata</param>
    /// <param name="count">Condition value to update progress of a trophy. Default is 1.</param>
    /// <param name="targetString">Trophy grade condition target parameter in string.</param>
    /// <param name="targetLong">Trophy grade condition target parameter in long.</param>
    /// <param name="codeString">Trophy grade condition code parameter in string.</param>
    /// <param name="codeLong">Trophy grade condition code parameter in long.</param>
    public void Update(ConditionType conditionType, long count = 1, string targetString = "", long targetLong = 0, string codeString = "", long codeLong = 0) {
        foreach (AchievementMetadata metadata in session.AchievementMetadata.GetType(conditionType)) {
            if (!TrophyReportRules.Counts(metadata)) {
                continue;
            }
            IDictionary<int, Achievement> achievements = metadata.AccountWide ? accountValues : characterValues;
            if (!achievements.TryGetValue(metadata.Id, out Achievement? achievement) || !metadata.Grades.TryGetValue(achievement.CurrentGrade, out AchievementMetadataGrade? grade)) {
                int lowestgradeValue = metadata.Grades.Keys.Min();
                grade = metadata.Grades[lowestgradeValue];
            }

            if (!grade.Condition.Check(session, targetString, targetLong, codeString, codeLong)) {
                continue;
            }
            if (achievement != null && !Advances(conditionType, achievement.Counter, count)) {
                continue;
            }

            if (achievement == null) {
                achievement = new Achievement(metadata) {
                    CurrentGrade = grade.Grade,
                    RewardGrade = grade.Grade,
                };
                GameStorage.Request db = session.GameStorage.Context();
                achievement = db.CreateAchievement(metadata.AccountWide ? session.AccountId : session.CharacterId, achievement);
                if (achievement == null) {
                    throw new InvalidOperationException($"Failed to create achievement: {metadata.Id}");
                }
                achievements.Add(metadata.Id, achievement);
            }

            if (!RankUp(conditionType, achievement, count)) {
                session.Send(AchievementPacket.Update(achievement));
            }
        }
    }

    /// <summary>
    /// Whether a count moves a trophy that the player already has. The gear score counter keeps the highest score
    /// (RankUp), and every stats refresh reports the score (StatsManager.Refresh, also on each buff with stats), so a
    /// score at or below the counter changes nothing and sends no Update. The kill streak counter keeps the longest streak
    /// the same way, and interior_level the highest home decoration level. Other conditions add the count.
    /// </summary>
    public static bool Advances(ConditionType conditionType, long counter, long count) {
        if (conditionType is ConditionType.interior_point) {
            return InteriorPointRules.Advances(counter, count);
        }
        return conditionType is not (ConditionType.item_gear_score or ConditionType.killcount or ConditionType.interior_level) || count > counter;
    }

    /// <summary>
    /// Checks if trophy has reached a new grade. Provides rewards only on certain reward types.
    /// </summary>
    /// <param name="conditionType">ConditionType to check for trophy rank up.</param>
    /// <param name="achievement">Trophy entry from player</param>
    /// <param name="count">Count amount to increment on for the trophy.</param>
    /// <returns>False if there is no rank up possible or condition value has not been met.</returns>
    // KMS2 tells the whole guild of each trophy grade a member earns (Guild 35).
    private void NotifyGuild(int trophyId, int grade, bool lastGrade) {
        long guildId = session.Player.Value.Character.GuildId;
        if (guildId == 0) {
            return;
        }

        try {
            session.World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                TrophyNotice = new GuildRequest.Types.TrophyNotice {
                    GuildId = guildId,
                    TrophyId = trophyId,
                    Grade = grade,
                    LastGrade = lastGrade,
                },
            });
        } catch (Exception ex) {
            logger.Warning(ex, "Guild trophy notice failed: {TrophyId}", trophyId);
        }
    }

    private bool RankUp(ConditionType conditionType, Achievement achievement, long count = 1) {
        // Are there more conditions that should be added here?
        if (conditionType is ConditionType.item_gear_score or ConditionType.killcount or ConditionType.interior_level) {
            achievement.Counter = Math.Max(achievement.Counter, count); // Using Math.Max to ensure gear score doesn't decrease.
        } else if (conditionType is ConditionType.interior_point) {
            achievement.Counter = InteriorPointRules.Counter(achievement.Counter, count);
        } else {
            achievement.Counter += count;
        }

        if (!achievement.Metadata.Grades.TryGetValue(achievement.CurrentGrade, out AchievementMetadataGrade? grade)) {
            return false;
        }

        int newGradesCount = 0;
        while (achievement.Counter >= grade.Condition.Value) {
            if (achievement.Completed) {
                break;
            }
            achievement.Grades.Add(achievement.CurrentGrade, DateTime.Now.ToEpochSeconds());
            newGradesCount++;
            NotifyGuild(achievement.Id, achievement.CurrentGrade, achievement.Completed);

            if (achievement.Grades.Count < achievement.Metadata.Grades.Count) {
                achievement.CurrentGrade++;
            }

            // Update count on player
            switch (achievement.Category) {
                case AchievementCategory.Combat:
                    session.Player.Value.Character.AchievementInfo.Combat++;
                    break;
                case AchievementCategory.Adventure:
                    session.Player.Value.Character.AchievementInfo.Adventure++;
                    break;
                case AchievementCategory.Life:
                    session.Player.Value.Character.AchievementInfo.Lifestyle++;
                    break;
                default:
                    continue; // Invalid category, just skip.
            }

            GiveReward(achievement);

            session.Send(AchievementPacket.Update(achievement));
            session.ConditionUpdate(ConditionType.revise_achieve_multi_grade, codeLong: achievement.Id, targetLong: achievement.CurrentGrade);
            session.ConditionUpdate(ConditionType.revise_achieve_single_grade, codeLong: achievement.Id, targetLong: achievement.CurrentGrade);
            session.ConditionUpdate(ConditionType.hero_achieve, codeLong: achievement.Id, targetLong: achievement.CurrentGrade);
            session.ConditionUpdate(ConditionType.trophy_point, codeLong: achievement.Id);
            // hero_achieve_grade (trophies 21240001, 21100019, 23100290): each grade a trophy of the condition's
            // code gains adds 1. KMS2 sends the update after the trophy_point one.
            session.ConditionUpdate(ConditionType.hero_achieve_grade, codeLong: achievement.Id);
            if (!achievement.Metadata.Grades.TryGetValue(achievement.CurrentGrade, out grade)) {
                break;
            }
        }

        if (newGradesCount <= 0) {
            return false;
        }

        session.Dungeon.UpdateDungeonEnterLimit();
        return true;
    }

    /// <summary>
    /// A reward is due only for a grade that is finished. The reward grade can be ahead of the finished grades after
    /// the GM debug "down" button took a grade back; that grade's reward was given and is not given again, and the
    /// next grade's reward waits until that grade is finished.
    /// </summary>
    public static bool RewardDue(Achievement achievement) {
        return achievement.CurrentGrade >= achievement.RewardGrade && achievement.Grades.ContainsKey(achievement.RewardGrade);
    }

    /// <summary>
    /// The GM debug "down" button: the last finished grade is taken back and the counter set to the goal of the grade
    /// before it. The reward grade is kept, so a reward that was given is not given again.
    /// </summary>
    /// <returns>false if no grade was finished (the counter is set to 0).</returns>
    public static bool DebugGradeBack(Achievement achievement) {
        if (achievement.Grades.Count == 0) {
            achievement.Counter = 0;
            return false;
        }
        int last = achievement.Grades.Keys.Max();
        achievement.Grades.Remove(last);
        achievement.CurrentGrade = last;
        achievement.Counter = achievement.Metadata.Grades.TryGetValue(last - 1, out AchievementMetadataGrade? before) ? before.Condition.Value : 0;
        return true;
    }

    /// <summary>
    /// Gives rewards (if applicable) from awarded trophy grades. If there is no reward, it'll increment up on the reward grade. Will not increment if there is a item, title, skill point, or attribute point to give.
    /// </summary>
    /// <param name="achievement">Achievement entry from user</param>
    /// <param name="manualClaim">If true, assumes player requested the reward. it will give the player rewards for items, titles, skill points, and attribute points.
    /// These are never given automatically upon newly awarded trophy grade.</param>
    private void GiveReward(Achievement achievement, bool manualClaim = false) {
        if (session.Field is null) return;
        if (!RewardDue(achievement)) {
            return;
        }

        if (!achievement.Metadata.Grades.TryGetValue(achievement.RewardGrade, out AchievementMetadataGrade? grade)) {
            return;
        }

        bool hasMoreGrades = achievement.Metadata.Grades.Count > achievement.CurrentGrade;
        // If an achievement has still more grade then we need to make sure the reward grade
        // does not exceed the current grade. Else it will not show the correct trophy in
        // the UI but rather the one past.
        if (grade.Reward == null && hasMoreGrades) {
            achievement.RewardGrade = Math.Min(achievement.RewardGrade + 1, achievement.CurrentGrade);
            return;
        }

        // If an achievement has no reward and no further grade we need to push the reward grade
        // past the current grade to mark it as fully completed. Else it will not show the crown
        // and completion date for the trophy but rather the claim button which is not correct.
        if (grade.Reward == null) {
            achievement.RewardGrade++;
            return;
        }

        switch (grade.Reward.Type) {
            case AchievementRewardType.item:
                if (!manualClaim) {
                    return;
                }
                Item? item = session.Field?.ItemDrop.CreateItem(grade.Reward.Code, grade.Reward.Rank, grade.Reward.Value);
                if (item == null) {
                    return;
                }
                if (!session.Item.Inventory.Add(item, true)) {
                    session.Item.MailItem(item);
                }
                break;
            case AchievementRewardType.title:
                if (!manualClaim) {
                    return;
                }
                if (session.Player.Value.Unlock.HasTitle(grade.Reward.Code)) {
                    break;
                }
                session.Send(UserEnvPacket.AddTitle(grade.Reward.Code));
                session.Player.Value.Unlock.AddTitle(grade.Reward.Code);
                break;
            case AchievementRewardType.dynamicaction:
                if (session.Player.Value.Unlock.Emotes.Contains(grade.Reward.Code)) {
                    break;
                }
                session.Player.Value.Unlock.Emotes.Add(grade.Reward.Code);
                session.Send(EmotePacket.Learn(new Emote(grade.Reward.Code)));
                break;
            case AchievementRewardType.skillpoint:
                session.Config.AddSkillPoint(SkillPointSource.Trophy, grade.Reward.Value, (short) (grade.Reward.Rank - 1));
                break;
            case AchievementRewardType.statpoint:
                session.Config.AddStatPoint(AttributePointSource.Trophy, grade.Reward.Value);
                break;
            case AchievementRewardType.beauty_hair:
            case AchievementRewardType.beauty_makeup:
            case AchievementRewardType.beauty_skin:
            case AchievementRewardType.itemcoloring:
            case AchievementRewardType.shop_build:
            case AchievementRewardType.shop_ride:
            case AchievementRewardType.shop_weapon:
            case AchievementRewardType.etc: // currently used as quest unlocks
                // I don't think anything is supposed to happen here. Just client sided visuals?
                break;
            default:
                logger.Error("Unimplemented trophy reward type {RewardType}", grade.Reward.Type);
                break;
        }
        achievement.RewardGrade++;
    }

    /// <summary>
    /// Claims the reward of one trophy grade, as KMS2 gives one reward for each claim request. The reward grade then
    /// moves on over the finished grades whose reward needs no claim, and stops at the next item or title.
    /// </summary>
    /// <param name="achievementId"></param>
    public void ClaimReward(int achievementId) {
        if (!TryGetAchievement(achievementId, out Achievement? achievement)) {
            return;
        }

        int rewardGrade = achievement.RewardGrade;
        GiveReward(achievement, true);
        while (achievement.RewardGrade != rewardGrade) {
            rewardGrade = achievement.RewardGrade;
            GiveReward(achievement);
        }
        session.Send(AchievementPacket.Update(achievement));
    }

    public bool HasAchievement(int achievementId, int grade = -1) {
        if (!TryGetAchievement(achievementId, out Achievement? achievement)) {
            return false;
        }

        if (grade <= 0) {
            return achievement.Grades.Count > 0;
        }

        return achievement.Grades.ContainsKey(grade);
    }

    // GM debug buttons of the trophy window (Achieve 5). Rewards already given are not taken back by "down".
    public void DebugButton(int achievementId, byte button) {
        if (!DebugButtonRules.KnownAchieveButton(button)) {
            return;
        }
        if (!session.AchievementMetadata.TryGet(achievementId, out AchievementMetadata? metadata) || metadata.Grades.Count == 0) {
            return;
        }

        IDictionary<int, Achievement> achievements = metadata.AccountWide ? accountValues : characterValues;
        if (!achievements.TryGetValue(achievementId, out Achievement? achievement)) {
            if (button == 1) {
                return;
            }
            int lowest = metadata.Grades.Keys.Min();
            achievement = new Achievement(metadata) {
                CurrentGrade = lowest,
                RewardGrade = lowest,
            };
            using GameStorage.Request db = session.GameStorage.Context();
            achievement = db.CreateAchievement(metadata.AccountWide ? session.AccountId : session.CharacterId, achievement);
            if (achievement == null) {
                return;
            }
            achievements.Add(achievementId, achievement);
        }

        if (button == 1) {
            if (!DebugGradeBack(achievement)) {
                session.Send(AchievementPacket.Update(achievement));
                return;
            }
            AchievementInfo info = session.Player.Value.Character.AchievementInfo;
            switch (achievement.Category) {
                case AchievementCategory.Combat:
                    info.Combat = Math.Max(0, info.Combat - 1);
                    break;
                case AchievementCategory.Adventure:
                    info.Adventure = Math.Max(0, info.Adventure - 1);
                    break;
                case AchievementCategory.Life:
                    info.Lifestyle = Math.Max(0, info.Lifestyle - 1);
                    break;
            }
            session.Send(AchievementPacket.Update(achievement));
            return;
        }

        if (achievement.Completed || !metadata.Grades.TryGetValue(achievement.CurrentGrade, out AchievementMetadataGrade? grade)
            || !Util.DebugButtonRules.TryUpCounter(button, grade.Condition.Value, out long counter)) {
            return;
        }

        achievement.Counter = Math.Max(achievement.Counter, counter);
        if (!RankUp(grade.Condition.Type, achievement, 0)) {
            session.Send(AchievementPacket.Update(achievement));
        }
    }

    public void DebugCompleteAllTrophies() {
        ICollection<AchievementMetadata> achievementMetadataCollection = session.AchievementMetadata.GetAll();
        foreach (AchievementMetadata metadata in achievementMetadataCollection) {
            int trophyId = metadata.Id;
            int maxGrade = metadata.Grades.Keys.Max();

            if (!TryGetAchievement(trophyId, out Achievement? achievement)) {
                achievement = new Achievement(metadata) {
                    CurrentGrade = metadata.Grades.Keys.Min(),
                    RewardGrade = metadata.Grades.Keys.Min(),
                };
                GameStorage.Request db = session.GameStorage.Context();
                achievement = db.CreateAchievement(metadata.AccountWide ? session.AccountId : session.CharacterId, achievement);
                if (achievement == null) {
                    throw new InvalidOperationException($"Failed to create achievement: {metadata.Id}");
                }
                if (metadata.AccountWide) {
                    accountValues.TryAdd(metadata.Id, achievement);
                } else {
                    characterValues.TryAdd(metadata.Id, achievement);
                }
            }

            for (int grade = achievement.CurrentGrade; grade <= maxGrade; grade++) {
                if (achievement.Grades.ContainsKey(grade)) {
                    achievement.Grades[grade] = DateTime.Now.ToEpochSeconds();
                    GiveReward(achievement);
                    continue;
                }
                achievement.Grades.Add(grade, DateTime.Now.ToEpochSeconds());
                GiveReward(achievement);
            }
            achievement.CurrentGrade = maxGrade;
        }
    }

    public void Save(GameStorage.Request db) {
        db.SaveAchievements(session.AccountId, accountValues.Values.ToList());
        db.SaveAchievements(session.CharacterId, characterValues.Values.ToList());
    }
}
