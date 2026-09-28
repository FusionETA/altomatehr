using C = AltomateHR.Api.Modules.Payroll.PayrollAdjustmentCategories;

namespace AltomateHR.Api.Modules.Payroll;

// Borang PCB/TP1 (1/2026) — LHDN MTD Spec 2026, Exhibit 1 (pages 37–38).
//
// The employee's monthly claim for deductions and rebates. The spec (p.36,
// item 16) requires the system to let the employee print and save it, and to
// show the current-month and cumulative figure for each item for audit (p.28).
//
// Pure: the form's items in its own order and wording, and a builder that
// fills them from what payroll actually GRANTED — each TP1 row's relief after
// its item and group limits — so the printed form agrees with the PCB that was
// withheld. Labels are the form's (Malay), abridged only where a line is long.
public static class Tp1Form
{
    // One printed line. A group (C1, C3, C4, C11, C16) prints a header carrying
    // the shared limit and totals, then its sub-items; a stand-alone item is a
    // single line.
    public sealed record Item(
        string Ref,
        string Label,
        decimal? Limit,
        IReadOnlyList<string> Categories,
        IReadOnlyList<Item>? Parts = null);

    public sealed record Row(
        string Ref, string Label, decimal? Limit, decimal Current, decimal Cumulative, bool IsPart);

    // Bahagian C — deductions (feed LP1 / ΣLP).
    public static readonly IReadOnlyList<Item> Deductions =
    [
        new("C1", "Perbelanjaan untuk ibu bapa / datuk nenek", 8000m, [],
        [
            new("a", "Rawatan perubatan, keperluan khas dan perkhidmatan penjagaan", null, [C.DeductTp1ParentsMedical]),
            new("b", "Rawatan pergigian", null, [C.DeductTp1ParentsDental]),
            new("c", "Pemeriksaan perubatan penuh termasuk kos pemvaksinan (Terhad RM1,000)", 1000m, [C.DeductTp1ParentsMedicalExam]),
        ]),
        new("C2", "Peralatan sokongan asas untuk kegunaan diri sendiri / pasangan / anak / ibu bapa yang kurang upaya", 6000m, [C.DeductTp1SupportingEquipment]),
        new("C3", "Yuran pengajian (diri sendiri)", 7000m, [],
        [
            new("a–b", "Peringkat selain Sarjana / Doktor Falsafah (bidang tertentu), atau Sarjana / Doktor Falsafah", null, [C.DeductTp1EducationFees]),
            new("c", "Kursus peningkatan kemahiran atau kemajuan diri (Terhad RM2,000)", 2000m, [C.DeductTp1Upskilling]),
        ]),
        new("C4", "Perbelanjaan rawatan perubatan bagi diri sendiri / pasangan / anak", 10000m, [],
        [
            new("a–b", "Penyakit serius; rawatan kesuburan ke atas diri sendiri / pasangan", null, [C.DeductTp1SeriousDiseaseMedical]),
            new("c", "Pemvaksinan ke atas diri sendiri / pasangan / anak (Terhad RM1,000)", 1000m, [C.DeductTp1Vaccination]),
            new("d", "Pemeriksaan atau rawatan pergigian ke atas diri sendiri / pasangan / anak (Terhad RM1,000)", 1000m, [C.DeductTp1Dental]),
            new("e", "Pemeriksaan perubatan penuh, kesihatan mental, ujian pengesanan penyakit dan peralatan pemeriksaan kendiri (Terhad RM1,000)", 1000m, [C.DeductTp1MedicalExam]),
            new("f", "Penilaian diagnosis, program intervensi awal atau rawatan pemulihan anak kurang upaya pembelajaran 18 tahun ke bawah (Terhad RM10,000)", 10000m, [C.DeductTp1LearningDisability]),
        ]),
        new("C5", "Gaya hidup — buku / jurnal / majalah, komputer / telefon pintar / tablet, internet, kursus kemajuan diri", 2500m, [C.DeductTp1Lifestyle]),
        new("C6", "Gaya hidup — peralatan sukan, sewa / fi fasiliti sukan, fi pertandingan, gimnasium / latihan sukan", 1000m, [C.DeductTp1SportsEquipment]),
        new("C7", "Pembelian peralatan penyusuan ibu untuk anak berumur 2 tahun dan ke bawah (sekali setiap 2 tahun taksiran)", 1000m, [C.DeductTp1Breastfeeding]),
        new("C8", "Yuran penghantaran anak berumur 12 tahun dan ke bawah ke taman asuhan / tadika / pusat jagaan harian / pusat transit berdaftar", 3000m, [C.DeductTp1ChildcareFees]),
        new("C9", "Tabungan bersih dalam Skim Simpanan Pendidikan Nasional (SSPN)", 8000m, [C.DeductTp1Sspn]),
        new("C10", "Bayaran alimoni kepada bekas isteri", 4000m, [C.DeductTp1Alimony]),
        new("C11", "Insurans nyawa / KWSP Sukarela", 7000m, [],
        [
            new("a", "KWSP Sukarela (Terhad RM4,000 termasuk KWSP wajib)", 4000m, [C.DeductTp1VoluntaryEpf]),
            new("b", "Insurans nyawa / KWSP Sukarela (Terhad RM3,000)", 3000m, [C.DeductTp1LifeInsurance]),
        ]),
        new("C12", "Skim persaraan swasta dan anuiti tertangguh", 3000m, [C.DeductTp1Prs]),
        new("C13", "Insurans pendidikan dan perubatan", 4000m, [C.DeductTp1MedicalInsurance]),
        // Applied automatically from the employee's own payroll contributions
        // (Payroll Settings), so never claimed here — printed for completeness.
        new("C14", "Caruman kepada PERKESO (dikira secara automatik daripada caruman gaji)", 350m, []),
        new("C15", "Pengecasan kenderaan elektrik / mesin kompos atau rincih sisa makanan / CCTV kegunaan isi rumah", 2500m, [C.DeductTp1EvCharging]),
        new("C16", "Bayaran faedah pinjaman rumah kediaman pertama", 7000m, [],
        [
            new("a", "Harga rumah sehingga RM500,000", 7000m, [C.DeductTp1HousingLoan500k]),
            new("b", "Harga rumah melebihi RM500,000 hingga RM750,000", 5000m, [C.DeductTp1HousingLoan750k]),
        ]),
        new("C17", "Bayaran fi kemasukan ke pusat pelancongan dan program kebudayaan / kesenian dalam negara", 1000m, [C.DeductTp1Tourism]),
        // Not a line on the form: TP1 amounts an admin recorded without an item.
        new("—", "Lain-lain potongan (tidak dikelaskan)", null, [C.DeductTp1Other, C.DeductTp1]),
    ];

