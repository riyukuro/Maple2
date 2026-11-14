namespace Maple2.Model.Validators;

// reduce_recovery_ep_inv (129, specialabilitystringtable value type 2, "EP recovery interval {0:0.1f}% down") is a rate
// in thousandths taken off StaminaRegenInterval: 100 (10 %) makes the 500 ms interval 450 ms. The client has no reader
// for it (the server sends the regen ticks). KR rolls it as a merge option (itemmergeoptionbase reduce_recovery_ep_inv,
// 19 to 220). The cut stops at 90 %, so stacked lines can never make the interval 0.
public static class StaminaRecoveryRules {
    public const double MaxCut = 0.9;

    public static float IntervalCut(long rate) => (float) Math.Clamp(rate / 1000.0, 0, MaxCut);
}
