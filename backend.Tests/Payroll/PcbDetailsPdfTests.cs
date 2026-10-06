using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System.Text.Json;

namespace AltomateHR.Api.Tests.Payroll;

// The LHDN MTD §E worksheet.
//
// The thing that actually matters about this document is that a reader can
// re-derive the deduction from the page. So what is pinned is that every
// figure it prints comes from the SNAPSHOT, that a missing snapshot degrades
// to a sentence rather than a page of zeroes, and that the AR sections only
// appear when there was additional remuneration.
public class PcbDetailsPdfTests
{
    private static PcbBreakdown Resident(PcbArBreakdown? ar = null) => new()
    {
        Formula = PcbFormula.Resident,
        Y = 11000m, K = 1210m,
        Y1 = 5500m, K1 = 605m,
        Y2 = 49500m, K2 = 242.77m, N = 9,
        D = 9000m, S = 0m, Du = 0m, Su = 0m,
        Q = 2000m, C = 3m, QC = 6000m,
        SumLp = 0m, Lp1 = 0m,
        P = 47000m, M = 35000m, R = 0.06m, B = 600m,
        Z = 0m, X = 220m,
        YearlyTax = 1320m,
        CurrentMonthPcb = 110m,
        Ar = ar,
        PcbNormal = 110m,
        PcbAdditional = ar?.PcbC ?? 0m,
        PcbTotal = 110m + (ar?.PcbC ?? 0m),
    };

    private static PcbArBreakdown Ar() => new()
    {
        Yt = 8250m,
        Kt = 908m,
        KtEffective = 0m,
        ChargeableWithAr = 55250m,
        M2 = 50000m, R2 = 0.11m, B2 = 1500m,
        Cs = 2077.50m,
        PcbB = 1320m,
        PcbCBeforeRounding = 757.50m,
        PcbC = 757.50m,
    };

    private static PcbBreakdown NonResident() => new()
    {
        Formula = PcbFormula.NonResident,
        Rate = 0.30m,
        NormalTaxable = 7000m,
        AdditionalTaxable = 3000m,
        PcbNormal = 2100m,
        PcbAdditional = 900m,
        PcbTotal = 3000m,
    };

    private static PcbDetailsModel Model(params PcbDetailsEmployee[] employees) => new()
    {
        OrganizationName = "Globe Engineering Sdn Bhd",
        PeriodLabel = "March 2026",
        Employees = employees.Length == 0
            ? [Employee()]
            : employees,
    };

    private static PcbDetailsEmployee Employee(
        PcbBreakdown? breakdown = null,
        string name = "Aisyah Binti Rahman") => new()
        {
            Name = name,
            Position = "Site Engineer",
            EmployeeCode = "E-001",
            Breakdown = breakdown ?? Resident(),
        };

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == 0x25 && bytes[1] == 0x50
        && bytes[2] == 0x44 && bytes[3] == 0x46;   // "%PDF"

    // ─── It renders at all ──────────────────────────────────────────────

    [Fact]
    public void RendersAPdf()
    {
        Assert.True(IsPdf(PcbCalculationDetailsPdf.Render(Model())));
    }

    // Each employee starts a new page, and nobody's worksheet bleeds into
    // anybody else's — an auditor reads this one person at a time, so a page
    // carrying two people's figures would be actively misleading.
    //
    // The full resident worksheet does not fit on one A4 page, so this is
    // expressed as a multiple rather than a fixed count.
    [Fact]
    public void EachEmployeeGetsTheirOwnPages()
    {
        var one = PageCount(PcbCalculationDetailsPdf.Build(Model(Employee())));

        var three = PageCount(PcbCalculationDetailsPdf.Build(Model(
            Employee(name: "Aisyah Binti Rahman"),
            Employee(name: "Tan Wei Ming"),
            Employee(name: "Arjun Subramaniam"))));

        Assert.Equal(one * 3, three);
    }

