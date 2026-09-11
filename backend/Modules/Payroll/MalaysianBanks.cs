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
        new("Malayan Banking Berhad", "MBBEMYKL", EcpPaymentMode.IBG, ["maybank", "malayan banking", "mbb"]),
        new("Maybank Islamic Berhad", "MBISMYKL", EcpPaymentMode.IBG, ["maybank islamic", "maybank islam"]),
        new("CIMB Bank Berhad", "CIBBMYKL", EcpPaymentMode.IBG, ["cimb", "cimb bank"]),
        new("CIMB Islamic Bank Berhad", "CTBBMYKL", EcpPaymentMode.IBG, ["cimb islamic"]),
        new("Public Bank Berhad", "PBBEMYKL", EcpPaymentMode.PBB, ["public bank", "pbb", "public bank berhad"]),
        new("Public Islamic Bank Berhad", "PIBEMYKL", EcpPaymentMode.PBB, ["public islamic"]),
        new("RHB Bank Berhad", "RHBBMYKL", EcpPaymentMode.IBG, ["rhb", "rhb bank"]),
        new("RHB Islamic Bank Berhad", "RHBAMYKL", EcpPaymentMode.IBG, ["rhb islamic"]),
        new("Hong Leong Bank Berhad", "HLBBMYKL", EcpPaymentMode.IBG, ["hong leong", "hlb"]),
        new("Hong Leong Islamic Bank Berhad", "HLIBMYKL", EcpPaymentMode.IBG, ["hong leong islamic"]),
        new("AmBank (M) Berhad", "ARBKMYKL", EcpPaymentMode.IBG, ["ambank", "am bank"]),
        new("AmIslamic Bank (M) Berhad", "AISLMYKL", EcpPaymentMode.IBG, ["ambank islamic", "amislamic"]),
        new("Affin Bank Berhad", "PHBMMYKL", EcpPaymentMode.IBG, ["affin", "affin bank"]),
        new("Affin Islamic Bank Berhad", "AIBBMYKL", EcpPaymentMode.IBG, ["affin islamic"]),
        new("Alliance Bank Malaysia Berhad", "MFBBMYKL", EcpPaymentMode.IBG, ["alliance bank", "alliance"]),
        new("Alliance Islamic Bank Malaysia Berhad", "ALSRMYK1", EcpPaymentMode.IBG, ["alliance islamic"]),
        new("Bank Islam Malaysia Berhad", "BIMBMYKL", EcpPaymentMode.IBG, ["bank islam"]),
        new("Bank Muamalat (Malaysia) Berhad", "BMMBMYKL", EcpPaymentMode.IBG, ["bank muamalat", "muamalat"]),
        new("Bank Rakyat (Bank Kerjasama Rakyat Malaysia Berhad)", "BKRMMYKL", EcpPaymentMode.IBG, ["bank rakyat", "rakyat", "kerjasama rakyat"]),
        new("Bank Simpanan Nasional Berhad", "BSNAMYK1", EcpPaymentMode.IBG, ["bsn", "bank simpanan", "simpanan nasional"]),
        new("Agrobank Berhad", "AGOBMYK1", EcpPaymentMode.IBG, ["agrobank", "agro bank"]),
        new("Al-Rajhi Bank (Malaysia) Berhad", "RJHIMYKL", EcpPaymentMode.IBG, ["al-rajhi", "al rajhi", "rajhi"]),
        new("OCBC Bank (Malaysia) Berhad", "OCBCMYKL", EcpPaymentMode.IBG, ["ocbc"]),
        new("OCBC Al-Amin Bank Berhad", "OABBMYKL", EcpPaymentMode.IBG, ["ocbc al-amin", "al-amin"]),
        new("HSBC Bank Malaysia Berhad", "HBMBMYKL", EcpPaymentMode.IBG, ["hsbc"]),
        new("HSBC Amanah Malaysia Berhad", "HMABMYKL", EcpPaymentMode.IBG, ["hsbc amanah"]),
        new("Standard Chartered Bank (Malaysia) Berhad", "SCBLMYKX", EcpPaymentMode.IBG, ["standard chartered", "scb"]),
        new("Standard Chartered Saadiq (Malaysia) Berhad", "SCSRMYK1", EcpPaymentMode.IBG, ["scb saadiq", "standard chartered saadiq"]),
        new("Citibank Berhad", "CITIMYKL", EcpPaymentMode.IBG, ["citi", "citibank"]),
        new("United Overseas Bank (Malaysia) Berhad", "UOVBMYKL", EcpPaymentMode.IBG, ["uob", "united overseas"]),
        new("MBSB Bank Berhad", "AFBQMYKL", EcpPaymentMode.IBG, ["mbsb"]),
        new("Kuwait Finance House (Malaysia) Berhad", "KFHOMYKL", EcpPaymentMode.IBG, ["kuwait finance", "kfh"]),
        new("Bank of China (Malaysia) Berhad", "BKCHMYKL", EcpPaymentMode.IBG, ["bank of china", "boc"]),
        new("Bank of America (Malaysia) Berhad", "BOFAMY2X", EcpPaymentMode.IBG, ["bank of america", "bofa"]),
        new("Bangkok Bank Berhad", "BKKBMYKL", EcpPaymentMode.IBG, ["bangkok bank"]),
        new("BNP Paribas Malaysia Berhad", "BNPAMYKL", EcpPaymentMode.IBG, ["bnp paribas"]),
        new("China Construction Bank (Malaysia) Berhad", "PCBCMYKL", EcpPaymentMode.IBG, ["china construction", "ccb"]),
        new("Deutsche Bank (Malaysia) Berhad", "DEUTMYKL", EcpPaymentMode.IBG, ["deutsche", "deutsche bank"]),
        new("Industrial and Commercial Bank of China (Malaysia) Berhad", "ICBKMYKL", EcpPaymentMode.IBG, ["icbc", "industrial and commercial"]),
        new("JP Morgan Chase Bank Berhad", "CHASMYKX", EcpPaymentMode.IBG, ["jp morgan", "chase"]),
        new("Mizuho Bank (Malaysia) Berhad", "MHCBMYKA", EcpPaymentMode.IBG, ["mizuho"]),
        new("MUFG Bank (Malaysia) Berhad", "BOTKMYKX", EcpPaymentMode.IBG, ["mufg", "bank of tokyo", "btmu"]),
        new("Sumitomo Mitsui Banking Corporation Malaysia Berhad", "SMBCMYKL", EcpPaymentMode.IBG, ["sumitomo", "smbc"]),
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
    EcpPaymentMode EcpMode,
    // Lower-case, for matching the free-text name on the employee profile.
    IReadOnlyList<string> Aliases);
