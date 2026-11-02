import { useEffect, useState } from "react";
import { LoginForm } from "./features/auth/components/LoginForm";
import { logout, refresh, toSignedInUser } from "./features/auth/api";
import { AdminShell } from "./features/admin/components/AdminShell";
import { EmployeeShell } from "./features/employee-portal/components/EmployeeShell";
import { SupportModeBanner } from "./features/support/components/SupportModeBanner";
import { SupportPage } from "./features/support/components/SupportPage";
import { EnableNotificationsPrompt } from "./features/notifications/components/EnableNotificationsPrompt";
import { LoadingScreen } from "./shared/components/LoadingScreen";
import { onSessionClosed, setAuthToken } from "./shared/lib/api-client";
import { connectRealtime, disconnectRealtime } from "./shared/lib/realtime-client";
import type { SignedInUser } from "./shared/types/session";

// Admins and owners get the admin portal; everyone else (supervisors,
// employees) gets the employee portal. Approval duties are decided by team
// seat inside the employee portal, not by this split.
function isAdminRole(role: string) {
  return role === "Admin" || role === "Owner";
}

// The Fusioneta support page sits outside both shells, behind its own query
// parameter so a reload or Back keeps you where you were.
const SUPPORT_PARAM = "support";
const supportInUrl = () => new URLSearchParams(window.location.search).has(SUPPORT_PARAM);

// Carries "why you were signed out" across the reload that follows it.
const NOTICE_KEY = "altomate:session-notice";
function takeNotice(): string | null {
  try {
    const notice = sessionStorage.getItem(NOTICE_KEY);
    sessionStorage.removeItem(NOTICE_KEY);
    return notice;
  } catch {
    return null;
  }
}

function App() {
  const [user, setUser] = useState<SignedInUser | null>(null);
  const [booting, setBooting] = useState(true);
  const [supportOpen, setSupportOpen] = useState(supportInUrl);
  // Why the session ended or moved, when the server closed it (company deactivated).
  const [notice, setNotice] = useState<string | null>(takeNotice);

  useEffect(() => {
    const onPop = () => setSupportOpen(supportInUrl());
    window.addEventListener("popstate", onPop);
    return () => window.removeEventListener("popstate", onPop);
  }, []);

  // Access tokens live in memory, so refresh restores the session after a page reload.
  useEffect(() => {
    refresh()
      .then((res) => {
        setAuthToken(res.token);
        setUser(toSignedInUser(res));
        connectRealtime();
      })
      .catch(() => {
        /* no valid refresh cookie: stay logged out */
      })
      .finally(() => setBooting(false));

    return () => disconnectRealtime();
  }, []);

  // The company was deactivated under them. Reloaded like a company switch,
  // so no screen keeps its data: someone in another company lands there (the
  // refresh picks it), anyone else on the sign-in page — told why either way.
  useEffect(
    () =>
      onSessionClosed((message) => {
        try {
          sessionStorage.setItem(NOTICE_KEY, message);
        } catch {
          /* private mode: they're signed out without the reason */
        }
        window.location.reload();
      }),
    [],
  );

  if (booting) {
    return <LoadingScreen />;
  }

  if (!user) {
    return (
      <LoginForm
        notice={notice}
        onSuccess={(res) => {
          setNotice(null);
          setAuthToken(res.token);
          setUser(toSignedInUser(res));
          connectRealtime();
        }}
      />
    );
  }

  const handleLogout = async () => {
    disconnectRealtime();
    await logout().catch(() => {});
    setAuthToken(null);
    setUser(null);
  };

  const openSupport = user.isSuperadmin
    ? () => {
        window.history.pushState(null, "", `${window.location.pathname}?${SUPPORT_PARAM}`);
        setSupportOpen(true);
      }
    : undefined;

  const closeSupport = () => {
    window.history.pushState(null, "", window.location.pathname);
    setSupportOpen(false);
  };

  // The server refuses the page's calls for anyone else; this only keeps a
  // stray ?support from rendering an empty page.
  if (supportOpen && user.isSuperadmin) {
    return <SupportPage onBack={closeSupport} />;
  }

  return (
    <>
      {notice ? (
        <div className="flex items-center justify-between gap-3 border-b border-destructive/20 bg-destructive/5 px-4 py-2.5 text-sm font-medium text-destructive">
          <span>
            {notice} You're now in {user.activeOrganizationName}.
          </span>
          <button
            type="button"
            onClick={() => setNotice(null)}
            className="shrink-0 rounded-full px-2 py-1 text-xs font-semibold transition hover:bg-destructive/10"
          >
            Dismiss
          </button>
        </div>
      ) : null}
      {user.supportMode ? <SupportModeBanner organizationName={user.activeOrganizationName} /> : null}
      {isAdminRole(user.role) ? (
        <AdminShell user={user} onLogout={handleLogout} onOpenSupport={openSupport} />
      ) : (
        <EmployeeShell user={user} onLogout={handleLogout} onOpenSupport={openSupport} />
      )}
      <EnableNotificationsPrompt />
    </>
  );
}

export default App;
