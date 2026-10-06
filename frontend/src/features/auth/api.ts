import { apiGet, apiPost } from "@/shared/lib/api-client";
import type { SignedInUser } from "@/shared/types/session";

export type LoginRequest = { email: string; password: string };
export type AuthResponse = SignedInUser & { token: string };

// The session fields the app keeps from a login / refresh / switch.
export const toSignedInUser = (res: AuthResponse): SignedInUser => ({
  email: res.email,
  role: res.role,
  name: res.name,
  isSuperadmin: res.isSuperadmin ?? false,
  supportMode: res.supportMode ?? false,
  viaSso: res.viaSso ?? false,
  activeOrganizationName: res.activeOrganizationName ?? null,
  formerEmployee: res.formerEmployee ?? false,
});

export const login = (body: LoginRequest) => apiPost<AuthResponse>("/auth/login", body);

// Uses the httpOnly refresh cookie (sent automatically) to get a new access token.
export const refresh = () => apiPost<AuthResponse>("/auth/refresh");

// One company the signed-in account can act in. `role` is the account's role in
// THAT org (an Owner here may be a plain Employee elsewhere). `isFormer`: they
// no longer work there — kept for its payslips, listed after current ones.
export type UserOrg = { organizationId: string; name: string; role: string; isFormer?: boolean };

// The companies this account belongs to — drives the org switcher.
export const getOrgs = () => apiGet<UserOrg[]>("/auth/orgs");

// Re-mint the session for another company. The server also rotates the httpOnly
// refresh cookie to the new org, so a full reload afterwards lands there with
// every screen re-fetching against it.
export const switchOrg = (organizationId: string) =>
  apiPost<AuthResponse>(`/auth/switch-org/${organizationId}`);

// A former employee removes a company they no longer work at from their own
// account. When it was the active company the server moves the session to
// their home company (and rotates the cookie), or ends it when none is left —
// either way the caller reloads.
export const leaveOrg = (organizationId: string) =>
  apiPost<AuthResponse | { switched: false } | undefined>(`/auth/leave-org/${organizationId}`);

// Fusioneta support: act inside a customer's company as an Admin. Like a
// switch, the server rotates the refresh cookie, so reload afterwards. What is
// done there is logged in that company as "System (Support)".
export const enterSupport = (organizationId: string) =>
  apiPost<AuthResponse>(`/auth/support/enter/${organizationId}`);

// Leave support mode, back to your own company. Reload afterwards.
export const exitSupport = () => apiPost<AuthResponse>("/auth/support/exit");

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
