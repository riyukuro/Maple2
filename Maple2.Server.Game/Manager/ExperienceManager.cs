using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class ExperienceManager {
    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private long Exp {
        get => session.Player.Value.Character.Exp;
        set => session.Player.Value.Character.Exp = value;
    }
    private long RestExp {
        get => session.Player.Value.Character.RestExp;
        set => session.Player.Value.Character.RestExp = value;
    }

    private short Level {
        get => session.Player.Value.Character.Level;
        set => session.Player.Value.Character.Level = value;
    }

    public int PrestigeLevel {
        get => session.Player.Value.Account.PrestigeLevel;
        set => session.Player.Value.Account.PrestigeLevel = value;
    }

    public int PrestigeLevelsGained {
        get => session.Player.Value.Account.PrestigeLevelsGained;
        set => session.Player.Value.Account.PrestigeLevelsGained = value;
    }

    public long PrestigeExp {
        get => session.Player.Value.Account.PrestigeExp;
        set => session.Player.Value.Account.PrestigeExp = value;
    }

    public long PrestigeCurrentExp {
        get => session.Player.Value.Account.PrestigeCurrentExp;
        set => session.Player.Value.Account.PrestigeCurrentExp = value;
    }

    public IList<PrestigeMission> PrestigeMissions => session.Player.Value.Account.PrestigeMissions;
    public IList<int> PrestigeRewardsClaimed => session.Player.Value.Account.PrestigeRewardsClaimed;

    private int ChainKillCount { get; set; }

    public ExperienceManager(GameSession session) {
        this.session = session;
        Init();
    }

    private void Init() {
        if (session.Player.Value.Account.PrestigeMissions.Count == 0) {
            foreach ((int missionId, PrestigeMissionMetadata _) in session.TableMetadata.PrestigeMissionTable.Entries) {
                session.Player.Value.Account.PrestigeMissions.Add(new PrestigeMission(missionId));
            }
        }
    }

    public void ResetChainKill() => ChainKillCount = 0;

    public void OnKill(IActor npc) {
        if (npc is not FieldNpc) {
            return;
        }
        FieldNpc fieldNpc = (npc as FieldNpc)!;
        // TODO: Check if there are level requirements for Chain Kill Count to count ?
        ChainKillCount++;
        float expRate = Lua.CalcKillCountBonusExpRate(ChainKillCount);

        // TODO: Using table ID 2. Need confirmation if particular maps (or dungeons) use a different table
        long expGained = fieldNpc.Value.Metadata.Basic.CustomExp;
        if (fieldNpc.Value.Metadata.Basic.CustomExp < 0) {
            if (!session.TableMetadata.ExpTable.ExpBase.TryGetValue(2, fieldNpc.Value.Metadata.Basic.Level, out expGained)) {
                return;
            }
        }
        expGained = SpendRest((long) (expGained * expRate), kill: true);
        LevelUp();
        session.Send(ExperienceUpPacket.Add(expGained, Exp, RestExp, ExpMessageCode.s_msg_take_exp, npc.ObjectId));
    }

    // Adds a gain and spends rest exp on it (RestExpRules); returns the gain with its bonus.
    private long SpendRest(long expGained, bool kill) {
        (long gain, long rest) = RestExpRules.Spend(expGained, RestExp, kill, Constants.RestExpAcquireRate);
        RestExp = rest;
        Exp += gain;
        return gain;
    }

    // The home rest exp tick (RestExpRules.Tick), sent as ExpUp 1 (SetRestExp) as the client reads it
    // (CSceneStateGame::OnExpUp, scenestategame.cpp:4186). Nothing is sent when the rest exp is at its cap.
    public void TickRestExp() {
        if (!session.TableMetadata.ExpTable.NextExp.TryGetValue(Level, out long nextExp)
            || !session.TableMetadata.ExpTable.ExpBase.TryGetValue(RestExpRules.TickTableId, Level, out long expBase)) {
            return;
        }
        long rest = RestExpRules.Tick(RestExp, expBase, nextExp, Constants.RestExpMaxAcquireRate);
        if (rest == RestExp) {
            return;
        }
        RestExp = rest;
        session.Send(ExperienceUpPacket.SetRestExp(RestExp));
    }

    public long AddExp(long expGained, ExpMessageCode message = ExpMessageCode.s_msg_take_exp) => AddExp(expGained, message, kill: false);

    // A kill's ExpUp carries the npc's object id, as KMS2 sends it.
    private long AddExp(long expGained, ExpMessageCode message, bool kill, int npcObjectId = 0) {
        if (expGained <= 0) {
            return 0;
        }
        expGained = SpendRest(expGained, kill);
        LevelUp();
        AddPrestigeExp(message.Type());
        session.Send(ExperienceUpPacket.Add(expGained, Exp, RestExp, message, npcObjectId));
        session.ConditionUpdate(ConditionType.exp, counter: expGained);
        return expGained;
    }

    public long AddExp(ExpType expType, float modifier = 1f, long additionalExp = 0) {
        if (session.Field is null) return 0;
        if (!session.TableMetadata.CommonExpTable.Entries.TryGetValue(expType, out CommonExpTable.Entry? entry)
            || !session.TableMetadata.ExpTable.ExpBase.TryGetValue(entry.ExpTableId, out IReadOnlyDictionary<int, long>? expBase)) {
            return 0;
        }

        int level;
        switch (CommonExpRules.Level(expType)) {
            case CommonExpRules.LevelSource.Character:
                level = Level;
                break;
            case CommonExpRules.LevelSource.Field:
                level = CommonExpRules.FieldLevel(session.Field.Metadata.Drop.Level, Level);
                break;
            default:
                Log.Logger.Warning("Unhandled ExpType: {ExpType}", expType);
                return 0;
        }
        if (!expBase.TryGetValue(level, out long expValue)) {
            return 0;
        }

        return AddExp(CommonExpRules.Amount(expValue, modifier, entry.Factor, additionalExp), expType.Message());
    }

    // The client's CExpTable::GetTypeValue at the character's level: the type's common exp curve, its base exp times its
    // factor rounded and at least 1; 0 for a type without a curve or a level past the table.
    public long TypeValue(ExpType expType) {
        if (expType == ExpType.none
            || !session.TableMetadata.CommonExpTable.Entries.TryGetValue(expType, out CommonExpTable.Entry? entry)
            || !session.TableMetadata.ExpTable.ExpBase.TryGetValue(entry.ExpTableId, out IReadOnlyDictionary<int, long>? expBase)
            || !expBase.TryGetValue(Level, out long expValue)) {
            return 0;
        }
        return QuestExpRules.CurveValue(expValue, entry.Factor);
    }

    public void AddStaticExp(long amount) {
        if (amount <= 0) {
            return;
        }
        Exp += amount;
        session.Send(ExperienceUpPacket.Add(amount, Exp, RestExp, ExpMessageCode.s_msg_take_exp));
        session.ConditionUpdate(ConditionType.exp, counter: amount);
        LevelUp();
    }

    // The exp of a kill: the monster exp table's column for the npc's class (elite, boss, dungeon elite or boss, else
    // common) at its level; a level the table lacks falls back to the exp base table 2, its common column.
    public void AddMobExp(int moblevel, int npcClass = 1, float modifier = 1f, long additionalExp = 0, int npcObjectId = 0) {
        long expValue = MobExp(moblevel, npcClass);
        if (expValue <= 0) {
            return;
        }

        AddExp((long) (expValue * modifier) + additionalExp, ExpType.monster.Message(), kill: true, npcObjectId);
    }

    // The monster exp table's value for a kill, 0 when neither table has the level.
    public long MobExp(int moblevel, int npcClass) {
        if (session.ServerTableMetadata.MonsterExpTable.Entries.TryGetValue(moblevel, out MonsterExpMetadata? row)) {
            return row.ForClass(npcClass);
        }
        if (session.TableMetadata.ExpTable.ExpBase.TryGetValue(2, out IReadOnlyDictionary<int, long>? expBase)
            && expBase.TryGetValue(moblevel, out long expValue)) {
            return expValue;
        }
        return 0;
    }

    // The field boss bonus (BossKillRules), sent after the kill exp with the boss's object id.
    public long AddFieldBossBonusExp(long killExp, int npcObjectId) {
        return AddExp(BossKillRules.FieldBossBonus(killExp), ExpMessageCode.s_msg_take_assist_bonus_exp, kill: false, npcObjectId);
    }

    public bool LevelUp() {
        int startLevel = Level;
        for (int level = startLevel; level < Constants.characterMaxLevel; level++) {
            if (!session.TableMetadata.ExpTable.NextExp.TryGetValue(level, out long expToNextLevel) || expToNextLevel > Exp) {
                break;
            }

            Exp -= expToNextLevel;
            Level++;
        }
        if (Level > startLevel) {
            session.Player.Flag |= PlayerObjectFlag.Level;
            session.Dungeon.UpdateDungeonEnterLimit();
            session.Field?.Broadcast(LevelUpPacket.LevelUp(session.Player));
            session.ConditionUpdate(ConditionType.level_up, codeLong: (int) session.Player.Value.Character.Job.Code(), targetLong: Level);
            session.ConditionUpdate(ConditionType.level, targetLong: Level);
            session.Config.UpdateDeathPenalty(0);
            session.Stats.Refresh(levelUp: true);

            session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                Level = Level,
                Async = true,
            });
        }
        return startLevel != Level;
    }

    private void AddPrestigeExp(ExpType expType) {
        if (Level < Constants.AdventureLevelStartLevel) {
            return;
        }

        if (!session.ServerTableMetadata.PrestigeExpTable.Entries.TryGetValue(expType, out long amount)) {
            return;
        }

        if (PrestigeCurrentExp - PrestigeExp + (PrestigeLevelsGained * Constants.AdventureLevelLvUpExp) >= Constants.AdventureLevelLvUpExp) {
            amount = (long) (amount * Constants.AdventureLevelFactor);
        }

        PrestigeCurrentExp = Math.Min(amount + PrestigeCurrentExp, long.MaxValue);

        // PrestigeLevelUp adds the levels; adding them here as well counted every level twice.
        int gained = PrestigeRules.LevelsGained(PrestigeCurrentExp, Constants.AdventureLevelLvUpExp, PrestigeLevel, Constants.AdventureLevelLimit);
        PrestigeCurrentExp -= gained * Constants.AdventureLevelLvUpExp;
        session.Send(PrestigePacket.AddExp(PrestigeCurrentExp, amount));
        if (gained > 0) {
            PrestigeLevelUp(gained);
        }
    }

    public void PrestigeLevelUp(int amount = 1) {
        PrestigeLevel = Math.Clamp(PrestigeLevel + amount, amount, Constants.AdventureLevelLimit);
        PrestigeLevelsGained += amount;
        session.ConditionUpdate(ConditionType.adventure_level, counter: amount);
        session.ConditionUpdate(ConditionType.adventure_level_up, counter: amount);
        foreach (PrestigeMission mission in PrestigeMissions) {
            mission.GainedLevels += amount;
        }

        for (int i = 0; i < amount; i++) {
            Item? item = session.Field?.ItemDrop.CreateItem(Constants.AdventureLevelLvUpRewardItem);
            if (item == null) {
                break;
            }

            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }

        session.Send(PrestigePacket.LevelUp(session.Player.ObjectId, PrestigeLevel));
        session.Send(PrestigePacket.LoadMissions(session.Player.Value.Account));
        if (session.Field != null) {
            session.Player.Buffs.RefreshPrestigeBuffs();
        }
        CheckPrestigeReward();
    }

    private void CheckPrestigeReward() {
        if (!session.TableMetadata.PrestigeLevelRewardTable.Entries.TryGetValue(PrestigeLevel, out PrestigeLevelRewardMetadata? metadata)) {
            return;
        }

        if (metadata.Type != PrestigeAwardType.statPoint) {
            return;
        }

        session.Config.AddStatPoint(AttributePointSource.Prestige, metadata.Value);
    }
}
