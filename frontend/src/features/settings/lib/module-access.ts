import { useMemo } from "react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { getModuleAccess } from "../api";

// The signed-in admin's effective modules — the org's plan ∩ their "Manage
// access" grant, exactly what the backend gates admin endpoints on. Null while
// loading, or if the read failed: callers treat that as "hide nothing", since
// the backend still refuses whatever the grant leaves out.
export function useEnabledModules(): ReadonlySet<string> | null {
  const query = useCachedQuery("/organizations/modules", getModuleAccess);
  return useMemo(() => (query.data ? new Set(query.data.enabled) : null), [query.data]);
}
