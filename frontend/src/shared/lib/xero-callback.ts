// The Xero OAuth consent screen returns as a FULL page load carrying
// ?xero=connected or ?xero=failed. Two places need that answer — the admin
// shell, to open the Xero card, and the card itself, to say what happened — so
// neither can be the one that reads and clears it.
//
// Captured once here at module load instead, before any component renders, and
// taken out of the address bar straight away so a later reload doesn't replay
// the banner. Reading it inside a component would mean a side effect in a
// render path, which StrictMode's double-invoke turns into a value that
// vanishes on the second pass.

export type XeroCallbackOutcome = "connected" | "failed";

// Why a connect was refused, when the server said:
//   in-use  — that Xero org is already connected to a different company
//   several — more than one org was ticked on Xero's screen
//   none    — Xero returned no org for this sign-in
export type XeroRefusal = { reason: "in-use" | "several" | "none"; org: string | null };

let refusal: XeroRefusal | null = null;

function read(): XeroCallbackOutcome | null {
  if (typeof window === "undefined") return null;

  const params = new URLSearchParams(window.location.search);
  const outcome = params.get("xero");
  if (outcome !== "connected" && outcome !== "failed") return null;

  const reason = params.get("xeroReason");
  if (outcome === "failed" && (reason === "in-use" || reason === "several" || reason === "none")) {
    refusal = { reason, org: params.get("xeroOrg") };
  }

  params.delete("xero");
  params.delete("xeroReason");
  params.delete("xeroOrg");
  const query = params.toString();
  window.history.replaceState(
    null,
    "",
    `${window.location.pathname}${query ? `?${query}` : ""}${window.location.hash}`,
  );
  return outcome;
}

export const xeroCallbackOutcome = read();
export const xeroCallbackRefusal: XeroRefusal | null = refusal;
