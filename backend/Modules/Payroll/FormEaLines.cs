using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Which line of Form EA (C.P.8A) each part of a payslip belongs on.
//
// The form reports income by KIND, not as one gross figure: salary and
// overtime (B1a) apart from fees, commission and bonus (B1b), apart from
// allowances (B1c), benefits in kind (B3) and so on — and every one of those
// "excluding tax exempt allowances / perquisites / gifts / benefits", which
// are totalled once, in F. So each allowance line is split by what was
// actually TAXABLE after its annual exemption ceiling (PcbTaxableAmount): the
// taxable part goes to the category's own line, the exempt part to F.
//
// Pure: a payslip and its line items in, figures out. The annual service adds
// the months up.
public static class FormEaLines
{
    public enum Line { B1a, B1b, B1c, B1d, B1e, B1f, B3, B4, B6, NotOnForm }

    // A category not listed here falls back by its nature: a benefit in kind
    // to B3, anything paid in cash to B1(c) "other allowances".
    private static readonly IReadOnlyDictionary<string, Line> ByCategory = new Dictionary<string, Line>(StringComparer.Ordinal)
    {
        // Salary, wages, leave pay and overtime.
        [PayrollAdjustmentCategories.WagesOvertime] = Line.B1a,
        [PayrollAdjustmentCategories.WagesLeavePay] = Line.B1a,
        [PayrollAdjustmentCategories.WagesArrears] = Line.B1a,

        // Fees (including director fees), commission or bonus.
        [PayrollAdjustmentCategories.WagesDirectorFee] = Line.B1b,
        [PayrollAdjustmentCategories.WagesCommission] = Line.B1b,
        [PayrollAdjustmentCategories.WagesBonusAnnual] = Line.B1b,
        [PayrollAdjustmentCategories.WagesBonusNonAnnual] = Line.B1b,
        [PayrollAdjustmentCategories.WagesIncentive] = Line.B1b,
        [PayrollAdjustmentCategories.WagesExGratia] = Line.B1b,

        // Tips, perquisites, awards/rewards or other allowances — every
        // allowance_* category falls here by default; these two are wages
        // categories that belong with them.
        [PayrollAdjustmentCategories.WagesServiceCharge] = Line.B1c,
        [PayrollAdjustmentCategories.BikAward] = Line.B1c,

        [PayrollAdjustmentCategories.WagesTaxBorneByEmployer] = Line.B1d,
        [PayrollAdjustmentCategories.BikShareScheme] = Line.B1e,
        [PayrollAdjustmentCategories.WagesGratuity] = Line.B1f,
        [PayrollAdjustmentCategories.BikLivingAccommodation] = Line.B4,
        [PayrollAdjustmentCategories.WagesCompensationLossEmployment] = Line.B6,

        // Reimbursement of money the employee spent for the employer: not
        // remuneration at all, so on no line of the form.
        [PayrollAdjustmentCategories.WagesExpenseClaim] = Line.NotOnForm,

        // Exempt, and NOT among the nine kinds LHDN's EA guide lists for F
        // ("Nota Bahagian F", note 2): medical benefits and other exemptions
        // are left off the form entirely rather than reported as exempt.
        [PayrollAdjustmentCategories.BikMedical] = Line.NotOnForm,
        [PayrollAdjustmentCategories.BikOtherExempt] = Line.NotOnForm,
    };

    public static Line LineFor(string? category, bool nonCash)
    {
        if (category is not null && ByCategory.TryGetValue(category, out var line)) return line;
        return nonCash ? Line.B3 : Line.B1c;
    }

    // One payslip's contribution to the year's Form EA.
    public static FormEaFigures For(Payslip payslip, IReadOnlyList<PayslipLineItem> lineItems)
    {
        var f = new FormEaFigures();

        // Every CASH allowance line is inside GrossPay. What is left of gross
        // once they (and claim reimbursements) are taken out is basic pay and
        // computed overtime, net of unpaid leave — the core of B1(a).
        var cashLines = 0m;
        var zakatNotViaSalary = 0m;

        foreach (var item in lineItems)
        {
            var meta = PayrollAdjustmentCategories.Find(item.Category);

            if (item.Kind == PayslipLineKind.DEDUCTION)
            {
                if (meta is null) continue;
                // TP1 reliefs, at what was GRANTED once the limits applied.
                if (meta.FeedsLp1Relief) f.D5aTp1Relief += item.PcbTaxableAmount ?? item.Amount;
                // Zakat paid by the employee directly, declared on TP1.
                if (item.Category == PayrollAdjustmentCategories.DeductZakatTp1) f.D5bZakatSelfPaid += item.Amount;
                // Payslip.Zakat also holds self-paid zakat and the departure
                // levy rebate (all three offset PCB); D3 is salary zakat only.
                if (meta.OffsetsPcb && meta.CashNeutral) zakatNotViaSalary += item.Amount;
                continue;
            }

            if (item.Kind != PayslipLineKind.ALLOWANCE) continue;

            var nonCash = meta?.NonCash ?? false;
            if (!nonCash) cashLines += item.Amount;

            var line = LineFor(item.Category, nonCash);
            if (line == Line.NotOnForm) continue;

            // The exempt part is what the category's ceiling (or its not being
            // taxable at all) kept out of PCB.
            var exempt = item.SubjectToPcb ? item.Amount - (item.PcbTaxableAmount ?? item.Amount) : item.Amount;
            exempt = Math.Clamp(exempt, 0m, item.Amount);
            var taxable = item.Amount - exempt;

            f.F += exempt;
            f.Add(line, taxable, item.Label);
        }

        f.B1a += payslip.GrossPay - payslip.TotalReimbursements - cashLines;
        f.D3ZakatViaSalary += payslip.Zakat - zakatNotViaSalary;
        return f;
    }
}

