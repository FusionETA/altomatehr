import { useEffect } from "react";
import { trackPage } from "./tracker";

// Reports a page view whenever the shell's view changes. Called by each shell
// with its own view id ("payroll/runs", "leave") — the shells own navigation,
// so this sits beside them rather than inside shared/lib/use-url-nav.
export function usePageTracking(parent: string, child?: string | null) {
  const name = child ? `${parent}/${child}` : parent;
  useEffect(() => {
    trackPage(name);
  }, [name]);
}
