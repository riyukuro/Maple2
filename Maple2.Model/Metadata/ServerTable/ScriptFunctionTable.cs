using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

public record ScriptFunctionTable(IReadOnlyDictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> Entries) : ServerTable;

public record ScriptFunctionMetadata(
    int Id, // QuestId or NpcId
    int ScriptId,
    ScriptType Type,
    int FunctionId,
    bool EndFunction,
    int PortalId,
    string UiName,
    string UiArg,
    string UiArg2,
    int MoveMapId,
    int MovePortalId,
    string MoveMapMovie,
    string Emoticon,
    IList<ItemComponent> PresentItems,
    IList<ItemComponent> CollectItems,
    int SetTriggerValueTriggerId,
    string SetTriggerValueKey,
    string SetTriggerValue,
    string Divorce,
    long PresentExp,
    long CollectMeso,
    int MaidMoodIncrease,
    int MaidClosenessIncrease,
    bool MaidPay,
    // skillID / skillLevel: an additional effect put on the talker (KR: 70000030, which cancels 70000029).
    int EffectId = 0,
    short EffectLevel = 0,
    // presentGlobalDropBoxID rolled at presentGlobalDropLevel, presentIndivisualDropBoxID: items for the talker.
    int GlobalDropBoxId = 0,
    int GlobalDropLevel = 0,
    int IndividualDropBoxId = 0);
