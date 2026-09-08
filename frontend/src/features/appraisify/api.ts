import { apiGet } from "@/shared/lib/api-client";

// Mints a single-use SSO ticket and navigates the browser to AppraisifyAlt's
// callback with it — a full top-level navigation, not a fetch-followed
// redirect, since the ticket must be spent by the user's own browser.
export async function launchAppraisify(): Promise<void> {
  const { redirectUrl } = await apiGet<{ redirectUrl: string }>("/sso/launch/appraisify");
  window.location.href = redirectUrl;
}
