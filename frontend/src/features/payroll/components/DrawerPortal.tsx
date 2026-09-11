import { useCallback, useEffect } from "react";
import { createPortal } from "react-dom";

// A right-hand drawer, rendered at the document root.
//
// The portal is not optional. `position: fixed` resolves against the nearest
// ancestor with a transform, filter or BACKDROP-FILTER rather than against
// the viewport — and the payroll cards carry `backdrop-blur-sm`. A drawer
// rendered in place therefore came out sized to the card it was opened from
// (1074×302 instead of the full 1440×1000) with its own content clipped.
// Mounting on document.body puts it outside every such containing block.
export function DrawerPortal({
  label,
  onClose,
  children,
}: {
  label: string;
  onClose: () => void;
  children: React.ReactNode;
}) {
  // Escape closes. A layer that only dismisses on an outside click strands
  // anyone working from the keyboard.
  const escape = useCallback(
    (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    },
    [onClose],
  );

  useEffect(() => {
    window.addEventListener("keydown", escape);

    // The page behind must not scroll while a drawer is over it — otherwise
    // a wheel gesture at the drawer's end scrolls the run page instead.
    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";

    return () => {
      window.removeEventListener("keydown", escape);
      document.body.style.overflow = overflow;
    };
  }, [escape]);

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex justify-end bg-black/40 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-label={label}
      onClick={onClose}
    >
      <div
        className="h-full w-full max-w-2xl overflow-y-auto border-l border-border bg-background shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {children}
      </div>
    </div>,
    document.body,
  );
}
