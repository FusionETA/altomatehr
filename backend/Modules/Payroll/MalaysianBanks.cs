namespace AltomateHR.Api.Modules.Payroll;

// Malaysian bank metadata — canonical name, BIC, and the Public Bank ECP
// payment mode.
//
// Source: BNM's IBG member list plus Public Bank's published BIC reference.
//
// `EmployeeProfile.BankName` is FREE TEXT: an admin may type "Maybank",
// "MAYBANK BERHAD" or "Malayan Banking" for the same bank. So every entry
// carries aliases and `Find` matches loosely — a bank file that silently
// routed someone's pay to the wrong institution because of a spelling is the
// failure being designed against, which is why an unrecognised name returns
// null and the caller refuses rather than guessing.
public static class MalaysianBanks
{
    public static readonly IReadOnlyList<MalaysianBank> All =
    [
        new("Malayan Banking Berhad", "MBBEMYKL", "27", "MBBB", EcpPaymentMode.IBG, ["maybank", "malayan banking", "mbb"]),
        new("Maybank Islamic Berhad", "MBISMYKL", "27", "MBBB", EcpPaymentMode.IBG, ["maybank islamic", "maybank islam"]),
        new("CIMB Bank Berhad", "CIBBMYKL", "35", "CIMB", EcpPaymentMode.IBG, ["cimb", "cimb bank"]),
        new("CIMB Islamic Bank Berhad", "CTBBMYKL", "35", "CIMB", EcpPaymentMode.IBG, ["cimb islamic"]),
        new("Public Bank Berhad", "PBBEMYKL", "33", "PBBB", EcpPaymentMode.PBB, ["public bank", "pbb", "public bank berhad"]),
        new("Public Islamic Bank Berhad", "PIBEMYKL", "33", "PBBB", EcpPaymentMode.PBB, ["public islamic"]),
        new("RHB Bank Berhad", "RHBBMYKL", "18", "RHBB", EcpPaymentMode.IBG, ["rhb", "rhb bank"]),
        new("RHB Islamic Bank Berhad", "RHBAMYKL", "18", "RHBB", EcpPaymentMode.IBG, ["rhb islamic"]),
        new("Hong Leong Bank Berhad", "HLBBMYKL", "24", "HLBB", EcpPaymentMode.IBG, ["hong leong", "hlb"]),
        new("Hong Leong Islamic Bank Berhad", "HLIBMYKL", "24", "HLBB", EcpPaymentMode.IBG, ["hong leong islamic"]),
        new("AmBank (M) Berhad", "ARBKMYKL", "08", "AMBB", EcpPaymentMode.IBG, ["ambank", "am bank"]),
        new("AmIslamic Bank (M) Berhad", "AISLMYKL", "08", "AMBB", EcpPaymentMode.IBG, ["ambank islamic", "amislamic"]),
        new("Affin Bank Berhad", "PHBMMYKL", "32", "PABB", EcpPaymentMode.IBG, ["affin", "affin bank"]),
        new("Affin Islamic Bank Berhad", "AIBBMYKL", "32", "PABB", EcpPaymentMode.IBG, ["affin islamic"]),
        new("Alliance Bank Malaysia Berhad", "MFBBMYKL", "12", "ALBB", EcpPaymentMode.IBG, ["alliance bank", "alliance"]),
        new("Alliance Islamic Bank Malaysia Berhad", "ALSRMYK1", "12", "ALBB", EcpPaymentMode.IBG, ["alliance islamic"]),
        new("Bank Islam Malaysia Berhad", "BIMBMYKL", "45", "BIMB", EcpPaymentMode.IBG, ["bank islam"]),
        new("Bank Muamalat (Malaysia) Berhad", "BMMBMYKL", "41", "BMMB", EcpPaymentMode.IBG, ["bank muamalat", "muamalat"]),
        new("Bank Rakyat (Bank Kerjasama Rakyat Malaysia Berhad)", "BKRMMYKL", "02", "BKRM", EcpPaymentMode.IBG, ["bank rakyat", "rakyat", "kerjasama rakyat"]),
        new("Bank Simpanan Nasional Berhad", "BSNAMYK1", "10", "BSNB", EcpPaymentMode.IBG, ["bsn", "bank simpanan", "simpanan nasional"]),
        new("Agrobank Berhad", "AGOBMYK1", "49", "AGRO", EcpPaymentMode.IBG, ["agrobank", "agro bank"]),
        new("Al-Rajhi Bank (Malaysia) Berhad", "RJHIMYKL", "53", "ARB", EcpPaymentMode.IBG, ["al-rajhi", "al rajhi", "rajhi"]),
        new("OCBC Bank (Malaysia) Berhad", "OCBCMYKL", "29", "OCBC", EcpPaymentMode.IBG, ["ocbc"]),
        new("OCBC Al-Amin Bank Berhad", "OABBMYKL", "29", "OCBC", EcpPaymentMode.IBG, ["ocbc al-amin", "al-amin"]),
        new("HSBC Bank Malaysia Berhad", "HBMBMYKL", "22", "HSBC", EcpPaymentMode.IBG, ["hsbc"]),
        new("HSBC Amanah Malaysia Berhad", "HMABMYKL", "22", "HSBC", EcpPaymentMode.IBG, ["hsbc amanah"]),
        new("Standard Chartered Bank (Malaysia) Berhad", "SCBLMYKX", "14", "SCBB", EcpPaymentMode.IBG, ["standard chartered", "scb"]),
        new("Standard Chartered Saadiq (Malaysia) Berhad", "SCSRMYK1", "14", "SCBB", EcpPaymentMode.IBG, ["scb saadiq", "standard chartered saadiq"]),
        new("Citibank Berhad", "CITIMYKL", "17", "CITI", EcpPaymentMode.IBG, ["citi", "citibank"]),
        new("United Overseas Bank (Malaysia) Berhad", "UOVBMYKL", "26", "UOBB", EcpPaymentMode.IBG, ["uob", "united overseas"]),
        new("MBSB Bank Berhad", "AFBQMYKL", "75", "AFB", EcpPaymentMode.IBG, ["mbsb"]),
        new("Kuwait Finance House (Malaysia) Berhad", "KFHOMYKL", "47", "KFHB", EcpPaymentMode.IBG, ["kuwait finance", "kfh"]),
        new("Bank of China (Malaysia) Berhad", "BKCHMYKL", "42", "BOCM", EcpPaymentMode.IBG, ["bank of china", "boc"]),
        new("Bank of America (Malaysia) Berhad", "BOFAMY2X", "07", "BOFA", EcpPaymentMode.IBG, ["bank of america", "bofa"]),
        new("Bangkok Bank Berhad", "BKKBMYKL", "04", "BKKB", EcpPaymentMode.IBG, ["bangkok bank"]),
        new("BNP Paribas Malaysia Berhad", "BNPAMYKL", "60", "BNPM", EcpPaymentMode.IBG, ["bnp paribas"]),
        new("China Construction Bank (Malaysia) Berhad", "PCBCMYKL", "65", "CCBM", EcpPaymentMode.IBG, ["china construction", "ccb"]),
        new("Deutsche Bank (Malaysia) Berhad", "DEUTMYKL", "19", "DEUM", EcpPaymentMode.IBG, ["deutsche", "deutsche bank"]),
        new("Industrial and Commercial Bank of China (Malaysia) Berhad", "ICBKMYKL", "59", "ICBC", EcpPaymentMode.IBG, ["icbc", "industrial and commercial"]),
        new("JP Morgan Chase Bank Berhad", "CHASMYKX", "48", "JPMC", EcpPaymentMode.IBG, ["jp morgan", "chase"]),
        new("Mizuho Bank (Malaysia) Berhad", "MHCBMYKA", "73", "MHCB", EcpPaymentMode.IBG, ["mizuho"]),
        new("MUFG Bank (Malaysia) Berhad", "BOTKMYKX", "52", "BTMU", EcpPaymentMode.IBG, ["mufg", "bank of tokyo", "btmu"]),
        new("Sumitomo Mitsui Banking Corporation Malaysia Berhad", "SMBCMYKL", "51", "SMBC", EcpPaymentMode.IBG, ["sumitomo", "smbc"]),
    ];

