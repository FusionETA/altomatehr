export type SignedInUser = {
  email: string;
  /** Real name from the user's profile; null when none is set. */
  name?: string | null;
  role: string;
  /** Fusioneta staff (SUPERADMIN_EMAILS): the Support page is available. */
  isSuperadmin?: boolean;
  /** Acting inside a customer's company as Fusioneta support. */
  supportMode?: boolean;
  /** Signed in through Altomate (SSO). The account is managed there, so New
   *  company, Change password and Log out are hidden — as the previous
   *  system did. */
  viaSso?: boolean;
  /** The company the session is scoped to. */
  activeOrganizationName?: string | null;
};
