import { useEffect, useState } from "react";
import {
  Building2,
  FolderKanban,
  Network,
  ShieldCheck,
  Users,
  Wallet,
  type LucideIcon,
} from "lucide-react";
import type { SignedInUser } from "@/shared/types/session";
import { getAdminOverview, type AdminOverview as AdminOverviewData } from "../api";
import { ExecutiveOverview } from "./ExecutiveOverview";

type QuickLink = { parent: string; child: string; label: string; hint: string; icon: LucideIcon };

// Base admin config — always available (not module-gated; these are settings, not analytics).
const quickLinks: QuickLink[] = [
  { parent: "company", child: "manage-employee", label: "Manage Employee", hint: "Roles, supervisors & policies", icon: Users },
  { parent: "company", child: "company-structure", label: "Company Structure", hint: "Projects, teams & approval chains", icon: Network },
  { parent: "settings", child: "settings-organization", label: "Organization", hint: "Company profile & geofence", icon: Building2 },
  { parent: "settings", child: "settings-policies", label: "Policies", hint: "Enforcement & entitlements", icon: ShieldCheck },
  { parent: "settings", child: "settings-accounts", label: "Accounts", hint: "Spend limits & mileage", icon: Wallet },
  { parent: "settings", child: "settings-projects", label: "Projects", hint: "Sites & geofence centres", icon: FolderKanban },
];

export function AdminOverview({
  onOpen,
}: {
  // user is still passed by AdminShell (nav contract) but the overview no longer greets.
  user: SignedInUser;
  onOpen: (parentId: string, childId: string) => void;
}) {
  const [overview, setOverview] = useState<AdminOverviewData | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getAdminOverview()
      .then(setOverview)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)));
  }, []);

  return (
    <div className="space-y-6">
      <div>
        <p className="mb-2 text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
          Quick actions
        </p>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {quickLinks.map((link) => {
            const Icon = link.icon;
            return (
              <button
                key={`${link.parent}:${link.child}`}
                type="button"
                onClick={() => onOpen(link.parent, link.child)}
                className="flex items-start gap-3 rounded-[24px] border border-border/70 bg-card/90 p-5 text-left shadow-ambient backdrop-blur-sm transition hover:border-primary/40 hover:text-primary"
              >
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                  <Icon className="h-5 w-5" />
                </span>
                <span className="min-w-0">
                  <span className="block text-sm font-bold text-foreground">{link.label}</span>
                  <span className="mt-0.5 block text-xs text-muted-foreground">{link.hint}</span>
                </span>
              </button>
            );
          })}
        </div>
      </div>

      {error ? (
        <div className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
          {error}
        </div>
      ) : !overview ? (
        <div className="rounded-[28px] border border-border/70 bg-card/90 p-6 text-sm text-muted-foreground">
          Loading overview…
        </div>
      ) : (
        <ExecutiveOverview data={overview} />
      )}
    </div>
  );
}