    // A section heading stranded on the page before its own rows is how the
    // first draft of this document read; `ShowEntire()` on the short sections
    // is what stops it. The AR case is the longest, so it is the one to watch.
    //
    // The bound was ≤2 while the documents asked for "Helvetica" — a font that
    // exists on macOS and NOT on the Linux images CI and production run, where
    // QuestPDF substituted something narrower. Now that the family is pinned
    // to the embedded Lato the same content measures the same everywhere, and
    // it is three pages. Verified by rasterising: every section is whole and
    // no heading is orphaned.
    //
    // This is a smoke bound, not the invariant. The structural check is
    // EachEmployeeGetsTheirOwnPages above, which is font-independent.
    [Fact]
    public void TheLongestWorksheet_StaysWithinItsPageBudget()
    {
        var pages = PageCount(PcbCalculationDetailsPdf.Build(Model(Employee(Resident(Ar())))));

        Assert.True(pages <= 3, $"the AR worksheet should fit three pages, took {pages}");
    }

    // A run that exists but was never generated still has to produce a file
    // rather than throwing — QuestPDF refuses a document with no pages.
    [Fact]
    public void ARunWithNoPayslips_StillRendersAPage()
    {
        var model = new PcbDetailsModel
        {
            OrganizationName = "Globe Engineering Sdn Bhd",
            PeriodLabel = "March 2026",
            Employees = [],
        };

        Assert.True(IsPdf(PcbCalculationDetailsPdf.Render(model)));
    }

    // ─── The AR sections are conditional ────────────────────────────────

    // A clean month has no bonus to explain. Printing PCB(B) and a zero
    // PCB(C) invites the reader to hunt for a deduction never made, so a
    // no-AR page is materially shorter.
    [Fact]
    public void WithoutAdditionalRemuneration_ThePageIsShorter()
    {
        var withoutAr = PcbCalculationDetailsPdf.Render(Model(Employee(Resident())));
        var withAr = PcbCalculationDetailsPdf.Render(Model(Employee(Resident(Ar()))));

        Assert.True(withAr.Length > withoutAr.Length,
            "the AR sections should add content, not be rendered as zeroes either way");
    }

    // ─── Non-residents ──────────────────────────────────────────────────

    // A flat 30% withholding has no bands and no reliefs. Reporting an M or a
    // relief would imply the rate came from somewhere it did not.
    [Fact]
    public void ANonResident_RendersTheFlatRatePage()
    {
        Assert.True(IsPdf(PcbCalculationDetailsPdf.Render(Model(Employee(NonResident())))));
    }

    // ─── A missing snapshot ─────────────────────────────────────────────

    // A page of zeroes reads as "no tax was due", which is a different and
    // much worse claim than "we do not have the working".
    [Fact]
    public void AMissingSnapshot_DoesNotRenderZeroes()
    {
        var missing = new PcbDetailsEmployee
        {
            Name = "Aisyah Binti Rahman",
            Position = "Site Engineer",
            EmployeeCode = "E-001",
            Breakdown = null,
        };

        var withMissing = PcbCalculationDetailsPdf.Render(Model(missing));
        var withBreakdown = PcbCalculationDetailsPdf.Render(Model(Employee()));

        Assert.True(IsPdf(withMissing));
        Assert.True(withMissing.Length < withBreakdown.Length,
            "a missing snapshot should print one sentence, not the whole worksheet");
    }

    // ─── Mixed runs ─────────────────────────────────────────────────────

    // One employee's missing snapshot must not take the rest of the run down.
    [Fact]
    public void OneBrokenSnapshot_DoesNotStopTheOthers()
    {
        var model = Model(
            Employee(),
            new PcbDetailsEmployee { Name = "No Working", Breakdown = null },
            Employee(NonResident(), "Non Resident"));

        // Two residents at two pages each, plus one page apiece for the
        // missing snapshot and the non-resident.
        Assert.Equal(4, PageCount(PcbCalculationDetailsPdf.Build(model)));
    }

    // ─── The snapshot round-trips ───────────────────────────────────────

