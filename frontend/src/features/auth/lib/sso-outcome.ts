// A partner SSO hand-off (New-Altomate → /sso/callback) that the backend
// refused comes back as a full page load of "/?sso=expired": the ticket was
// older than two minutes, already used, or its person is no longer an admin
// of that company. The sign-in form was left bare under that URL, so it read
// as a random logout with no hint of what to do.
//
// Captured once at module load, the same way as ../../../shared/lib/xero-callback.ts,
// and taken out of the address bar so a reload doesn't show it again.

function read(): boolean {
  if (typeof window === "undefined") return false;
  const params = new URLSearchParams(window.location.search);
  if (params.get("sso") !== "expired") return false;

  params.delete("sso");
  const query = params.toString();
  window.history.replaceState(
    null,
    "",
    `${window.location.pathname}${query ? `?${query}` : ""}${window.location.hash}`,
  );
  return true;
}

export const ssoLinkExpired = read();
