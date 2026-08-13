import { useState } from "react";
import { Building2, FolderKanban, Wallet, type LucideIcon } from "lucide-react";
import { OrganizationSettings } from "./OrganizationSettings";
import { ProjectsSettings } from "./ProjectsSettings";
import { AccountsSettings } from "./AccountsSettings";

type SettingsTab = "organization" | "projects" | "accounts";

const tabs: { id: SettingsTab; label: string; icon: LucideIcon }[] = [
  { id: "organization", label: "Organization", icon: Building2 },
  { id: "projects", label: "Projects", icon: FolderKanban },
  { id: "accounts", label: "Accounts", icon: Wallet },
];

export function SettingsView() {
  const [tab, setTab] = useState<SettingsTab>("organization");

  return (
    <div className="space-y-5 sm:space-y-6">
      <div className="flex gap-2 overflow-x-auto pb-0.5">
        {tabs.map(({ id, label, icon: Icon }) => {
          const active = id === tab;
          return (
            <button
              key={id}
              type="button"
              onClick={() => setTab(id)}
              className={`inline-flex shrink-0 items-center gap-2 rounded-full border px-4 py-2 text-sm font-semibold transition-colors ${
                active
                  ? "border-primary bg-primary text-primary-foreground"
                  : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
              }`}
            >
              <Icon className="h-4 w-4" />
              {label}
            </button>
          );
        })}
      </div>

      {tab === "organization" ? <OrganizationSettings /> : null}
      {tab === "projects" ? <ProjectsSettings /> : null}
      {tab === "accounts" ? <AccountsSettings /> : null}
    </div>
  );
}
