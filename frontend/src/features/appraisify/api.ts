import { apiGet } from "@/shared/lib/api-client";

function runningAsInstalledApp(): boolean {
  const iosStandalone = (navigator as Navigator & { standalone?: boolean }).standalone === true;
  return iosStandalone || window.matchMedia?.("(display-mode: standalone)").matches === true;
}

async function mintLaunchUrl(dest?: string | null): Promise<string> {
  const query = dest ? `?dest=${encodeURIComponent(dest)}` : "";
  const { redirectUrl } = await apiGet<{ redirectUrl: string }>(`/sso/launch/appraisify${query}`);
  return redirectUrl;
}

// Mints a single-use SSO ticket and navigates the browser to AppraisifyAlt's
// callback with it — a full top-level navigation, not a fetch-followed
// redirect, since the ticket must be spent by the user's own browser.
//
// `dest` is an optional in-app Appraisify path (e.g. "/employee/appraisals/123")
// to land on after redemption instead of Appraisify's default dashboard.
// Same-tab: used by the notification relay page, which exists only to hand
// off to Appraisify.
export async function launchAppraisify(dest?: string | null): Promise<void> {
  window.location.href = await mintLaunchUrl(dest);
}

// The account-menu "Launch Appraisify" button — opens Appraisify in a new tab
// so AltomateHR stays open behind it. MUST be called synchronously from the
// click handler: the blank tab is opened before the ticket request so it
// still counts as a user gesture (opening it after the await gets it eaten
// by popup blockers, Safari especially). Not opened with "noopener" — that
// makes window.open return null — so the opener link is severed by hand.
export async function launchAppraisifyInNewTab(): Promise<void> {
  // Installed to the home screen (iOS especially), window.open hands off to a
  // separate Safari view that this page may not be able to steer — the user
  // would be left on a blank sheet. Same-tab there, as before.
  if (runningAsInstalledApp()) return launchAppraisify();

  const tab = window.open("about:blank", "_blank");
  if (!tab) {
    // Popup blocked anyway — fall back to the old same-tab behaviour.
    return launchAppraisify();
  }
  tab.opener = null;
  try {
    tab.location.href = await mintLaunchUrl();
  } catch (error) {
    tab.close();
    throw error;
  }
}
