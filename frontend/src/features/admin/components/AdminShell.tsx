import { useEffect, useMemo, useRef, useState } from "react";
import { ExternalLink, LogOut, MoreVertical } from "lucide-react";
import { NotificationBell } from "@/features/notifications/components/NotificationBell";
import { launchAppraisify } from "@/features/appraisify/api";
import { AccountsSettings } from "@/features/settings/components/AccountsSettings";
import { EmployeesSettings } from "@/features/settings/components/EmployeesSettings";
import { OrganizationSettings } from "@/features/settings/components/OrganizationSettings";
import { PoliciesSettings } from "@/features/settings/components/PoliciesSettings";
import { ProjectsSettings } from "@/features/settings/components/ProjectsSettings";
import { CompanyStructure } from "@/features/settings/components/CompanyStructure";
import { buildInitials, buildName } from "@/features/employee-portal/lib/employee-formatters";
import { HorizontalScrollArea } from "@/shared/components/HorizontalScrollArea";
import type { SignedInUser } from "@/shared/types/session";
import { adminNav, defaultChildOf, findNavItem } from "../lib/nav";
import { AdminAttendance } from "./AdminAttendance";
import { AdminPayroll } from "@/features/payroll/components/AdminPayroll";
import { ActivityLog } from "./ActivityLog";
import { AdminClaims } from "./AdminClaims";
import { AdminLeave } from "./AdminLeave";
import { AdminOverview } from "./AdminOverview";

