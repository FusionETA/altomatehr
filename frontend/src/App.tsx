import { useEffect, useState } from "react";
import { LoginForm } from "./features/auth/components/LoginForm";
import { logout, refresh } from "./features/auth/api";
import { EmployeeShell } from "./features/employee/components/EmployeeShell";
import type { EmployeeView } from "./features/employee/lib/types";
import { LoadingScreen } from "./shared/components/LoadingScreen";
import { setAuthToken } from "./shared/lib/api-client";
import type { SignedInUser } from "./shared/types/session";

function App() {
  const [user, setUser] = useState<SignedInUser | null>(null);
  const [activeView, setActiveView] = useState<EmployeeView>("dashboard");
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
          setActiveView("dashboard");
        }}
      />
    );
  }

  return (
    <EmployeeShell
      user={user}
      activeView={activeView}
      onChangeView={setActiveView}
      onLogout={async () => {
        await logout().catch(() => {});
        setAuthToken(null);
        setUser(null);
        setActiveView("dashboard");
      }}
    />
  );
}

export default App;
