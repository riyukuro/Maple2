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
        session.Send(MapleSurvivalPacket.LevelProgress(account.SurvivalExp, Math.Max(1, account.SurvivalLevel), Math.Max(1, account.SurvivalLevel)));
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
        // The bonus item is made before the merets are taken (an item that cannot be made: refused).
        Item? bonus = purchase is { BonusItemId: not 0 } ? session.Item.CreateItem(purchase.Value.BonusItemId, 1, 1) : null;
        if (!SurvivalRules.PassOn(session.ServerTableMetadata.FeatureTable) || purchase == null || account.ActiveGoldPass
            || MapleSurvivalHandler.Shutdown(session, GameEventType.ShutdownMapleSurvivalPass)
            || (purchase.Value.BonusItemId != 0 && bonus == null) || !session.Currency.TryTakeMeret(purchase.Value.Cost)) {
            session.Send(SurvivalPacket.UpdateStats(account));
            return;
        }
        account.ActiveGoldPass = true;
        if (bonus != null && (session.Field == null || !session.Item.Inventory.Add(bonus, true))) {
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
        // Without a field (a field change under way) the reward items go by mail (EventClaim).
        if (!SurvivalRules.PassOn(session.ServerTableMetadata.FeatureTable) || table == null || period == null
            || MapleSurvivalHandler.Shutdown(session, GameEventType.ShutdownMapleSurvivalPass)) {
            session.Send(SurvivalPacket.UpdateStats(account));
            return;
        }
        (int freeTo, int paidTo) = SurvivalRules.Claim(account.SurvivalLevel, account.SurvivalSilverLevelRewardClaimed,
            account.SurvivalGoldLevelRewardClaimed, account.ActiveGoldPass, free, paid);
        int freeCount = freeTo - account.SurvivalSilverLevelRewardClaimed;
        int paidCount = paidTo - account.SurvivalGoldLevelRewardClaimed;
        var rewards = new Rewards();
        for (int level = account.SurvivalSilverLevelRewardClaimed + 1; level <= freeTo; level++) {
            Collect(session, table.FreePass, period.RewardId, level, rewards);
        }
        for (int level = account.SurvivalGoldLevelRewardClaimed + 1; level <= paidTo; level++) {
            Collect(session, table.PaidPass, period.RewardId, level, rewards);
        }
        // The levels claimed and their items are stored together; when they are not, nothing is claimed.
        if (!Store(session, rewards, account.SurvivalLevel, account.SurvivalExp, freeTo, paidTo)) {
            session.Send(SurvivalPacket.UpdateStats(account));
            return;
        }
        session.Send(MapleSurvivalPacket.PassProgress(freeCount, paidCount));
        session.Send(SurvivalPacket.UpdateStats(account));
    }

    // The rewards of the levels claimed or reached, made but not yet given: the items and the buffs (id, level).
    private sealed class Rewards {
        public readonly List<Item> Items = [];
        public readonly List<(int Id, short Level)> Buffs = [];
    }

    private static void Collect(GameSession session, IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>> pass,
            int rewardId, int level, Rewards rewards) {
        if (pass.TryGetValue(rewardId, out IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>? levels)
            && levels.TryGetValue(level, out IReadOnlyList<MapleSurvivalTable.Reward>? levelRewards)) {
            foreach (MapleSurvivalTable.Reward reward in levelRewards) {
                Collect(session, reward, rewards);
            }
        }
    }

    // The rewards with the progress that gives them (the survival level and exp, the pass levels claimed), stored in
    // one transaction (EventClaim): a crash cannot keep the items and lose the progress, which gave them again. The
    // progress reaches the account once the transaction committed; the buffs are added then. With no item there is
    // nothing stored at once, and the progress is kept in memory for the session save as before. usedUid: a bag item
    // the progress takes (a tonic), stored as used in the same transaction (InventoryManager.SaveConsumed) and taken
    // from the bag after the commit, so a crash cannot keep the progress and give the item back. False when nothing
    // was stored: no item, buff or progress is given and the used item is kept.
    private static bool Store(GameSession session, Rewards rewards, int level, long exp, int silverClaimed, int goldClaimed, long usedUid = 0) {
        Account account = session.Player.Value.Account;
        void Apply() {
            account.SurvivalLevel = level;
            account.SurvivalExp = exp;
            account.SurvivalSilverLevelRewardClaimed = silverClaimed;
            account.SurvivalGoldLevelRewardClaimed = goldClaimed;
        }
        bool taken = true;
        if (rewards.Items.Count == 0 && usedUid == 0) {
            Apply();
        } else if (EventClaim.Give(session, rewards.Items, [],
                       db => db.SaveSurvivalProgress(session.Player, level, exp, silverClaimed, goldClaimed)
                             && (usedUid == 0 || session.Item.Inventory.SaveConsumed(db, usedUid, 1)),
                       () => {
                           Apply();
                           if (usedUid != 0) {
                               taken = session.Item.Inventory.ConsumeStored(usedUid, 1);
                           }
                       }) == null) {
            Logger.Error("Maple Survival progress of {CharacterId} (level {Level}, pass {Silver}/{Gold}) not stored, its rewards not given",
                session.CharacterId, level, silverClaimed, goldClaimed);
            return false;
        }
        // Not taken: the item left the bag between its save and the commit; logged as the other SaveConsumed uses.
        if (!taken) {
            Logger.Error("Maple Survival: item {ItemUid} stored as used but not taken from the bag of {CharacterId}", usedUid, session.CharacterId);
        }
        foreach ((int id, short buffLevel) in rewards.Buffs) {
            session.Player.Buffs.AddBuff(session.Player, session.Player, id, buffLevel, session.Field?.FieldTick ?? 0);
        }
        return true;
    }

    // An item (genderItem: the male entry, then the female one) or a buff (additionalEffect, Values the level), made
    // and added to rewards. An item that cannot be made (an item the played pack lacks) is logged and left out.
    private static void Collect(GameSession session, MapleSurvivalTable.Reward reward, Rewards rewards) {
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
                // A player between fields (Field null for the moment of a field change) gets it by mail (EventClaim).
                Item? item = session.Item.CreateItem(id, Math.Max(1, value), Math.Max(1, count));
                if (item == null) {
                    Logger.Warning("Maple Survival reward item {ItemId} not given to {CharacterId}", id, session.CharacterId);
                    return;
                }
                rewards.Items.Add(item);
                return;
            case "additionalEffect":
                rewards.Buffs.Add((id, (short) Math.Max(1, value)));
                return;
            default:
                Logger.Warning("Maple Survival reward type {Type} not given", reward.Type);
                return;
        }
    }

    // An item a game's level rewards gave, as the result window (0x11) shows it.
    public readonly record struct ResultItem(int Id, short Rarity, int Amount);

    // The exp with the level rewards of the levels reached, stored with the new level and exp (Store); the items given
    // go to items when there is one. The level rewards are given only in the level-reward mode (SurvivalContents03
    // off, SurvivalRules.PassFeature); in the pass mode the levels give the pass rewards to claim instead. False when
    // the rewards and the progress were not stored: the account keeps its level and exp, level and exp come back as
    // they were.
    private static bool AddExp(GameSession session, MapleSurvivalTable table, ref int level, ref long exp, long gained, List<ResultItem>? items = null,
            long usedUid = 0) {
        Account account = session.Player.Value.Account;
        int levelBefore = level;
        long expBefore = exp;
        bool levelRewards = !SurvivalRules.PassOn(session.ServerTableMetadata.FeatureTable);
        var rewards = new Rewards();
        foreach (int reached in SurvivalRules.AddExp(table, ref level, ref exp, gained)) {
            if (levelRewards && table.LevelRewards.TryGetValue(reached, out IReadOnlyList<MapleSurvivalTable.Reward>? levelRewardList)) {
                foreach (MapleSurvivalTable.Reward reward in levelRewardList) {
                    Collect(session, reward, rewards);
                }
            }
        }
        if (!Store(session, rewards, level, exp, account.SurvivalSilverLevelRewardClaimed, account.SurvivalGoldLevelRewardClaimed, usedUid)) {
            level = levelBefore;
            exp = expBefore;
            return false;
        }
        items?.AddRange(rewards.Items.Select(item => new ResultItem(item.Id, (short) item.Rarity, item.Amount)));
        return true;
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
    // kept the old level). The item (tonicUid) is used up with the exp, in the transaction that stores it. False when
    // nothing was added: the level rewards, the progress and the used item were not stored, the item is kept.
    public static bool AddItemExp(GameSession session, long gained, long tonicUid) {
        MapleSurvivalTable? table = Table(session);
        if (table == null) {
            return false;
        }
        Period(session, table);
        Account account = session.Player.Value.Account;
        int level = Math.Max(1, account.SurvivalLevel);
        long exp = account.SurvivalExp;
        if (!AddExp(session, table, ref level, ref exp, gained, usedUid: tonicUid)) {
            session.Send(SurvivalPacket.UpdateStats(account));
            return false;
        }
        session.Send(SurvivalPacket.UpdateStats(account, gained));
        if (MapleSurvivalRules.RestoredClient) {
            session.Send(MapleSurvivalPacket.LevelProgress(exp, level, level));
        }
        return true;
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
            if (!AddExp(session, table, ref level, ref exp, gained, items)) {
                gained = 0;
            }
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
