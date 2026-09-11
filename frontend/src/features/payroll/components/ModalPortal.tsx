import { useCallback, useEffect } from "react";
import { createPortal } from "react-dom";

// A centred dialog, rendered at the document root.
//
// Same reason as DrawerPortal: `position: fixed` resolves against the
// nearest ancestor with a transform, filter or BACKDROP-FILTER, and the
// payroll cards carry `backdrop-blur-sm` — so a dialog rendered in place
// sizes itself to the card that opened it.
export function ModalPortal({
  label,
  onClose,
  children,
}: {
  label: string;
  onClose: () => void;
  children: React.ReactNode;
}) {
  const escape = useCallback(
    (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    },
    [onClose],
  );

  useEffect(() => {
    window.addEventListener("keydown", escape);

    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";

    return () => {
      window.removeEventListener("keydown", escape);
      document.body.style.overflow = overflow;
    };
  }, [escape]);

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-label={label}
      onClick={onClose}
    >
      <div
        className="flex max-h-[90vh] w-full max-w-2xl flex-col overflow-hidden rounded-[28px] border border-border bg-background shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {children}
      </div>
    </div>,
    document.body,
  );
}
