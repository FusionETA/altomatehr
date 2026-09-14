import { apiGet, apiPost } from "@/shared/lib/api-client";
import type { SignedInUser } from "@/shared/types/session";

export type LoginRequest = { email: string; password: string };
export type AuthResponse = SignedInUser & { token: string };

export const login = (body: LoginRequest) => apiPost<AuthResponse>("/auth/login", body);

// Uses the httpOnly refresh cookie (sent automatically) to get a new access token.
export const refresh = () => apiPost<AuthResponse>("/auth/refresh");

// One company the signed-in account can act in. `role` is the account's role in
// THAT org (an Owner here may be a plain Employee elsewhere).
export type UserOrg = { organizationId: string; name: string; role: string };

// The companies this account belongs to — drives the org switcher.
export const getOrgs = () => apiGet<UserOrg[]>("/auth/orgs");

// Re-mint the session for another company. The server also rotates the httpOnly
// refresh cookie to the new org, so a full reload afterwards lands there with
// every screen re-fetching against it.
export const switchOrg = (organizationId: string) =>
  apiPost<AuthResponse>(`/auth/switch-org/${organizationId}`);

// Revokes the refresh token server-side + clears the cookie.
export const logout = () => apiPost<void>("/auth/logout");

// Emails a 6-digit reset code.
//
// Always succeeds, even for an address with no account — the server answers 204
// either way so nobody can use this to discover which emails are registered.
// The UI must therefore say "if that address has an account" rather than
// "code sent", or it leaks what the API deliberately doesn't.
export const forgotPassword = (email: string) =>
  apiPost<void>("/auth/forgot-password", { email });

// Redeems the code and sets the new password. Every failure — wrong code,
// expired, too many attempts, unknown email — comes back with the same message,
// for the same reason.
export const resetPassword = (body: { email: string; otp: string; newPassword: string }) =>
  apiPost<void>("/auth/reset-password", body);

// The server's own minimum. Checked here too so the user isn't told to try
// again after a round trip.
export const MIN_PASSWORD_LENGTH = 8;

// Change your own password, proving it's you with the current one.
//
// Every session is revoked on success — including this one — so the caller must
// send the user back to the login screen afterwards. Anything else leaves them
// holding a token the server has already invalidated.
export const changePassword = (body: { currentPassword: string; newPassword: string }) =>
  apiPost<void>("/auth/change-password", body);
