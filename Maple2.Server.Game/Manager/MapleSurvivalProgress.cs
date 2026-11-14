using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
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

    // The open period, which the account enters first (SurvivalRules.EnterSeason: a new period starts the survival
    // level and the pass again). Every read of the account's survival level or pass goes through here. The restored
    // client is told of the new period (0x18: the season-record line, then the pass and lobby refresh); the packets
    // that follow carry the new level and season.
    private static MapleSurvivalTable.Period? Period(GameSession session, MapleSurvivalTable? table) {
        MapleSurvivalTable.Period? period = table == null ? null : SurvivalRules.CurrentPeriod(table, DateTime.Now);
        if (period != null && SurvivalRules.EnterSeason(session.Player.Value.Account, period.Id)) {
            Logger.Information("Maple Survival period {Period}: account {AccountId} starts its survival level and pass again",
                period.Id, session.Player.Value.Account.Id);
            if (MapleSurvivalRules.RestoredClient && SurvivalRules.On(session.ServerTableMetadata.FeatureTable)) {
                session.Send(MapleSurvivalPacket.NewSeason());
            }
        }
        return period;
    }

    // The account enters the open period (SurvivalManager.Load, before it sends the stats).
    public static void EnterPeriod(GameSession session) {
        Period(session, Table(session));
    }

    // After each game server entry and on the lobby's refresh (0xC2 0x12).
    // The 0x02 match box's character: the one this season's ranking shows. 0 (no game recorded in the current
    // season yet) adds the line that this character's name will appear in the season's ranking.
    public static long RankingCharacter(GameSession session) {
        MapleSurvivalTable.Period? period = Period(session, Table(session));
        if (period == null) {
            return session.CharacterId;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        return db.GetSurvivalRecord(session.CharacterId, period.Id).Games == 0 ? 0 : session.CharacterId;
    }

    public static void SendLobby(GameSession session) {
        Account account = session.Player.Value.Account;
        MapleSurvivalTable? table = Table(session);
        MapleSurvivalTable.Period? period = Period(session, table);
        // The first survival level is 1 (survivallevel.xml starts there); an account that never played has 0 stored.
        if (account.SurvivalLevel < 1) {
            account.SurvivalLevel = 1;
        }
        if (period != null) {
            using GameStorage.Request db = session.GameStorage.Context();
            SurvivalSeasonRecord record = db.GetSurvivalRecord(session.CharacterId, period.Id);
            long end = period.EndTime == DateTime.MaxValue ? 0 : new DateTimeOffset(period.EndTime).ToUnixTimeSeconds();
            session.Send(MapleSurvivalPacket.Season(period.Id, true, end, $"Season {period.Id}", record.Rating, record.Games, record.Wins, record.Kills));
        }
        session.Survival.Load(); // the record (0x17) and the skins (0x1e)
        // The lobby's level, exp and reward level (0x15) last: the record (0x17) sets the same level and exp but
        // refreshes only the pass window, and 0x12 sets only the reward level, so the lobby showed level 0.
        session.Send(MapleSurvivalPacket.LevelProgress(account.SurvivalExp, account.SurvivalLevel, account.SurvivalLevel));
    }

    // 0xC2 0x22: the paid pass in merets. The pass window sends (true, the season's passPrice); while a
    // MapleSurvivalPassBundle event runs it opens the buy-bundle window instead, which sends (false, value1) for the pass
    // alone and (true, value2) for the pass with the bonus item (maplesurvivalbuybundledialog.cpp OnBuyDefault /
    // OnBuyBundle). The price must be one of those; a ShutdownMapleSurvivalPass event closes the buy.
    public static void BuyPass(GameSession session, bool paid, long price) {
        Account account = session.Player.Value.Account;
        MapleSurvivalTable? table = Table(session);
        MapleSurvivalTable.Period? period = Period(session, table);
        MapleSurvivalPassBundle? bundle = session.FindEvent(GameEventType.MapleSurvivalPassBundle)
            .Select(gameEvent => gameEvent.Metadata.Data).OfType<MapleSurvivalPassBundle>().FirstOrDefault();
        (long Cost, int BonusItemId)? purchase = period == null ? null : PassPurchase(period.PassPrice, bundle, paid, price);
        // The bonus item is made before the merets are taken (no field during a field change: refused).
        Item? bonus = purchase is { BonusItemId: not 0 } ? session.Field?.ItemDrop.CreateItem(purchase.Value.BonusItemId, 1, 1) : null;
        if (!SurvivalRules.PassOn(session.ServerTableMetadata.FeatureTable) || purchase == null || account.ActiveGoldPass
            || MapleSurvivalHandler.Shutdown(session, GameEventType.ShutdownMapleSurvivalPass)
            || (purchase.Value.BonusItemId != 0 && bonus == null) || !session.Currency.TryTakeMeret(purchase.Value.Cost)) {
            session.Send(SurvivalPacket.UpdateStats(account));
            return;
        }
        account.ActiveGoldPass = true;
        if (bonus != null && !session.Item.Inventory.Add(bonus, true)) {
            session.Item.MailItem(bonus);
        }
        // survival_buy_gold_pass (23200042 "(Account) Royalety: Activate {0} Gold Royale Pass").
        session.ConditionUpdate(ConditionType.survival_buy_gold_pass);
        session.Send(MapleSurvivalPacket.PassProgress(0, 0));
        session.Send(SurvivalPacket.UpdateStats(account));
    }

    // The merets a pass buy takes and its bonus item (0: none), or null when the price is none the windows send.
    public static (long Cost, int BonusItemId)? PassPurchase(long passPrice, MapleSurvivalPassBundle? bundle, bool paid, long price) {
        if (bundle != null) {
            if (!paid && price == bundle.DefaultPrice) {
                return (price, 0);
            }
            if (paid && price == bundle.BundlePrice) {
                return (price, bundle.ItemId);
            }
        }
        return paid && price == passPrice ? (passPrice, 0) : null;
    }

    // 0xC2 0x23: the free and paid pass levels the client claims (counts after those claimed); the rewards of the
    // season's survivalRewardID, then the progress (0x23) and the record (0x17).
    public static void ClaimPass(GameSession session, int free, int paid) {
        Account account = session.Player.Value.Account;
        MapleSurvivalTable? table = Table(session);
        MapleSurvivalTable.Period? period = Period(session, table);
        // Without a field (a field change under way) no reward item can be made: the claim is refused, nothing is
        // marked claimed.
        if (!SurvivalRules.PassOn(session.ServerTableMetadata.FeatureTable) || table == null || period == null || session.Field == null
            || MapleSurvivalHandler.Shutdown(session, GameEventType.ShutdownMapleSurvivalPass)) {
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

    // An item (genderItem: the male entry, then the female one) or a buff (additionalEffect, Values the level). The
    // item given (null for a buff or none).
    private static ResultItem? Give(GameSession session, MapleSurvivalTable.Reward reward) {
        int index = reward.Type == "genderItem" && session.Player.Value.Character.Gender == Gender.Female ? 1 : 0;
        if (index >= reward.Ids.Length) {
            index = 0;
        }
        if (reward.Ids.Length == 0) {
            return null;
        }
        int id = reward.Ids[index];
        int value = index < reward.Values.Length ? reward.Values[index] : 1;
        int count = index < reward.Counts.Length ? reward.Counts[index] : 1;
        switch (reward.Type) {
            case "item":
            case "genderItem":
                Item? item = session.Field?.ItemDrop.CreateItem(id, Math.Max(1, value), Math.Max(1, count));
                if (item == null) {
                    return null;
                }
                var given = new ResultItem(item.Id, (short) item.Rarity, item.Amount);
                if (!session.Item.Inventory.Add(item, true)) {
                    session.Item.MailItem(item);
                }
                return given;
            case "additionalEffect":
                session.Player.Buffs.AddBuff(session.Player, session.Player, id, (short) Math.Max(1, value), session.Field?.FieldTick ?? 0);
                return null;
            default:
                Logger.Warning("Maple Survival reward type {Type} not given", reward.Type);
                return null;
        }
    }

    // An item a game's level rewards gave, as the result window (0x11) shows it.
    public readonly record struct ResultItem(int Id, short Rarity, int Amount);

    // The exp with the level rewards of the levels reached; the items given go to items when there is one. The level
    // rewards are given only in the level-reward mode (SurvivalContents03 off, SurvivalRules.PassFeature); in the
    // pass mode the levels give the pass rewards to claim instead.
    private static void AddExp(GameSession session, MapleSurvivalTable table, ref int level, ref long exp, long gained, List<ResultItem>? items = null) {
        bool levelRewards = !SurvivalRules.PassOn(session.ServerTableMetadata.FeatureTable);
        foreach (int reached in SurvivalRules.AddExp(table, ref level, ref exp, gained)) {
            if (levelRewards && table.LevelRewards.TryGetValue(reached, out IReadOnlyList<MapleSurvivalTable.Reward>? rewards)) {
                foreach (MapleSurvivalTable.Reward reward in rewards) {
                    if (Give(session, reward) is { } item) {
                        items?.Add(item);
                    }
                }
            }
        }
    }

    // An item's survival exp (SurvivalLevelExp) can be added: there is a level above the account's. At the last
    // level SurvivalRules.AddExp keeps 0 exp, so the item would add nothing.
    public static bool CanAddItemExp(GameSession session) {
        MapleSurvivalTable? table = Table(session);
        Period(session, table);
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
        Period(session, table);
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
    // 16 result ints (0x11) with the items the level rewards gave (the window's reward slots).
    // squad: the player played in a squad; its rating goes to the squad season's record (SurvivalRules.SquadSeasonOffset).
    public static int[] Result(GameSession session, int rank, int participants, int kills, int seconds, out List<ResultItem> items, bool squad = false) {
        items = [];
        Account account = session.Player.Value.Account;
        MapleSurvivalTable? table = Table(session);
        MapleSurvivalTable.Period? period = Period(session, table);
        int ratingBefore = 0, ratingAfter = 0;
        bool inSeason = false;
        int levelBefore = Math.Max(1, account.SurvivalLevel);
        long expBefore = account.SurvivalExp;
        long gained = SurvivalRules.GameExp(rank, participants, kills);
        int level = levelBefore;
        long exp = expBefore;
        if (table != null) {
            AddExp(session, table, ref level, ref exp, gained, items);
            account.SurvivalLevel = level;
            account.SurvivalExp = exp;
            int seasonId = squad
                ? SquadSeason(session) is > 0 and int squadSeason ? SurvivalRules.SquadSeasonOffset + squadSeason : 0
                : period?.Id ?? 0;
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