    // Best match for a free-text bank name, or null when nothing fits.
    //
    // Exact alias first, then the LONGEST partial match — length is the
    // tie-break so "bank islam" cannot outrank "bank islam malaysia" when the
    // admin typed the longer one.
    public static MalaysianBank? Find(string? bankName)
    {
        var needle = bankName?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(needle)) return null;

        foreach (var bank in All)
        {
            if (bank.Aliases.Contains(needle, StringComparer.Ordinal)) return bank;
        }

        MalaysianBank? best = null;
        var bestLength = 0;

        foreach (var bank in All)
        {
            foreach (var alias in bank.Aliases)
            {
                if (!needle.Contains(alias, StringComparison.Ordinal)
                    && !alias.Contains(needle, StringComparison.Ordinal))
                {
                    continue;
                }

                var length = Math.Min(alias.Length, needle.Length);
                if (length > bestLength) { best = bank; bestLength = length; }
            }

            var canonical = bank.Name.ToLowerInvariant();
            if (needle.Contains(canonical, StringComparison.Ordinal) && canonical.Length > bestLength)
            {
                best = bank;
                bestLength = canonical.Length;
            }
        }

        return best;
    }

    // True when a free-text bank name is the same institution as `bank`, or
    // its Islamic sibling.
    //
    // Every bulk format splits intra-bank credits (a free book transfer) from
    // interbank ones (IBG, which costs money and needs a routing code), so
    // each renderer has to ask this about its own bank. Comparing HlbCode is
    // what makes the Islamic arm count: Hong Leong Islamic shares HLBB, so a
    // Hong Leong Islamic employee is correctly paid as intra-bank.
    public static bool IsSameInstitution(string? bankName, MalaysianBank bank)
    {
        var found = Find(bankName);
        return found is not null && found.HlbCode == bank.HlbCode;
    }
}

// How a Public Bank ECP payment is routed.
public enum EcpPaymentMode
{
    // Within Public Bank — instant and free.
    PBB,

    // Interbank GIRO to another domestic bank. Carries a fee and settles
    // T+0/T+1, and REQUIRES the recipient's BIC.
    IBG,

    // RENTAS, for high-value same-day transfers. No employee is paid this way
    // in practice, but the mode exists in the ECP spec.
    REN,
}

public sealed record MalaysianBank(
    string Name,
    // Bank Identifier Code (BIC / SWIFT). The ECP file needs it for every
    // IBG payment.
    string Bic,
    // Two-digit BNM / IBG participating-bank code, for the formats that route
    // by national code instead of BIC — the CIMB BizChannel file's "BNM Code"
    // column. Islamic subsidiaries share their parent's code, which is how the
    // IBG scheme (and CIMB's own list) treats them.
    string BnmCode,
    // Hong Leong's own 4-character IBG code, from the CBIZ Bulk Payroll
    // template's "Bank Code (IBG)" sheet. A DIFFERENT scheme from BnmCode, and
    // easy to misread: PABB is AFFIN (from its old name Perwira Affin Bank)
    // while Public Bank is PBBB. Transcribed, never inferred.
    string HlbCode,
    EcpPaymentMode EcpMode,
    // Lower-case, for matching the free-text name on the employee profile.
    IReadOnlyList<string> Aliases);
