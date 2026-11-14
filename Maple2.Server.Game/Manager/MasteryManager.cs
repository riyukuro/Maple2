using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class MasteryManager {

    private readonly GameSession session;
    private Mastery Mastery => session.Player.Value.Character.Mastery;
    private IDictionary<int, int> gatheringCounts => session.Config.GatheringCounts;

    public MasteryManager(GameSession session) {
        this.session = session;
    }

    public int this[MasteryType type] {
        get => type switch {
            MasteryType.Fishing => Mastery.Fishing,
            MasteryType.Music => Mastery.Instrument,
            MasteryType.Mining => Mastery.Mining,
            MasteryType.Gathering => Mastery.Foraging,
            MasteryType.Breeding => Mastery.Ranching,
            MasteryType.Farming => Mastery.Farming,
            MasteryType.Blacksmithing => Mastery.Smithing,
            MasteryType.Engraving => Mastery.Handicrafts,
            MasteryType.Alchemist => Mastery.Alchemy,
            MasteryType.Cooking => Mastery.Cooking,
            MasteryType.PetTaming => Mastery.PetTaming,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid mastery type."),
        };
        set {
            short startLevel = GetLevel(type);
            int startValue = this[type];
            switch (type) {
                case MasteryType.Fishing:
                    Mastery.Fishing = Math.Clamp(value, Mastery.Fishing, Constant.FishingMasteryMax);
                    break;
                case MasteryType.Music:
                    Mastery.Instrument = Math.Clamp(value, Mastery.Instrument, Constant.PerformanceMasteryMax);
                    break;
                case MasteryType.Mining:
                    Mastery.Mining = Math.Clamp(value, Mastery.Mining, Constant.MiningMasteryMax);
                    break;
                case MasteryType.Gathering:
                    Mastery.Foraging = Math.Clamp(value, Mastery.Foraging, Constant.ForagingMasteryMax);
                    break;
                case MasteryType.Breeding:
                    Mastery.Ranching = Math.Clamp(value, Mastery.Ranching, Constant.RanchingMasteryMax);
                    break;
                case MasteryType.Farming:
                    Mastery.Farming = Math.Clamp(value, Mastery.Farming, Constant.FarmingMasteryMax);
                    break;
                case MasteryType.Blacksmithing:
                    Mastery.Smithing = Math.Clamp(value, Mastery.Smithing, Constant.SmithingMasteryMax);
                    break;
                case MasteryType.Engraving:
                    Mastery.Handicrafts = Math.Clamp(value, Mastery.Handicrafts, Constant.HandicraftsMasteryMax);
                    break;
                case MasteryType.Alchemist:
                    Mastery.Alchemy = Math.Clamp(value, Mastery.Alchemy, Constant.AlchemyMasteryMax);
                    break;
                case MasteryType.Cooking:
                    Mastery.Cooking = Math.Clamp(value, Mastery.Cooking, Constant.CookingMasteryMax);
                    break;
                case MasteryType.PetTaming:
                    Mastery.PetTaming = Math.Clamp(value, Mastery.PetTaming, Constant.PetTamingMasteryMax);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid mastery type.");
            }

            session.Send(MasteryPacket.UpdateMastery(type, session.Mastery[type]));
            int currentLevel = GetLevel(type);
            // The first mastery gained earns grade 1; a set that leaves the mastery at 0 earns nothing.
            int deltaLevel = currentLevel - startLevel + (startValue == 0 && this[type] > 0 ? 1 : 0);
            int deltaExp = value - startValue;
            Log.Logger.Debug("[Mastery] {type} changed from {startValue} to {value} (Level {startLevel} -> {currentLevel}), ΔLevel: {deltaLevel}, ΔExp: {deltaExp}", type, startValue, value, startLevel, currentLevel, deltaLevel, deltaExp);

            HandleMasteryLevelChange(type, currentLevel, deltaLevel);
            HandleMasteryExpIncrease(type, deltaExp);
        }

    }

    /// <summary>
    /// Handles the change of mastery level for a specified <see cref="MasteryType"/>.
    /// Updates the corresponding condition based on the mastery type and level changes.
    /// </summary>
    /// <param name="type">The type of mastery whos level has been increased.</param>
    /// <param name="currentLevel">The new current level of the mastery after the increase.</param>
    /// <param name="deltaLevel">The delta by which the mastery level has changed.</param>
    private void HandleMasteryLevelChange(MasteryType type, int currentLevel, int deltaLevel) {
        // The mastery never goes down (the setter clamps it to the old value), so only a rise is counted.
        if (deltaLevel <= 0) {
            return;
        }

        switch (type) {
            case MasteryType.Fishing:
                session.ConditionUpdate(ConditionType.fisher_grade, codeLong: currentLevel);
                return;
            case MasteryType.Music:
                session.ConditionUpdate(ConditionType.music_play_grade, counter: deltaLevel);
                return;
            default:
                // mastery_grade counts each grade gained (23100238-23100241 "Master Miner (1)": grades 1 to 13, a
                // rise of two grades counts two); set_mastery_grade is the grade reached (23100330 "Master Miner (2)":
                // grades 14 and 15 of mining), kept as the highest.
                session.ConditionUpdate(ConditionType.mastery_grade, counter: deltaLevel, codeLong: (int) type);
                session.ConditionUpdate(ConditionType.set_mastery_grade, counter: currentLevel, codeLong: (int) type);
                return;
        }
    }

    /// <summary>
    /// Handles the increase of mastery experience for a specified MasteryType.
    /// Updates relevant conditions based on the mastery type and the amount of experience gained.
    /// </summary>
    /// <param name="type">The type of mastery for which experience is being increased.</param>
    /// <param name="deltaExp">The amount of experience that has been gained for the mastery.</param>
    private void HandleMasteryExpIncrease(MasteryType type, int deltaExp) {
        switch (type) {
            case MasteryType.Music:
                session.ConditionUpdate(ConditionType.music_play_instrument_mastery, counter: deltaExp, codeLong: session.Instrument?.Value.Category ?? 0);
                return;
            default:
                return;
        }
    }

    public short GetLevel(MasteryType type) {
        if (!session.TableMetadata.MasteryRewardTable.Entries.TryGetValue(type, out IReadOnlyDictionary<int, MasteryRewardTable.Entry>? masteryRewardEntries)) {
            return 1;
        }

        return (short) Math.Max(1, masteryRewardEntries.OrderByDescending(mastery => mastery.Key).FirstOrDefault(mastery => session.Mastery[type] >= mastery.Value.Value).Key);
    }

    // KMS2 order for a gathered object: the mastery, the drops, InteractObject 5 and 13, then the experience.
    public void Gather(FieldInteract fieldInteract) {
        bool success = GatherCommon(fieldInteract.Value.Item.RecipeId, fieldInteract.Position, fieldInteract.Rotation, out MasteryRecipeTable.Entry? recipeMetadata);
        session.Send(success
            ? InteractObjectPacket.Interact(fieldInteract, decreaseAmount: 1)
            : InteractObjectPacket.Interact(fieldInteract, GatherResult.Fail, decreaseAmount: 0));
        session.Send(InteractObjectPacket.Result(InteractResult.none, fieldInteract));
        GatherExp(success, recipeMetadata);
    }

    // KMS2 order for a farming or breeding cube: the mastery, the drops, the cube's update, the success or
    // failure, then the experience.
    public void Gather(FieldFunctionInteract fieldFunctionInteract) {
        bool success = GatherCommon(fieldFunctionInteract.Value.RecipeId, fieldFunctionInteract.Position, fieldFunctionInteract.Rotation, out MasteryRecipeTable.Entry? recipeMetadata);
        session.Send(FunctionCubePacket.UpdateFunctionCube(fieldFunctionInteract.InteractCube));
        session.Send(success
            ? FunctionCubePacket.SuccessLifeSkill(session.CharacterId, fieldFunctionInteract.InteractCube)
            : FunctionCubePacket.FailLifeSkill(session.CharacterId, fieldFunctionInteract.InteractCube));
        GatherExp(success, recipeMetadata);
    }

    private void GatherExp(bool success, MasteryRecipeTable.Entry? recipeMetadata) {
        if (success && recipeMetadata is { NoRewardExp: false }) {
            session.Exp.AddExp(ExpType.gathering, message: MasteryRules.ExpReason(recipeMetadata.Type));
        }
    }

    private bool GatherCommon(int recipeId, Vector3 position, Vector3 rotation, out MasteryRecipeTable.Entry? recipeMetadata) {
        if (!session.TableMetadata.MasteryRecipeTable.Entries.TryGetValue(recipeId, out recipeMetadata)) {
            session.Send(MasteryPacket.Error(MasteryError.s_mastery_error_unknown));
            return false;
        }

        MasteryError error = MasteryRules.GatherError(this[recipeMetadata.Type], recipeMetadata.RequiredMastery,
            recipeMetadata.RequiredQuests.Count, HasRequiredQuest(recipeMetadata));
        if (error != 0) {
            session.Send(MasteryPacket.Error(error));
            return false;
        }
        float successRate = GatherSuccessRate(recipeMetadata);

        BeforeGather(recipeMetadata);
        if (Random.Shared.NextDouble() > (successRate / 100)) {
            return false;
        }

        int reward = GatherReward(recipeMetadata);
        if (reward > 0) {
            this[recipeMetadata.Type] += reward;
        }
        Gather(recipeMetadata, position, rotation);
        return true;
    }

    // One of the recipe's required quests is in progress.
    public bool HasRequiredQuest(MasteryRecipeTable.Entry recipe) {
        return recipe.RequiredQuests.Any(questId => session.Quest.TryGetQuest(questId, out Quest? quest) && MasteryRules.CountsAsRequiredQuest(quest.State));
    }

    // The mastery one gather gives at the current mastery (the client's CMasteryTable::Factor).
    public int GatherReward(MasteryRecipeTable.Entry recipeMetadata) {
        IEnumerable<(int Grade, int Value)> rows = session.TableMetadata.MasteryRewardTable.Entries.TryGetValue(recipeMetadata.Type, out IReadOnlyDictionary<int, MasteryRewardTable.Entry>? grades)
            ? grades.Select(grade => (grade.Key, grade.Value.Value))
            : [];
        return MasteryRules.Reward(recipeMetadata.Type, recipeMetadata.RewardMastery, recipeMetadata.RequiredMastery, this[recipeMetadata.Type], rows,
            difference => session.TableMetadata.MasteryDifferentialFactorTable.Entries.TryGetValue(difference, out MasteryDifferentialFactorTable.Entry? entry) ? entry.Factor : null);
    }

    public void Gather(MasteryRecipeTable.Entry recipeMetadata, Vector3 position, Vector3 rotation) {
        // The count the client's recipe list shows: the recipe's count raised by the type's ability.
        SpecialAttribute? bonus = MasteryRules.GatherBonus(recipeMetadata.Type);
        int bonusTotal = bonus == null ? 0 : (int) session.Player.Stats.Values[bonus.Value].Total;
        foreach (ItemComponent itemReward in recipeMetadata.RewardItems) {
            int amount = MasteryRules.GatherItemCount(itemReward.Amount, bonusTotal);
            if (amount < 1) {
                continue;
            }
            Item? item = session.Field?.ItemDrop.CreateItem(itemReward.ItemId, itemReward.Rarity, amount);
            if (item == null || session.Field is null) {
                continue;
            }
            session.Field.DropItem(position, rotation, item, characterId: session.CharacterId,
                dropKind: (short) (ItemDropKind.Mastery + (int) recipeMetadata.Type));
        }

        AfterGather(recipeMetadata, 1);
    }

    public float GatherSuccessRate(MasteryRecipeTable.Entry recipeMetadata) {
        if (!gatheringCounts.TryGetValue(recipeMetadata.Id, out int currentCount)) {
            gatheringCounts[recipeMetadata.Id] = 0;
        }

        int myHome = 1; // For outside gathering, it should be always 1
        if (session.Field is HomeFieldManager homeField) {
            myHome = homeField.OwnerId == session.AccountId ? 1 : 0;
        }
        return Lua.CalcGatheringObjectSuccessRate(currentCount, recipeMetadata.HighRateLimitCount, recipeMetadata.NormalRateLimitCount, myHome);
    }

    private void AfterGather(MasteryRecipeTable.Entry recipeMetadata, int gatheringAmount) {
        switch (recipeMetadata.Type) {
            case MasteryType.Breeding:
            case MasteryType.Farming:
                if (session.Field is HomeFieldManager homeField && homeField.OwnerId != session.AccountId) {
                    session.ConditionUpdate(ConditionType.mastery_harvest_otherhouse, counter: gatheringAmount, codeLong: recipeMetadata.Id);
                }
                if (recipeMetadata.Type == MasteryType.Farming) {
                    session.ConditionUpdate(ConditionType.mastery_farming, counter: gatheringAmount, codeLong: recipeMetadata.Id);
                }
                session.ConditionUpdate(ConditionType.mastery_harvest, counter: gatheringAmount, codeLong: recipeMetadata.Id);
                break;
            case MasteryType.Gathering:
            case MasteryType.Mining:
                session.ConditionUpdate(ConditionType.mastery_gathering, counter: gatheringAmount, codeLong: recipeMetadata.Id);
                break;
        }

        gatheringCounts[recipeMetadata.Id] += 1;
    }

    public void BeforeGather(MasteryRecipeTable.Entry recipeMetadata) {
        switch (recipeMetadata.Type) {
            case MasteryType.Farming:
            case MasteryType.Breeding:
                session.ConditionUpdate(ConditionType.mastery_harvest_try, codeLong: recipeMetadata.Id);
                session.ConditionUpdate(ConditionType.mastery_farming_try, codeLong: recipeMetadata.Id);
                break;
            case MasteryType.Gathering:
            case MasteryType.Mining:
                session.ConditionUpdate(ConditionType.mastery_gathering_try, codeLong: recipeMetadata.Id);
                break;
        }
    }
}
