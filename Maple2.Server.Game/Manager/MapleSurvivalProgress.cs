using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Manager;

// The restored client's Maple Survival lobby and pass: the season (0x16), the own record (0x17), the
// lobby value (0x12), the skins (0x1e), the pass buy (0xC2 0x22) and claim (0xC2 0x23), and what a game's result
// adds. The account keeps the level, exp and pass (Account.Survival*), the survival-record table the season rating.
public static class MapleSurvivalProgress {
    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(MapleSurvivalProgress));

    private static MapleSurvivalTable? Table(GameSession session) {
        try {
            return session.TableMetadata.MapleSurvivalTable;
        } catch (InvalidOperationException) {
            return null;
        }
    }

    // After each game server entry and on the lobby's refresh (0xC2 0x12).
    public static void SendLobby(GameSession session) {
        Account account = session.Player.Value.Account;
        // The first survival level is 1 (survivallevel.xml starts there); an account that never played has 0 stored.
        if (account.SurvivalLevel < 1) {
            account.SurvivalLevel = 1;
        }
        MapleSurvivalTable? table = Table(session);
        MapleSurvivalTable.Period? period = table == null ? null : SurvivalRules.CurrentPeriod(table, DateTime.Now);
        if (period != null) {
            using GameStorage.Request db = session.GameStorage.Context();
            SurvivalSeasonRecord record = db.GetSurvivalRecord(session.CharacterId, period.Id);
            long end = period.EndTime == DateTime.MaxValue ? 0 : new DateTimeOffset(period.EndTime).ToUnixTimeSeconds();
            session.Send(MapleSurvivalPacket.Season(period.Id, true, end, $"Season {period.Id}", record.Rating, record.Games, record.Wins, record.Kills));
        }
        session.Send(MapleSurvivalPacket.LobbyValue(Math.Max(1, account.SurvivalLevel)));
        session.Survival.Load(); // the record (0x17) and the skins (0x1e)
    }

    // 0xC2 0x22: the paid pass for the season's passPrice in merets (the price the client sends must be the table's).
    public static void BuyPass(GameSession session, bool paid, long price) {
        Account account = session.Player.Value.Account;
        MapleSurvivalTable? table = Table(session);
        MapleSurvivalTable.Period? period = table == null ? null : SurvivalRules.CurrentPeriod(table, DateTime.Now);
        if (!paid || period == null || account.ActiveGoldPass || price != period.PassPrice || !session.Currency.TryTakeMeret(period.PassPrice)) {
            session.Send(SurvivalPacket.UpdateStats(account));
            return;
        }
        account.ActiveGoldPass = true;
        // survival_buy_gold_pass (23200042 "(Account) Royalety: Activate {0} Gold Royale Pass").
        session.ConditionUpdate(ConditionType.survival_buy_gold_pass);
        session.Send(MapleSurvivalPacket.PassProgress(0, 0));
        session.Send(SurvivalPacket.UpdateStats(account));
    }

    // 0xC2 0x23: the free and paid pass levels the client claims (counts after those claimed); the rewards of the
    // season's survivalRewardID, then the progress (0x23) and the record (0x17).
    public static void ClaimPass(GameSession session, int free, int paid) {
        Account account = session.Player.Value.Account;
        MapleSurvivalTable? table = Table(session);
        MapleSurvivalTable.Period? period = table == null ? null : SurvivalRules.CurrentPeriod(table, DateTime.Now);
        // Without a field (a field change under way) no reward item can be made: the claim is refused, nothing is
        // marked claimed.
        if (table == null || period == null || session.Field == null) {
            session.Send(SurvivalPacket.UpdateStats(account));
            return;
        }
        (int freeTo, int paidTo) = SurvivalRules.Claim(account.SurvivalLevel, account.SurvivalSilverLevelRewardClaimed,
            account.SurvivalGoldLevelRewardClaimed, account.ActiveGoldPass, free, paid);
        int freeCount = freeTo - account.SurvivalSilverLevelRewardClaimed;
        int paidCount = paidTo - account.SurvivalGoldLevelRewardClaimed;
        for (int level = account.SurvivalSilverLevelRewardClaimed + 1; level <= freeTo; level++) {
            Give(session, table.FreePass, period.RewardId, level);
        }
        for (int level = account.SurvivalGoldLevelRewardClaimed + 1; level <= paidTo; level++) {
            Give(session, table.PaidPass, period.RewardId, level);
        }
        account.SurvivalSilverLevelRewardClaimed = freeTo;
        account.SurvivalGoldLevelRewardClaimed = paidTo;
        session.Send(MapleSurvivalPacket.PassProgress(freeCount, paidCount));
        session.Send(SurvivalPacket.UpdateStats(account));
    }

    private static void Give(GameSession session, IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>> pass, int rewardId, int level) {
        if (pass.TryGetValue(rewardId, out IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>? levels)
            && levels.TryGetValue(level, out IReadOnlyList<MapleSurvivalTable.Reward>? rewards)) {
            foreach (MapleSurvivalTable.Reward reward in rewards) {
                Give(session, reward);
            }
        }
    }

    // An item (genderItem: the male entry, then the female one) or a buff (additionalEffect, Values the level).
    private static void Give(GameSession session, MapleSurvivalTable.Reward reward) {
        int index = reward.Type == "genderItem" && session.Player.Value.Character.Gender == Gender.Female ? 1 : 0;
        if (index >= reward.Ids.Length) {
            index = 0;
        }
        if (reward.Ids.Length == 0) {
            return;
        }
        int id = reward.Ids[index];
        int value = index < reward.Values.Length ? reward.Values[index] : 1;
        int count = index < reward.Counts.Length ? reward.Counts[index] : 1;
        switch (reward.Type) {
            case "item":
            case "genderItem":
                Item? item = session.Field?.ItemDrop.CreateItem(id, Math.Max(1, value), Math.Max(1, count));
                if (item != null && !session.Item.Inventory.Add(item, true)) {
                    session.Item.MailItem(item);
                }
                return;
            case "additionalEffect":
                session.Player.Buffs.AddBuff(session.Player, session.Player, id, (short) Math.Max(1, value), session.Field?.FieldTick ?? 0);
                return;
            default:
                Logger.Warning("Maple Survival reward type {Type} not given", reward.Type);
                return;
        }
    }

    // The exp with the level rewards of the levels reached.
    private static void AddExp(GameSession session, MapleSurvivalTable table, ref int level, ref long exp, long gained) {
        foreach (int reached in SurvivalRules.AddExp(table, ref level, ref exp, gained)) {
            if (table.LevelRewards.TryGetValue(reached, out IReadOnlyList<MapleSurvivalTable.Reward>? rewards)) {
                foreach (MapleSurvivalTable.Reward reward in rewards) {
                    Give(session, reward);
                }
            }
        }
    }

    // An item's survival exp (SurvivalLevelExp) can be added: there is a level above the account's. At the last
    // level SurvivalRules.AddExp keeps 0 exp, so the item would add nothing.
    public static bool CanAddItemExp(GameSession session) {
        MapleSurvivalTable? table = Table(session);
        return table != null && table.LevelExp.ContainsKey(Math.Max(1, session.Player.Value.Account.SurvivalLevel) + 1);
    }

    // An item's survival exp: the level rewards of the levels reached, then the record (0x17) with the exp gained and
    // for the restored client the lobby's level and exp (0x15: the record only refreshes the pass window, so the lobby
    // kept the old level).
    public static void AddItemExp(GameSession session, long gained) {
        MapleSurvivalTable? table = Table(session);
        if (table == null) {
            return;
        }
        Account account = session.Player.Value.Account;
        int level = Math.Max(1, account.SurvivalLevel);
        long exp = account.SurvivalExp;
        AddExp(session, table, ref level, ref exp, gained);
        account.SurvivalLevel = level;
        account.SurvivalExp = exp;
        session.Send(SurvivalPacket.UpdateStats(account, gained));
        if (MapleSurvivalRules.RestoredClient) {
            session.Send(MapleSurvivalPacket.LevelProgress(exp, level, level));
        }
    }

    private static int SquadSeason(GameSession session) {
        try {
            return SurvivalRules.SeasonAt(session.TableMetadata.SeasonDataTable.SurvivalSquad, DateTime.Now);
        } catch (InvalidOperationException) {
            return 0;
        }
    }

    // A game's result for the player (its rank among the participants, its kills, the seconds played): the season
    // rating and record, the survival exp (with the level rewards of the levels reached), the record (0x17) and the
    // 16 result ints (0x11).
    // squad: the player played in a squad; its rating goes to the squad season's record (SurvivalRules.SquadSeasonOffset).
    public static int[] Result(GameSession session, int rank, int participants, int kills, int seconds, bool squad = false) {
        Account account = session.Player.Value.Account;
        MapleSurvivalTable? table = Table(session);
        int ratingBefore = 0, ratingAfter = 0;
        bool inSeason = false;
        int levelBefore = Math.Max(1, account.SurvivalLevel);
        long expBefore = account.SurvivalExp;
        long gained = SurvivalRules.GameExp(rank, participants, kills);
        int level = levelBefore;
        long exp = expBefore;
        if (table != null) {
            AddExp(session, table, ref level, ref exp, gained);
            account.SurvivalLevel = level;
            account.SurvivalExp = exp;
            int seasonId = squad
                ? SquadSeason(session) is > 0 and int squadSeason ? SurvivalRules.SquadSeasonOffset + squadSeason : 0
                : SurvivalRules.CurrentPeriod(table, DateTime.Now)?.Id ?? 0;
            if (seasonId != 0) {
                using GameStorage.Request db = session.GameStorage.Context();
                SurvivalSeasonRecord record = db.GetSurvivalRecord(session.CharacterId, seasonId);
                ratingBefore = record.Rating;
                record.Rating = Math.Max(0, record.Rating + SurvivalRules.RatingChange(rank, participants));
                record.Games++;
                record.Wins += rank == 1 ? 1 : 0;
                record.Kills += kills;
                ratingAfter = record.Rating;
                inSeason = true;
                if (!db.SaveSurvivalRecord(session.CharacterId, record)) {
                    Logger.Warning("Maple Survival record of {CharacterId} not saved", session.CharacterId);
                }
            }
        }
        session.Send(SurvivalPacket.UpdateStats(account, gained));
        // The players go back by a field change on the same channel (no game server entry): the lobby gets its season
        // (the new rating) and level again here.
        SendLobby(session);
        return SurvivalRules.ResultValues(rank, participants, kills, inSeason, ratingBefore, ratingAfter, levelBefore, expBefore, gained, level, exp,
            seconds, session.Player.Value.Character.Level, session.Player.Value.Account.PrestigeLevel);
    }
}
