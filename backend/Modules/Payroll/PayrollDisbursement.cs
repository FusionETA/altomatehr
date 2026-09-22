namespace AltomateHR.Api.Modules.Payroll;

// Which bulk-payroll upload file a company's disbursement bank takes.
//
// This constrains only the PAYOR account — the company's own bank, which
// decides the portal the file is uploaded to. It does NOT constrain where
// employees bank: every format routes to any Malaysian bank by BIC or BNM
// code, so a Maybank payor can pay a Public Bank employee.
public enum PayrollFileFormat
{
    PbEcpXlsx,
    MbbM2eTxt,
    CimbBizChannelTxt,
    // Hong Leong publishes TWO upload channels taking different files. The
    // admin picks at download time rather than nominating a portal here.
    HlbConnect,
}

public static class PayrollDisbursement
{
    // Stored in PayrollSettings.PayrollBankName when the company banks
    // somewhere no upload file is generated for.
    //
    // Deliberately distinct from null: null means nobody has configured this
    // yet, while "Other" records that an admin looked at the list and none
    // applied. Both produce no file — but only the first is worth nagging
    // about.
    public const string OtherBank = "Other";

    // One option per FORMAT, not per legal entity: the conventional and
    // Islamic arms of a bank file identically, so offering both would ask an
    // admin to choose between two answers that do the same thing.
    public static readonly IReadOnlyList<(string Value, string Label, PayrollFileFormat? Format)> Options =
    [
        ("Malayan Banking Berhad", "Maybank (incl. Maybank Islamic)", PayrollFileFormat.MbbM2eTxt),
        ("CIMB Bank Berhad", "CIMB (incl. CIMB Islamic)", PayrollFileFormat.CimbBizChannelTxt),
        ("Public Bank Berhad", "Public Bank (incl. Public Islamic)", PayrollFileFormat.PbEcpXlsx),
        ("Hong Leong Bank Berhad", "Hong Leong Bank (incl. Hong Leong Islamic)", PayrollFileFormat.HlbConnect),
        (OtherBank, "Other bank (no upload file)", null),
    ];

    // Null for a bank with no native format — including "Other", and including
    // a name stored before this list existed. Callers must handle "no file"
    // rather than assuming one exists and serving the wrong bank's layout.
    public static PayrollFileFormat? FormatFor(string? bankName)
    {
        var name = bankName?.Trim();
        if (string.IsNullOrEmpty(name)) return null;

        // Match the picker's own values first, then fall back to the bank
        // register's aliases so a name typed before the picker existed
        // ("maybank", "HLB") still resolves.
        foreach (var (value, _, format) in Options)
        {
            if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase)) return format;
        }

        var bank = MalaysianBanks.Find(name);
        if (bank is null) return null;
        return Options.FirstOrDefault(o =>
            string.Equals(o.Value, bank.Name, StringComparison.OrdinalIgnoreCase)).Format;
    }
}

// Which Hong Leong upload channel a run's file is for.
//
// HLB is the only bank here with two: the same payment goes to Connect First
// as a fixed-width TXT or to ConnectBiz as the CBIZ spreadsheet, and the
// portals do not accept each other's file. Nothing in payroll data says which
// one a company uses, so the admin picks at download time — and until they do
// there is no safe default to guess.
public enum HlbChannel
{
    ConnectFirst,
    ConnectBiz,
}