export function AdminShell({
  user,
  onLogout,
}: {
  user: SignedInUser;
  onLogout: () => void;
}) {
  const [activeParent, setActiveParent] = useState("overview");
  const [activeChild, setActiveChild] = useState("overview");
  const [accountMenuOpen, setAccountMenuOpen] = useState(false);
  const accountMenuRef = useRef<HTMLDivElement | null>(null);

  const activeItem = findNavItem(activeParent);
  const initials = useMemo(() => buildInitials(user.email), [user.email]);
  const displayName = useMemo(() => buildName(user.email), [user.email]);

  useEffect(() => {
    if (!accountMenuOpen) return;

    function handlePointerDown(event: PointerEvent) {
      if (!accountMenuRef.current?.contains(event.target as Node)) {
        setAccountMenuOpen(false);
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setAccountMenuOpen(false);
    }

    document.addEventListener("pointerdown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("pointerdown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [accountMenuOpen]);

  function selectParent(id: string) {
    const item = findNavItem(id);
    setActiveParent(id);
    setActiveChild(defaultChildOf(item));
  }

  function open(parentId: string, childId: string) {
    setActiveParent(parentId);
    setActiveChild(childId);
  }

  // A notification's url is a bare frontend path (e.g. "/claims") — this app
  // has no router, so map the paths the backend actually sends to nav ids.
  // Anything unmapped just closes the bell without navigating.
  function navigateFromNotification(url: string) {
    const path = url.split("?")[0];
    if (path === "/claims") selectParent("claims");
    else if (path === "/leave") selectParent("leave");
    else if (path === "/attendance" || path === "/overtime") selectParent("attendance");
  }

  return (
    <div className="min-h-screen bg-background lg:grid lg:grid-cols-[300px_1fr]">
      <aside className="hidden min-h-screen flex-col border-r border-border/60 bg-card/72 p-6 backdrop-blur-xl lg:flex">
        <div className="self-center text-center">
          <img src="/brand-logo.png" alt="AltomateHR logo" className="h-auto w-[148px] object-contain" />
          <p className="mt-2 text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
            Enterprise Admin
          </p>
        </div>

        <nav className="mt-10 space-y-1.5">
          {adminNav.map((item) => {
            const Icon = item.icon;
            const active = item.id === activeParent;

            return (
              <div key={item.id}>
                <button
                  type="button"
                  onClick={() => selectParent(item.id)}
                  className={`flex w-full items-center gap-3 rounded-[22px] border px-4 py-3 text-left text-sm font-semibold transition-all ${
                    active
                      ? "border-primary/40 bg-card text-primary shadow-ambient"
                      : "border-transparent text-muted-foreground hover:bg-muted hover:text-foreground"
                  }`}
                >
                  <Icon className="h-4 w-4" />
                  <span>{item.label}</span>
                </button>

                {item.children && active ? (
                  <div className="ml-5 mt-1 space-y-0.5 border-l border-border/60 pl-4">
                    {item.children.map((child) => {
                      const childActive = child.id === activeChild;
                      return (
                        <button
                          key={child.id}
                          type="button"
                          onClick={() => setActiveChild(child.id)}
                          className={`block w-full rounded-lg px-3 py-1.5 text-left text-xs font-semibold transition-colors ${
                            childActive
                              ? "bg-primary/10 text-primary"
                              : "text-muted-foreground hover:bg-muted hover:text-foreground"
                          }`}
                        >
                          {child.label}
                        </button>
                      );
                    })}
                  </div>
                ) : null}
              </div>
            );
          })}
        </nav>
      </aside>

      <div className="flex min-h-screen min-w-0 flex-col">
        <header className="sticky top-0 z-30 border-b border-border/55 bg-background/82 backdrop-blur-xl">
          <div className="mx-auto flex w-full max-w-6xl items-center justify-between gap-4 px-4 py-4 sm:px-6 lg:px-8">
            <div className="min-w-0">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                Admin Portal
              </p>
              <h1 className="truncate text-2xl font-black tracking-tight text-foreground">
                {activeItem.label}
              </h1>
              <p className="mt-1 hidden text-sm text-muted-foreground sm:block">AltomateHR</p>
            </div>

            <div className="flex items-center gap-2 sm:gap-3">
              <div className="hidden sm:block">
                <NotificationBell onNavigate={navigateFromNotification} />
              </div>

              <div
                ref={accountMenuRef}
                className="relative flex items-center gap-2 rounded-full border border-border/60 bg-card/90 px-2 py-2 shadow-ambient sm:gap-3 sm:px-3"
              >
                <div className="flex h-10 w-10 items-center justify-center rounded-full bg-primary text-sm font-bold text-primary-foreground">
                  {initials}
                </div>
                <div className="hidden text-right sm:block">
                  <p className="text-sm font-bold text-foreground">{displayName}</p>
                  <p className="text-xs text-muted-foreground">{user.role}</p>
                </div>
                <button
                  type="button"
                  aria-label="Account menu"
                  aria-expanded={accountMenuOpen}
                  onClick={() => setAccountMenuOpen((open) => !open)}
                  className="flex h-9 w-9 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
                >
                  <MoreVertical className="h-4 w-4" />
                </button>

                {accountMenuOpen ? (
                  <div className="absolute right-0 top-[calc(100%+0.6rem)] z-50 w-64 overflow-hidden rounded-2xl border border-border/70 bg-card/98 p-2 text-left shadow-[0_18px_48px_rgba(76,26,134,0.14)] backdrop-blur-xl">
                    <div className="border-b border-border/60 px-3 py-2.5">
                      <p className="truncate text-sm font-bold text-foreground">{displayName}</p>
                      <p className="truncate text-xs text-muted-foreground">{user.email}</p>
                    </div>

                    <button
                      type="button"
                      onClick={() => {
                        setAccountMenuOpen(false);
                        launchAppraisify().catch(() => {
                          window.alert("Couldn't open Appraisify — please try again.");
                        });
                      }}
                      className="mt-2 flex w-full items-start gap-3 rounded-xl px-3 py-2.5 text-left text-sm font-semibold text-foreground transition hover:bg-muted"
                    >
                      <ExternalLink className="mt-0.5 h-4 w-4 shrink-0" />
                      Launch Appraisify
                    </button>

                    <button
                      type="button"
                      onClick={() => {
                        setAccountMenuOpen(false);
                        onLogout();
                      }}
                      className="mt-1 flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm font-semibold text-destructive transition hover:bg-destructive/10"
                    >
                      <LogOut className="h-4 w-4 shrink-0" />
                      Log out
                    </button>
                  </div>
                ) : null}
              </div>
            </div>
          </div>
        </header>

        {/* Mobile module switcher — the desktop sidebar is hidden below lg. */}
        <HorizontalScrollArea className="px-4 py-3 lg:hidden" contentClassName="gap-2">
          {adminNav.map((item) => {
            const active = item.id === activeParent;
            const Icon = item.icon;
            return (
              <button
                key={item.id}
                type="button"
                onClick={() => selectParent(item.id)}
                className={`inline-flex shrink-0 items-center gap-2 rounded-full border px-3 py-2 text-xs font-semibold transition-colors ${
                  active
                    ? "border-primary bg-primary text-primary-foreground"
                    : "border-border/60 bg-card text-muted-foreground"
                }`}
              >
                <Icon className="h-4 w-4" />
                {item.label}
              </button>
            );
          })}
        </HorizontalScrollArea>

        {/* Mobile sub-nav for the active module. */}
        {activeItem.children ? (
          <HorizontalScrollArea className="px-4 pb-3 lg:hidden" contentClassName="gap-2">
            {activeItem.children.map((child) => {
              const childActive = child.id === activeChild;
              return (
                <button
                  key={child.id}
                  type="button"
                  onClick={() => setActiveChild(child.id)}
                  className={`shrink-0 rounded-full border px-3 py-1.5 text-xs font-semibold transition-colors ${
                    childActive
                      ? "border-primary/40 bg-primary/10 text-primary"
                      : "border-border/60 bg-card text-muted-foreground"
                  }`}
                >
                  {child.label}
                </button>
              );
            })}
          </HorizontalScrollArea>
        ) : null}

        <main className="flex-1 pb-16 lg:pb-10">
          <div className="mx-auto w-full max-w-6xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
            <AdminContent activeChild={activeChild} user={user} onOpen={open} />
          </div>
        </main>
      </div>
    </div>
  );
}

function AdminContent({
  activeChild,
  user,
  onOpen,
}: {
  activeChild: string;
  user: SignedInUser;
  onOpen: (parentId: string, childId: string) => void;
}) {
  switch (activeChild) {
    case "overview":
      return <AdminOverview user={user} onOpen={onOpen} />;

    // Company / Employee
    case "manage-employee":
      return <EmployeesSettings />;
    case "company-structure":
      return <CompanyStructure />;

    // System Settings
    case "settings-organization":
      return <OrganizationSettings />;
    case "settings-accounts":
      return <AccountsSettings />;
    case "settings-projects":
      return <ProjectsSettings />;
    case "settings-policies":
      return <PoliciesSettings />;

    // Org-wide attendance roll-call — the backend already returns every
    // employee's records to admins.
    case "attendance":
      return <AdminAttendance />;

    // Org-wide claims oversight: what needs attention, every claim behind it,
    // and the month-end export/import.
    case "claims":
      return <AdminClaims />;

    // Payroll runs, the payslips behind them, and the org's payroll rules.
    // `onOpen` lets its overview hand off to Manage Employee rather than
    // standing up a second place to manage people.
    case "payroll":
      return <AdminPayroll onOpen={onOpen} />;

    case "leave":
      return <AdminLeave />;
    case "audit":
      return <ActivityLog />;

    default:
      return <AdminOverview user={user} onOpen={onOpen} />;
  }
}
