import { ClaimsView } from "./ClaimsView";
import { ClaimsApprovals } from "./ClaimsApprovals";

// Claims tab: the sidebar sub-tab decides which view to show —
// "My claims" (ClaimsView) or the supervisor "Claims queue" (ClaimsApprovals).
export function ClaimsPage({ sub }: { sub: string }) {
  if (sub === "claims-queue") return <ClaimsApprovals />;
  return <ClaimsView />;
}
