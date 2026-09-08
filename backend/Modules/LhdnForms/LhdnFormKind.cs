namespace AltomateHR.Api.Modules.LhdnForms;

// Per-employee LHDN statutory PDFs, one per event: a new hire (CP22), a
// cessation (CP22A), leaving Malaysia (CP21), a handover to the next employer
// (TP3), or an on-request MTD statement (PCB 2(II)). Each PDF summarises the
// LHDN-required fields in an AltomateHR layout — HR transcribes onto the
// official LHDN form before submission, or pastes values into e-PCB.
public enum LhdnFormKind
{
    PCB2II,
    CP22,
    CP22A,
    CP21,
    TP3,
}
