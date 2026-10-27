import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { getOrganization } from "../api";

// What the accounts claims are filed against are called. With Xero connected
// they're Xero's chart of accounts; without it they're simply the kinds of
// claim a company accepts, so they read as "claim item types".
export type AccountTerms = {
  xeroConnected: boolean;
  plural: string;   // "Chart of accounts" / "Claim item types"
  singular: string; // "Account" / "Claim item type"
};

export function accountTerms(xeroConnected: boolean): AccountTerms {
  return xeroConnected
    ? { xeroConnected, plural: "Chart of accounts", singular: "Account" }
    : { xeroConnected, plural: "Claim item types", singular: "Claim item type" };
}

// The current company's terms. Until it has loaded, the non-Xero wording —
// most companies don't connect Xero.
export function useAccountTerms(): AccountTerms {
  const org = useCachedQuery("/organizations/current", getOrganization);
  return accountTerms(org.data?.xeroConnected ?? false);
}
