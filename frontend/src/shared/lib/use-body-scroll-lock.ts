import { useEffect } from "react";

// Freezes the page behind a modal for as long as it's mounted.
//
// Without this, scrolling over the backdrop scrolls the page underneath: the
// content shifts around behind the dialog, and on a phone the address bar
// collapses mid-interaction. Restoring the previous value rather than clearing
// it means nested modals unwind correctly instead of the inner one releasing
// the lock the outer one still wants.
export function useBodyScrollLock() {
  useEffect(() => {
    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = overflow;
    };
  }, []);
}