    // The document reads `Payslip.PcbCalculationJson` written months earlier.
    // If the writer's options and the reader's ever diverge, the breakdown
    // deserialises into a record of zeroes and the page silently claims no tax
    // was due. One shared options instance is the structural fix;
    // this pins that it actually round-trips.
    [Fact]
    public void AStoredBreakdown_ReadsBackIdentical()
    {
        var original = Resident(Ar());

        var json = JsonSerializer.Serialize(original, PayrollSnapshotJson.Options);
        var restored = JsonSerializer.Deserialize<PcbBreakdown>(json, PayrollSnapshotJson.Options);

        Assert.Equal(original, restored);
    }

    // The discriminator is an enum written as a string. A converter dropped
    // from one side would land every payslip on the resident layout.
    [Fact]
    public void TheFormulaDiscriminator_SurvivesTheRoundTrip()
    {
        var json = JsonSerializer.Serialize(NonResident(), PayrollSnapshotJson.Options);

        // The property name is camelCased; the enum VALUE keeps its member
        // name, because the converter is registered without a naming policy.
        Assert.Contains("\"formula\":\"NonResident\"", json);
        Assert.Equal(
            PcbFormula.NonResident,
            JsonSerializer.Deserialize<PcbBreakdown>(json, PayrollSnapshotJson.Options)!.Formula);
    }

    // A 15% approval prints its own formula line rather than implying a band.
    [Fact]
    public void RendersAnEmployeeOnA15PercentApproval()
    {
        var breakdown = Resident() with
        {
            SpecialTaxScheme = AltomateHR.Api.Modules.Employees.Entities.SpecialTaxScheme.RETURNING_EXPERT,
            M = 0m, R = 0.15m, B = 0m,
        };

        Assert.True(IsPdf(PcbCalculationDetailsPdf.Render(Model(Employee(breakdown)))));
    }

