import { useEffect, useState } from "react";
import { LoginForm } from "./features/auth/components/LoginForm";
import { logout, refresh, toSignedInUser } from "./features/auth/api";
import { AdminShell } from "./features/admin/components/AdminShell";
import { EmployeeShell } from "./features/employee-portal/components/EmployeeShell";
import { SupportModeBanner } from "./features/support/components/SupportModeBanner";
import { SupportPage } from "./features/support/components/SupportPage";
import { EnableNotificationsPrompt } from "./features/notifications/components/EnableNotificationsPrompt";
import { LoadingScreen } from "./shared/components/LoadingScreen";
import { setAuthToken } from "./shared/lib/api-client";
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

function App() {
  const [user, setUser] = useState<SignedInUser | null>(null);
  const [booting, setBooting] = useState(true);
  const [supportOpen, setSupportOpen] = useState(supportInUrl);

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

  if (booting) {
    return <LoadingScreen />;
  }

  if (!user) {
    return (
      <LoginForm
        onSuccess={(res) => {
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
