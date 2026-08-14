import { useEffect, useState } from "react";
import { LoginForm } from "./features/auth/components/LoginForm";
import { logout, refresh } from "./features/auth/api";
import { AdminShell } from "./features/admin/components/AdminShell";
import { EmployeeShell } from "./features/employee-portal/components/EmployeeShell";
import { LoadingScreen } from "./shared/components/LoadingScreen";
import { setAuthToken } from "./shared/lib/api-client";
import type { SignedInUser } from "./shared/types/session";

// Admins and owners get the admin portal; everyone else (supervisors,
// employees) gets the employee portal. Approval duties are decided by team
// seat inside the employee portal, not by this split.
function isAdminRole(role: string) {
  return role === "Admin" || role === "Owner";
}

function App() {
  const [user, setUser] = useState<SignedInUser | null>(null);
  const [booting, setBooting] = useState(true);

  // Access tokens live in memory, so refresh restores the session after a page reload.
  useEffect(() => {
    refresh()
      .then((res) => {
        setAuthToken(res.token);
        setUser({ email: res.email, role: res.role });
      })
      .catch(() => {
        /* no valid refresh cookie: stay logged out */
      })
      .finally(() => setBooting(false));
  }, []);

  if (booting) {
    return <LoadingScreen />;
  }

  if (!user) {
    return (
      <LoginForm
        onSuccess={(res) => {
          setAuthToken(res.token);
          setUser({ email: res.email, role: res.role });
        }}
      />
    );
  }

  const handleLogout = async () => {
    await logout().catch(() => {});
    setAuthToken(null);
    setUser(null);
  };

  if (isAdminRole(user.role)) {
    return <AdminShell user={user} onLogout={handleLogout} />;
  }

  return <EmployeeShell user={user} onLogout={handleLogout} />;
}

export default App;
