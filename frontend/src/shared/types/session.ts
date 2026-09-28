export type SignedInUser = {
  email: string;
  /** Real name from the user's profile; null when none is set. */
  name?: string | null;
  role: string;
  /** Fusioneta staff (SUPERADMIN_EMAILS): the Support page is available. */
  isSuperadmin?: boolean;
  /** Acting inside a customer's company as Fusioneta support. */
  supportMode?: boolean;
  /** The company the session is scoped to. */
  activeOrganizationName?: string | null;
};
