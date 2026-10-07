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

// The signed-in admin's limits beyond which modules they have, for hiding
// controls the server would refuse anyway:
//   · canManage(m)      — module m at Manage, not just View;
//   · canChangeSettings — the "Change settings" switch;
//   · allEmployees      — false when limited to some policies, which also
//                         makes company-wide payroll (runs, filings) view-only.
// Everything allowed while loading or on a failed read: the backend still
// refuses, so a missing hint only costs a clear error message.
export type MyAccess = {
  canManage: (module: string) => boolean;
  canChangeSettings: boolean;
  allEmployees: boolean;
};

export function useMyAccess(): MyAccess {
  const query = useCachedQuery("/organizations/modules", getModuleAccess);
  return useMemo(() => {
    const data = query.data;
    return {
      canManage: (m: string) => !data || data.levels?.[m] !== "View",
      canChangeSettings: data?.canChangeSettings ?? true,
      allEmployees: data?.allEmployees ?? true,
    };
  }, [query.data]);
}