    // Bahagian D — rebates (offset the PCB ringgit for ringgit).
    public static readonly IReadOnlyList<Item> Rebates =
    [
        new("D1a", "Zakat selain yang dibayar melalui potongan daripada gaji bulanan", null, [C.DeductZakatTp1]),
        new("D1b", "Levi pelepasan bagi perjalanan umrah / perjalanan bagi tujuan keagamaan (Terhad 2 kali tuntutan seumur hidup)", null, [C.DeductDepartureLevyTp1]),
    ];

    // Every category the form reads, so a claims list can tell who claimed.
    public static readonly IReadOnlySet<string> Categories =
        Deductions.Concat(Rebates)
            .SelectMany(i => i.Categories.Concat((i.Parts ?? []).SelectMany(p => p.Categories)))
            .ToHashSet(StringComparer.Ordinal);

    // The printed rows, filled from per-category amounts: `current` is this
    // month's, `cumulative` the year to date including this month.
    public static IReadOnlyList<Row> Build(
        IReadOnlyList<Item> items,
        IReadOnlyDictionary<string, decimal> current,
        IReadOnlyDictionary<string, decimal> cumulative)
    {
        decimal Sum(IReadOnlyDictionary<string, decimal> amounts, IEnumerable<string> categories) =>
            Money.Round2(categories.Sum(c => amounts.GetValueOrDefault(c)));

        var rows = new List<Row>();
        foreach (var item in items)
        {
            var categories = item.Categories.Concat((item.Parts ?? []).SelectMany(p => p.Categories)).ToList();
            rows.Add(new Row(item.Ref, item.Label, item.Limit,
                Sum(current, categories), Sum(cumulative, categories), IsPart: false));

            foreach (var part in item.Parts ?? [])
            {
                rows.Add(new Row(part.Ref, part.Label, part.Limit,
                    Sum(current, part.Categories), Sum(cumulative, part.Categories), IsPart: true));
            }
        }

        return rows;
    }

    // The references someone claimed this month — "C4c, C11b" — for the list.
    public static IReadOnlyList<string> ClaimedRefs(IReadOnlyDictionary<string, decimal> current)
    {
        var refs = new List<string>();
        foreach (var item in Deductions.Concat(Rebates))
        {
            if (item.Parts is { } parts)
            {
                refs.AddRange(parts
                    .Where(p => p.Categories.Any(c => current.GetValueOrDefault(c) > 0m))
                    .Select(p => item.Ref + p.Ref.Replace("–", "")));
            }
            else if (item.Categories.Any(c => current.GetValueOrDefault(c) > 0m))
            {
                refs.Add(item.Ref == "—" ? "Lain-lain" : item.Ref);
            }
        }

        return refs;
    }
}
