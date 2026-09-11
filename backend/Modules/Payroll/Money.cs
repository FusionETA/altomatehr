namespace AltomateHR.Api.Modules.Payroll;

// Rounding rules for payroll money.
//
// Everything here is `decimal`, never `double` — cent-level drift on a
// contribution is a filing discrepancy, not a display nit.
public static class Money
{
    // Round to sen. Half rounds away from zero (RM 0.125 → RM 0.13), which is
    // the ordinary commercial convention — NOT .NET's default banker's rounding.
    public static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // Truncate toward zero to sen, dropping the remainder rather than rounding.
    // LHDN's convention wherever the spec says figures beyond two decimals are
    // "omitted" — K2 (3,549 ÷ 11 = 322.6363… → 322.63), and the current-month
    // PCB before the 5-sen step.
    //
    // The reference TS needs a `toFixed` dance here because `32.55 * 100` is
    // 3254.9999999999995 in IEEE 754 and truncates to 32.54. Decimal is exact,
    // so the straightforward form is correct.
    public static decimal Trunc2(decimal value) =>
        Math.Truncate(value * 100m) / 100m;

    // A percentage of an amount, rounded UP to the next whole ringgit. KWSP's
    // off-table rule (Third Schedule Note 2) applies this once per side —
    // employee and employer — not once per component.
    public static decimal CeilRinggit(decimal amount, decimal ratePercent) =>
        amount <= 0m ? 0m : Math.Ceiling(amount * ratePercent / 100m);
}
