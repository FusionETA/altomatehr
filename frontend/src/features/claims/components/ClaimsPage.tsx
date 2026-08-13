import { ClaimsView } from "./ClaimsView";
import { ClaimsApprovals } from "./ClaimsApprovals";

// Employee portal Claims tab: everyone gets their own claims (ClaimsView);
// supervisors/admins also get a team approvals queue below it.
export function ClaimsPage({ role }: { role: string }) {
  return (
    <div className="space-y-6">
      <ClaimsView />
      {role !== "Employee" ? <ClaimsApprovals /> : null}
    </div>
  );
}
