import { createPortal } from "react-dom";

// The round "+" that sits in the bottom-right corner of a list screen.
//
// Rendered into document.body rather than in place, and that is the whole
// point of the component. `position: fixed` resolves against the viewport only
// while no ancestor has a transform, filter, backdrop-filter, perspective or
// contain — any of those makes that ancestor the containing block instead.
//
// The portal shells wrap their content in `animate-in`, whose keyframes set
// BOTH transform and filter for the length of the tab transition. Left in
// place, the button spent those milliseconds positioned against the content
// box — appearing over the page and snapping to the corner when the animation
// ended. Dropping the slide doesn't help: translate3d(0,0,0) is still a
// transform, and the containing block is the wrapper either way.
export function FloatingActionButton({
  label,
  onClick,
  disabled = false,
  children,
}: {
  /** Announced to screen readers; the button itself is icon-only. */
  label: string;
  onClick: () => void;
  disabled?: boolean;
  children: React.ReactNode;
}) {
  return createPortal(
    <button
      type="button"
      aria-label={label}
      onClick={onClick}
      disabled={disabled}
      // bottom-32 clears the mobile tab bar; lg drops it to the corner.
      className="fixed bottom-32 right-5 z-40 flex h-14 w-14 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-panel transition-transform hover:scale-105 active:scale-95 disabled:opacity-50 lg:bottom-8 lg:right-8"
    >
      {children}
    </button>,
    document.body,
  );
}
