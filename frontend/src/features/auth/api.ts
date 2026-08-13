import { apiPost } from "@/shared/lib/api-client";
import type { SignedInUser } from "@/shared/types/session";

export type LoginRequest = { email: string; password: string };
export type AuthResponse = SignedInUser & { token: string };

export const login = (body: LoginRequest) => apiPost<AuthResponse>("/auth/login", body);

// Uses the httpOnly refresh cookie (sent automatically) to get a new access token.
export const refresh = () => apiPost<AuthResponse>("/auth/refresh");

// Revokes the refresh token server-side + clears the cookie.
export const logout = () => apiPost<void>("/auth/logout");
