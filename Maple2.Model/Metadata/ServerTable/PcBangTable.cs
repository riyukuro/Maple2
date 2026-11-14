namespace Maple2.Model.Metadata;

// table/Server/{locale}/PcBangBonus.xml and PcBangGiftItem.xml (feature PCBang): the buffs a PC bang grade gives
// (<buffs level><buff additionalEffectID/></buffs>), the play bonus items an account claims once a day after playing
// requireMins minutes at a PC bang (<playBonus><item requireMins itemID count grade systemMailID/></playBonus>), and the
// gift items the PC bang dialog offers (<item giftID itemID count grade/>). The client shows the play bonus rows in
// file order (uipcbangdialog.cpp, at most 6) and hides a gift whose item it lacks.
public record PcBangTable(
    IReadOnlyDictionary<int, IReadOnlyList<int>> Buffs,
    IReadOnlyList<PcBangTable.PlayBonus> PlayBonuses,
    IReadOnlyDictionary<int, PcBangTable.Gift> Gifts) : ServerTable {
    public record PlayBonus(int RequireMinutes, int ItemId, int Rarity, int Count, int SystemMailId);

    public record Gift(int Id, int ItemId, int Rarity, int Count);
}
