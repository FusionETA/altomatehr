import { useEffect, useState } from "react";
import { LoginForm } from "@/features/auth/components/LoginForm";
import { refresh } from "@/features/auth/api";
import { LoadingScreen } from "@/shared/components/LoadingScreen";
import { setAuthToken } from "@/shared/lib/api-client";
import { launchAppraisify } from "../api";

// Served at /appraisify-relay — the target of every partner notification's
// link (bell click or email click alike), instead of a bare Appraisify URL
// that can't authenticate a cold visitor. This app's bootstrap already
// restores a session from the httpOnly refresh cookie on any fresh page
// load (see App.tsx), so reusing that same check here means a click from an
// email, with zero prior app state, still lands the user signed into
// Appraisify — logging into AltomateHR first only if that cookie is gone.
export function AppraisifyRelay() {
  const [needsLogin, setNeedsLogin] = useState<boolean | null>(null);
  const dest = new URLSearchParams(window.location.search).get("dest");

  useEffect(() => {
    refresh()
      .then((res) => {
        setAuthToken(res.token);
        launchAppraisify(dest);
      })
      .catch(() => setNeedsLogin(true));
    // Intentionally runs once — `dest` comes from the URL this page was
    // loaded with and never changes for the life of this mount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (needsLogin) {
    return (
      <LoginForm
        onSuccess={(res) => {
          setAuthToken(res.token);
          launchAppraisify(dest);
        }}
      />
    );
  }

  return <LoadingScreen />;
}
