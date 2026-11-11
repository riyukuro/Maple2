namespace Maple2.Server.Game.Util;

/// <summary>
/// The AI node TriggerModifyUserValue (189 nodes in 63 AI files) adds its value to a trigger's user value. The files'
/// comments say so: "PhaseSumTotal = PhaseSumTotal + 1", add 1 to MonsterMany when a monster enters battle and take 1
/// off when it dies (value 1 x120, -1 x63), and BridgeAppear +1 from each boss until it is 3. A key the field does not
/// have yet starts from 0.
/// </summary>
public static class AiTriggerValue {
    public static int Modify(Dictionary<string, int> userValues, string key, int value) {
        int result = userValues.GetValueOrDefault(key) + value;
        userValues[key] = result;
        return result;
    }
}