    private static int PageCount(IDocument document) =>
        document.GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            RasterDpi = 40,
        }).Count();

    // ─── 5. From the formula to the payslip ────────────────────────────

    // The formula's PCB is before this month's zakat; the payslip's is after
    // it. The page must end on the payslip's figure (the reported case:
    // 2,944.05 formula PCB, RM 1,000 zakat, 1,944.05 on the payslip).
    [Fact]
    public void ThisMonthsZakat_IsTakenOff_SoThePageEndsOnThePayslipPcb()
    {
        var rows = PcbCalculationDetailsPdf.NetPcbRows(2944.05m, 1000m, 0m);

        Assert.Equal(["PCB", "- Zakat", "PCB payable"], rows.Select(r => r.Label));
        Assert.Equal(1000m, rows[1].Amount);
        Assert.Equal(1944.05m, rows[^1].Amount);
        Assert.True(rows[^1].Bold);
    }

    // The reported case (LHDN Q2, March): 3,985.00 formula PCB, RM 12,000
    // zakat paid. The row shows what was PAID, PCB payable is 0.00, and the
    // 8,015.00 the PCB could not absorb is named as carried forward.
    [Fact]
    public void ZakatAboveThePcb_ShowsWhatWasPaid_AndWhatCarriesForward()
    {
        var rows = PcbCalculationDetailsPdf.NetPcbRows(3985m, 12000m, 0m);

        Assert.Equal(["PCB", "- Zakat", "Zakat carried forward", "PCB payable"], rows.Select(r => r.Label));
        Assert.Equal(12000m, rows[1].Amount);
        Assert.Equal(8015m, rows[2].Amount);
        Assert.Equal(0m, rows[^1].Amount);
    }

    // Zakat paid in a month with no PCB at all still shows, all of it carried.
    [Fact]
    public void ZakatWithNoPcb_IsAllCarriedForward()
    {
        var rows = PcbCalculationDetailsPdf.NetPcbRows(0m, 500m, 0m);

        Assert.Equal(500m, rows.Single(r => r.Label == "Zakat carried forward").Amount);
        Assert.Equal(0m, rows[^1].Amount);
    }

    // ─── P before the floor ────────────────────────────────────────────

    // Reliefs larger than the normal remuneration: P is floored to 0 for the
    // tax, but the page shows the negative figure Section 3 builds on
    // (reported case: 0 − (9,000 + 16,000 + 3,500) = −28,500).
    [Fact]
    public void ANegativeP_IsShownBeforeTheFloor()
    {
        var b = new PcbBreakdown { Formula = PcbFormula.Resident, P = 0m, PBeforeFloor = -28500m, D = 9000m, Q = 2000m, C = 8m, Lp1 = 3500m, N = 9 };
        Assert.Equal(-28500m, PcbCalculationDetailsPdf.PBeforeFloor(b));
    }

    // A snapshot from before PBeforeFloor was recorded: the figure is the
    // arithmetic printed beside P.
    [Fact]
    public void AnOlderSnapshot_DerivesPFromItsOwnComponents()
    {
        var b = new PcbBreakdown { Formula = PcbFormula.Resident, P = 0m, D = 9000m, Q = 2000m, C = 8m, Lp1 = 3500m, N = 9 };
        Assert.Equal(-28500m, PcbCalculationDetailsPdf.PBeforeFloor(b));
    }

    [Fact]
    public void APositiveP_IsUnchanged()
    {
        Assert.Equal(52000m, PcbCalculationDetailsPdf.PBeforeFloor(new PcbBreakdown { Formula = PcbFormula.Resident, P = 52000m }));
    }

    [Fact]
    public void WithNoZakatAndNoAdditionalPcb_ThereIsOneRow()
    {
        var row = Assert.Single(PcbCalculationDetailsPdf.NetPcbRows(2944.05m, 0m, 0m));
        Assert.Equal(2944.05m, row.Amount);
        Assert.True(row.Bold);
    }

    [Fact]
    public void ZakatAndAdditionalPcb_BothShow()
    {
        var rows = PcbCalculationDetailsPdf.NetPcbRows(2944.05m, 1000m, 200m);

        Assert.Equal(["PCB", "- Zakat", "+ Add. PCB", "PCB payable"], rows.Select(r => r.Label));
        Assert.Equal(2144.05m, rows[^1].Amount);
    }

    [Fact]
    public void APageWithZakat_StillRenders()
    {
        var model = new PcbDetailsModel
        {
            OrganizationName = "Acme Engineering",
            PeriodLabel = "November 2026",
            Employees = [new PcbDetailsEmployee { Name = "Employee A", Breakdown = Resident(), CurrentMonthZakat = 1000m }],
        };

        Assert.True(PcbCalculationDetailsPdf.Render(model).Length > 0);
    }

    // ─── 15% approvals: each in its own spec's words ───────────────────

    // Q1 is the C-suite case: a reviewer must see D.b.5 / Table 4 and a
    // formula with no rebate, not the REP wording.
    [Fact]
    public void CSuite_CitesTable4_WithNoRebate()
    {
        Assert.Equal("D.b.5, Table 4", PcbCalculationDetailsPdf.SchemeSection(SpecialTaxScheme.C_SUITE));
        Assert.Contains("C-suite", PcbCalculationDetailsPdf.SchemeName(SpecialTaxScheme.C_SUITE));
        Assert.Equal("[(P × R) − (Z + X)] ÷ (n + 1)", PcbCalculationDetailsPdf.SchemeFormula(SpecialTaxScheme.C_SUITE));
    }

    [Theory]
    [InlineData(SpecialTaxScheme.RETURNING_EXPERT, "D.b.3, Table 2", "Returning Expert")]
    [InlineData(SpecialTaxScheme.KNOWLEDGE_WORKER, "D.b.4, Table 3", "knowledge worker")]
    public void RepAndKnowledgeWorkers_KeepTheRebate(SpecialTaxScheme scheme, string section, string name)
    {
        Assert.Equal(section, PcbCalculationDetailsPdf.SchemeSection(scheme));
        Assert.Contains(name, PcbCalculationDetailsPdf.SchemeName(scheme));
        Assert.Equal("[(P × R − T) − (Z + X)] ÷ (n + 1)", PcbCalculationDetailsPdf.SchemeFormula(scheme));
    }
}
