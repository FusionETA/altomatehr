export type SignedInUser = {
  email: string;
  /** Real name from the user's profile; null when none is set. */
  name?: string | null;
  role: string;
};
