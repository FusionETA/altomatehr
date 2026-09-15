import { apiGet } from "@/shared/lib/api-client";

// Mints a single-use SSO ticket and navigates the browser to AppraisifyAlt's
// callback with it — a full top-level navigation, not a fetch-followed
// redirect, since the ticket must be spent by the user's own browser.
//
// `dest` is an optional in-app Appraisify path (e.g. "/employee/appraisals/123")
// to land on after redemption instead of Appraisify's default dashboard —
// used by the notification relay page, not the plain account-menu button.
export async function launchAppraisify(dest?: string | null): Promise<void> {
  const query = dest ? `?dest=${encodeURIComponent(dest)}` : "";
  const { redirectUrl } = await apiGet<{ redirectUrl: string }>(`/sso/launch/appraisify${query}`);
  window.location.href = redirectUrl;
}