// The year's figures for one employee's Form EA, by line. Money not listed
// (B2 preceding-year arrears, B5 unapproved-fund refunds, C pensions, D4
// donations via salary) is nothing payroll records, and prints as blank.
public sealed class FormEaFigures
{
    public decimal B1a { get; set; }   // gross salary, wages or leave pay (incl. overtime)
    public decimal B1b { get; set; }   // fees (incl. director fees), commission or bonus
    public decimal B1c { get; set; }   // gross tips, perquisites, awards/rewards or other allowances
    public decimal B1d { get; set; }   // income tax borne by the employer
    public decimal B1e { get; set; }   // ESOS benefit
    public decimal B1f { get; set; }   // gratuity
    public decimal B3 { get; set; }    // benefits in kind
    public decimal B4 { get; set; }    // value of living accommodation
    public decimal B6 { get; set; }    // compensation for loss of employment

    public decimal D3ZakatViaSalary { get; set; }
    public decimal D5aTp1Relief { get; set; }
    public decimal D5bZakatSelfPaid { get; set; }

    public decimal F { get; set; }     // tax-exempt allowances / perquisites / gifts / benefits

    // "Details of payment" (B1c) and "Specify" (B3): what the figure is made of.
    public SortedSet<string> B1cDetails { get; } = new(StringComparer.OrdinalIgnoreCase);
    public SortedSet<string> B3Details { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Sections B and C together — the form's TOTAL line.
    public decimal Total => B1a + B1b + B1c + B1d + B1e + B1f + B3 + B4 + B6;

    // CP8D field 10, "Jumlah saraan kasar": the same remuneration LESS what
    // CP8D reports in columns of its own — benefits in kind (11), living
    // accommodation (12) and ESOS (13) — so nothing is declared twice.
    public decimal Cp8dGross => B1a + B1b + B1c + B1d + B1f + B6;

    public void Add(FormEaLines.Line line, decimal amount, string label)
    {
        switch (line)
        {
            case FormEaLines.Line.B1a: B1a += amount; break;
            case FormEaLines.Line.B1b: B1b += amount; break;
            case FormEaLines.Line.B1c: B1c += amount; if (amount > 0) B1cDetails.Add(label); break;
            case FormEaLines.Line.B1d: B1d += amount; break;
            case FormEaLines.Line.B1e: B1e += amount; break;
            case FormEaLines.Line.B1f: B1f += amount; break;
            case FormEaLines.Line.B3: B3 += amount; if (amount > 0) B3Details.Add(label); break;
            case FormEaLines.Line.B4: B4 += amount; break;
            case FormEaLines.Line.B6: B6 += amount; break;
        }
    }

    public void Add(FormEaFigures other)
    {
        B1a += other.B1a; B1b += other.B1b; B1c += other.B1c; B1d += other.B1d;
        B1e += other.B1e; B1f += other.B1f; B3 += other.B3; B4 += other.B4; B6 += other.B6;
        D3ZakatViaSalary += other.D3ZakatViaSalary;
        D5aTp1Relief += other.D5aTp1Relief;
        D5bZakatSelfPaid += other.D5bZakatSelfPaid;
        F += other.F;
        B1cDetails.UnionWith(other.B1cDetails);
        B3Details.UnionWith(other.B3Details);
    }

    public FormEaFigures Rounded()
    {
        var r = new FormEaFigures
        {
            B1a = Money.Round2(B1a), B1b = Money.Round2(B1b), B1c = Money.Round2(B1c),
            B1d = Money.Round2(B1d), B1e = Money.Round2(B1e), B1f = Money.Round2(B1f),
            B3 = Money.Round2(B3), B4 = Money.Round2(B4), B6 = Money.Round2(B6),
            D3ZakatViaSalary = Money.Round2(D3ZakatViaSalary),
            D5aTp1Relief = Money.Round2(D5aTp1Relief),
            D5bZakatSelfPaid = Money.Round2(D5bZakatSelfPaid),
            F = Money.Round2(F),
        };
        r.B1cDetails.UnionWith(B1cDetails);
        r.B3Details.UnionWith(B3Details);
        return r;
    }
}
