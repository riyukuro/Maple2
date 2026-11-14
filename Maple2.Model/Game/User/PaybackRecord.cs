namespace Maple2.Model.Game;

// The account's payback month (PaybackRules): the month as yyyyMM, the merets spent on the payback products in it,
// and the adventure level the account had when the month was first seen; the levels gained since are its payback
// levels. Settled (paid by mail) at the first look in a later month.
public record PaybackRecord(int Month, long Spent, int StartLevel);
