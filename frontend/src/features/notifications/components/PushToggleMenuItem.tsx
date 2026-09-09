import { useEffect, useState } from "react";
import { BellOff, BellRing } from "lucide-react";
import { disablePush, enablePush, getPushStatus, isPushSupported, type PushStatus } from "../lib/push";

// Lives in the account menu (not the bell dropdown) since it's a device
// setting, not something about any one notification.
export function PushToggleMenuItem({
  onClose,
  className = "flex w-full items-start gap-3 rounded-xl px-3 py-2.5 text-left text-sm font-semibold text-foreground transition hover:bg-muted",
}: {
  onClose: () => void;
  className?: string;
}) {
  const [status, setStatus] = useState<PushStatus>("unsupported");

  useEffect(() => {
    if (isPushSupported()) getPushStatus().then(setStatus);
  }, []);

  if (!isPushSupported() || status === "denied") return null;

  function handleClick() {
    onClose();
    const action = status === "subscribed" ? disablePush() : enablePush();
    action.catch(() => {
      window.alert("Couldn't update push notification settings — please try again.");
    });
  }

  return (
    <button type="button" onClick={handleClick} className={className}>
      {status === "subscribed" ? (
        <BellOff className="mt-0.5 h-4 w-4 shrink-0" />
      ) : (
        <BellRing className="mt-0.5 h-4 w-4 shrink-0" />
      )}
      {status === "subscribed" ? "Turn off push notifications" : "Enable push notifications"}
    </button>
  );
}
