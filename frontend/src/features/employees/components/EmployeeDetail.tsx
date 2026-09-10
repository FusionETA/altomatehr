import { useEffect, useMemo, useState } from "react";
import { ArrowLeft, Check, CircleAlert, LoaderCircle, Plus, RotateCcw, Trash2 } from "lucide-react";
import {
  CHILD_ABILITY,
  CHILD_ABILITY_LABELS,
  CHILD_DEDUCTION,
  CHILD_DEDUCTION_LABELS,
  CHILD_STUDYING,
  CHILD_STUDYING_LABELS,
  GENDERS,
  ID_TYPES,
  ID_TYPE_LABELS,
  MARITAL_STATUSES,
  PAYMENT_METHODS,
  PAYMENT_METHOD_LABELS,
  STAFF_ROLES,
  SALARY_TYPES,
  SOCSO_SCHEMES,
  SOCSO_SCHEME_LABELS,
  isAdultChild,
  deleteEmployeeDocument,
  downloadEmployeeDocument,
  downloadLhdnForm,
  getEmployeeDocuments,
  getEmployeeProfile,
  getLhdnForms,
  parseChildRelief,
  parseFixedAllowances,
  saveEmployeeProfile,
  serializeList,
  toUpdateEmployee,
  updateEmployee,
  uploadEmployeeDocument,
  type ChildRelief,
  type Employee,
  type EmployeeDocument,
  type EmployeeProfile,
  type FixedAllowance,
  type LhdnFormDescriptor,
} from "../api";
import { saveFile } from "@/shared/lib/api-client";
import type { Policy } from "@/features/policies/api";
import { getProjects, type Project } from "@/features/settings/api";
import {
  addTeamMember,
  clearApproverOverride,
  getApproverOptions,
  getTeams,
  layerLabel,
  removeTeamMember,
  setApproverOverride,
  type LayerApproverOptions,
  type Team,
} from "@/features/teams/api";
import {
  DEFAULT_CATEGORY,
  categoriesFor,
  kindOf,
  labelForCategory,
  type AdjustmentKind,
} from "../lib/payroll-adjustments";
import {
  calculateAge,
  epfBranchInfo,
  formatEpfRate,
  isMalaysianNationality,
  pickEpfBranch,
  recommendSocsoScheme,
  socsoSchemeNeedsManualChoice,
} from "../lib/statutory";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import {
  Field,
  Group,
  LockedValue,
  Money,
  NONE,
  Num,
  Percent,
  Picker,
  RepeaterRow,
  Stack,
  Text,
  Toggle,
  TriToggle,
} from "./profile-fields";
import {
  isReadyForPayroll,
  missingFields,
  type SectionId,
} from "./employee-profile-sections";
import { useCachedQuery } from "@/shared/lib/use-cached-query";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

// The same four the previous system uses, holding the same cards: who the
// person is, what they're paid, what's deducted, and where they sit here.
const SECTIONS: { id: SectionId; label: string }[] = [
  { id: "personal", label: "Personal" },
  { id: "employment", label: "Employment" },
  { id: "statutory", label: "Statutory" },
  { id: "company", label: "Company" },
  { id: "documents", label: "Documents" },
];

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

/** "" from an empty input means "no value", which the API spells null. */
function blank(value: string) {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// The fields that live on the org membership rather than the HR profile. They
// are edited here alongside it, but travel on a different endpoint.
type Placement = {
  role: string;
  policyId: string;
  name: string;
  email: string;
  employeeNumber: string;
  jobTitle: string;
  joinDate: string;
};

// One employee's whole record.
//
// A 67-field profile can't be a single scrolling form, and it can't be five
// disconnected tabs either — an admin needs to know what's still missing
// without opening each one. So: a section rail carrying per-section
// completeness, one header that stays put, and a save bar that only appears
// once something has actually changed.
export function EmployeeDetail({
  employee,
  policies,
  onBack,
  onSaved,
}: {
  employee: Employee;
  policies: Policy[];
  onBack: () => void;
  onSaved: (updated: Employee) => void;
}) {
  const [section, setSection] = useState<SectionId>("personal");
  const [profile, setProfile] = useState<EmployeeProfile | null>(null);
  // The last-saved state, to tell "changed" from "loaded".
  const [baseline, setBaseline] = useState<EmployeeProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // The scheme as actually saved, before any auto-fill — distinct from
  // `profile.socsoScheme`, which gets pre-filled with the recommendation on
  // load. Used only to tell "still on the auto-fill" from "admin re-picked
  // the same value", for the hint text below the field.
  const [originalSocsoScheme, setOriginalSocsoScheme] = useState<EmployeeProfile["socsoScheme"]>(null);

  // Documents are their own API surface (upload/delete are immediate actions,
  // not part of the profile's Save/Discard flow), so they get their own
  // loading/error state rather than riding along on `profile`.
  const [documents, setDocuments] = useState<EmployeeDocument[]>([]);
  const [documentsLoading, setDocumentsLoading] = useState(true);
  const [documentsError, setDocumentsError] = useState<string | null>(null);
  const [uploadingDocument, setUploadingDocument] = useState(false);

  const [lhdnForms, setLhdnForms] = useState<LhdnFormDescriptor[]>([]);
  const [lhdnFormsLoading, setLhdnFormsLoading] = useState(true);
  const [lhdnFormsError, setLhdnFormsError] = useState<string | null>(null);
  const [lhdnYear, setLhdnYear] = useState<number>(new Date().getFullYear());
  const [downloadingLhdnKind, setDownloadingLhdnKind] = useState<string | null>(null);

  // Team/approval-chain assignment — its own API surface (Teams feature),
  // edited immediately rather than riding along on the profile Save/Discard.
  const [teams, setTeams] = useState<Team[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [teamsError, setTeamsError] = useState<string | null>(null);
  const [savingAssignment, setSavingAssignment] = useState(false);
  // The add-flow narrows in the order the previous system uses: pick a project,
  // which decides the teams on offer, which decides the levels on offer.
  const [assigningProjectId, setAssigningProjectId] = useState(NONE);
  const [assigningTeamId, setAssigningTeamId] = useState(NONE);
  const [assigningLayer, setAssigningLayer] = useState(0);
  // Keyed by team id — an employee can be on several teams at once, each with
  // its own set of layers above them.
  const [approverOptionsByTeam, setApproverOptionsByTeam] = useState<Record<string, LayerApproverOptions[]>>({});
  const [approverOptionsLoading, setApproverOptionsLoading] = useState(false);
  const [savingLayer, setSavingLayer] = useState<{ teamId: string; layer: number } | null>(null);

  const initialPlacement: Placement = {
    role: employee.role,
    policyId: employee.policyId ?? NONE,
    name: employee.name,
    email: employee.email,
    employeeNumber: employee.employeeNumber ?? "",
    jobTitle: employee.jobTitle ?? "",
    joinDate: employee.joinDate?.slice(0, 10) ?? "",
  };
  const [placement, setPlacement] = useState<Placement>(initialPlacement);
  const [placementBase, setPlacementBase] = useState<Placement>(initialPlacement);

  useEffect(() => {
    setLoading(true);
    setError(null);
    getEmployeeProfile(employee.id)
      .then((p) => {
        setOriginalSocsoScheme(p.socsoScheme);
        // Pre-fill an unset scheme with PERKESO's age-based recommendation.
        // Baseline gets the same seed so this alone doesn't count as a
        // change — only an actual edit shows "Unsaved changes".
        const recommendedScheme = recommendSocsoScheme({
          dateOfBirth: p.dateOfBirth,
          isMalaysianCitizen: isMalaysianNationality(p.nationality),
        });
        const seeded =
          p.socsoScheme === null && recommendedScheme
            ? { ...p, socsoScheme: recommendedScheme }
            : p;
        setProfile(seeded);
        setBaseline(seeded);
        // Two join dates existed before this screen did: one on the membership
        // (which pro-rates leave) and one on the profile. Where only the
        // profile has a value, show it rather than an empty field — the first
        // save then writes it to both and they stop disagreeing.
        if (!employee.joinDate && p.joinDate) {
          const seeded = p.joinDate.slice(0, 10);
          setPlacement((cur) => ({ ...cur, joinDate: seeded }));
          setPlacementBase((cur) => ({ ...cur, joinDate: seeded }));
        }
      })
      .catch((e: unknown) => setError(message(e, "Could not load this employee's profile.")))
      .finally(() => setLoading(false));
  }, [employee.id, employee.joinDate]);

  useEffect(() => {
    setDocumentsLoading(true);
    setDocumentsError(null);
    getEmployeeDocuments(employee.id)
      .then(setDocuments)
      .catch((e: unknown) => setDocumentsError(message(e, "Could not load documents.")))
      .finally(() => setDocumentsLoading(false));
  }, [employee.id]);

  useEffect(() => {
    setLhdnFormsLoading(true);
    setLhdnFormsError(null);
    getLhdnForms(employee.id)
      .then(setLhdnForms)
      .catch((e: unknown) => setLhdnFormsError(message(e, "Could not load LHDN forms.")))
      .finally(() => setLhdnFormsLoading(false));
  }, [employee.id]);

  // Teams and projects are org-wide reference data, not this employee's — they
  // were being refetched for every person an admin opened. The profile itself
  // is deliberately left uncached below: it is the thing being edited here, and
  // its load seeds a SOCSO recommendation and the dirty-check baseline.
  const teamsQuery = useCachedQuery("/teams", getTeams);
  const projectsQuery = useCachedQuery("/projects", getProjects);
  const teamsLoading = teamsQuery.loading || projectsQuery.loading;

  useEffect(() => {
    if (teamsQuery.data) setTeams(teamsQuery.data);
  }, [teamsQuery.data]);
  useEffect(() => {
    if (projectsQuery.data) setProjects(projectsQuery.data);
  }, [projectsQuery.data]);
  useEffect(() => {
    setTeamsError(teamsQuery.error ?? projectsQuery.error);
  }, [teamsQuery.error, projectsQuery.error]);

  // This employee's membership(s) across every team, freshly derived from
  // `teams` on every render — the same data Company Structure's roster
  // reads, so an edit from either screen shows up on both.
  const myMemberships = useMemo(
    () =>
      teams
        .map((team) => ({ team, member: team.members.find((m) => m.employeeId === employee.id) }))
        .filter((x): x is { team: Team; member: NonNullable<typeof x.member> } => x.member !== undefined),
    [teams, employee.id],
  );
  // A stable key that changes exactly when the set of memberships or any of
  // their layers changes — what the approver options actually depend on.
  // A team belongs to exactly one project, so a membership IS the answer to
  // "which team on this project" — presented project-first, the way an admin
  // thinks about it and the way the previous system asks for it.
  const myProjects = useMemo(
    () =>
      myMemberships
        .map(({ team, member }) => ({
          project: projects.find((p) => p.id === team.projectId) ?? {
            id: team.projectId,
            name: "Unknown project",
          },
          team,
          member,
        }))
        .sort((a, b) => a.project.name.localeCompare(b.project.name)),
    [myMemberships, projects],
  );

  // Only projects that have a team to join, and not one they are already on:
  // two memberships on one project would make the chain resolution ambiguous.
  const assignableProjects = useMemo(() => {
    const taken = new Set(myMemberships.map(({ team }) => team.projectId));
    return projects.filter((p) => !taken.has(p.id) && teams.some((t) => t.projectId === p.id));
  }, [projects, teams, myMemberships]);

  // Named so the empty-picker message can point at the actual blocker rather
  // than leaving an admin staring at a dropdown with nothing in it.
  const projectsWithoutTeams = useMemo(
    () => projects.filter((p) => !teams.some((t) => t.projectId === p.id)),
    [projects, teams],
  );

  const assigningProjectTeams = useMemo(
    () => (assigningProjectId === NONE ? [] : teams.filter((t) => t.projectId === assigningProjectId)),
    [teams, assigningProjectId],
  );
  const assigningTeam = teams.find((t) => t.id === assigningTeamId);

  // What the chosen level would mean, computed from the team roster the same
  // way the backend's implicit default does: everyone at each layer above.
  // A preview only — the real approvers come back from the API after the
  // assignment, and can then be narrowed per layer.
  const assigningPreviewApprovers = useMemo(() => {
    if (!assigningTeam) return [];
    const rows: { layer: number; label: string; names: string[] }[] = [];
    for (let layer = assigningLayer + 1; layer < assigningTeam.layerCount; layer++) {
      rows.push({
        layer,
        label: layerLabel(assigningTeam, layer),
        names: assigningTeam.members
          .filter((m) => m.layer === layer && m.employeeId !== employee.id)
          .map((m) => m.email ?? m.employeeId),
      });
    }
    return rows;
  }, [assigningTeam, assigningLayer, employee.id]);

  const membershipKey = myMemberships.map((m) => `${m.team.id}:${m.member.layer}`).join(",");

  useEffect(() => {
    if (myMemberships.length === 0) {
      setApproverOptionsByTeam({});
      return;
    }
    setApproverOptionsLoading(true);
    setTeamsError(null);
    Promise.all(
      myMemberships.map((m) =>
        getApproverOptions(m.team.id, employee.id).then((options) => [m.team.id, options] as const),
      ),
    )
      .then((entries) => setApproverOptionsByTeam(Object.fromEntries(entries)))
      .catch((e: unknown) => setTeamsError(message(e, "Could not load approvers.")))
      .finally(() => setApproverOptionsLoading(false));
  }, [membershipKey, employee.id]);

  async function handleUploadDocument(file: File) {
    setUploadingDocument(true);
    setDocumentsError(null);
    try {
      const uploaded = await uploadEmployeeDocument(employee.id, file);
      setDocuments((docs) => [uploaded, ...docs]);
    } catch (e: unknown) {
      setDocumentsError(message(e, "Could not upload this file."));
    } finally {
      setUploadingDocument(false);
    }
  }

  async function handleDeleteDocument(documentId: string) {
    setDocumentsError(null);
    try {
      await deleteEmployeeDocument(employee.id, documentId);
      setDocuments((docs) => docs.filter((d) => d.id !== documentId));
    } catch (e: unknown) {
      setDocumentsError(message(e, "Could not remove this document."));
    }
  }

  async function handleDownloadDocument(doc: EmployeeDocument) {
    try {
      saveFile(await downloadEmployeeDocument(employee.id, doc));
    } catch (e: unknown) {
      setDocumentsError(message(e, "Could not download this file."));
    }
  }

  async function handleDownloadLhdnForm(form: LhdnFormDescriptor) {
    setDownloadingLhdnKind(form.kind);
    setLhdnFormsError(null);
    try {
      saveFile(await downloadLhdnForm(employee.id, form.kind, form.needsYearPicker ? lhdnYear : null));
    } catch (e: unknown) {
      setLhdnFormsError(message(e, "Could not generate this form."));
    } finally {
      setDownloadingLhdnKind(null);
    }
  }

  async function handleAssignTeam() {
    if (assigningTeamId === NONE) return;
    setSavingAssignment(true);
    setTeamsError(null);
    try {
      const updated = await addTeamMember(assigningTeamId, { employeeId: employee.id, layer: assigningLayer });
      setTeams((ts) => ts.map((t) => (t.id === updated.id ? updated : t)));
      setAssigningProjectId(NONE);
      setAssigningTeamId(NONE);
      setAssigningLayer(0);
    } catch (e: unknown) {
      setTeamsError(message(e, "Could not assign this employee to a team."));
    } finally {
      setSavingAssignment(false);
    }
  }

  async function handleChangeLayer(teamId: string, layer: number) {
    setSavingAssignment(true);
    setTeamsError(null);
    try {
      const updated = await addTeamMember(teamId, { employeeId: employee.id, layer });
      setTeams((ts) => ts.map((t) => (t.id === updated.id ? updated : t)));
    } catch (e: unknown) {
      setTeamsError(message(e, "Could not change this employee's layer."));
    } finally {
      setSavingAssignment(false);
    }
  }

  // Switching the team within a project is a leave-and-join, since a membership
  // is (team, level) and there is no "move" on the API. The level is carried
  // across so changing team doesn't silently demote anyone.
  async function handleMoveTeam(fromTeamId: string, toTeamId: string, layer: number) {
    setSavingAssignment(true);
    setTeamsError(null);
    try {
      const left = await removeTeamMember(fromTeamId, employee.id);
      const target = teams.find((t) => t.id === toTeamId);
      // The new team may be shallower than the old one.
      const safeLayer = Math.min(layer, Math.max(0, (target?.layerCount ?? 1) - 1));
      const joined = await addTeamMember(toTeamId, { employeeId: employee.id, layer: safeLayer });
      setTeams((ts) =>
        ts.map((t) => (t.id === left.id ? left : t.id === joined.id ? joined : t)),
      );
      setApproverOptionsByTeam((cur) => {
        const { [fromTeamId]: _dropped, ...rest } = cur;
        return rest;
      });
    } catch (e: unknown) {
      setTeamsError(message(e, "Could not move this employee to that team."));
    } finally {
      setSavingAssignment(false);
    }
  }

  async function handleRemoveFromTeam(teamId: string) {
    setSavingAssignment(true);
    setTeamsError(null);
    try {
      const updated = await removeTeamMember(teamId, employee.id);
      setTeams((ts) => ts.map((t) => (t.id === updated.id ? updated : t)));
      setApproverOptionsByTeam((cur) => {
        const { [teamId]: _removed, ...rest } = cur;
        return rest;
      });
    } catch (e: unknown) {
      setTeamsError(message(e, "Could not remove this employee from the team."));
    } finally {
      setSavingAssignment(false);
    }
  }

  async function handleToggleApprover(teamId: string, layer: number, approverId: string, checked: boolean) {
    const current = approverOptionsByTeam[teamId]?.find((o) => o.layer === layer);
    if (!current) return;
    const nextIds = checked
      ? [...current.effectiveApproverIds, approverId]
      : current.effectiveApproverIds.filter((id) => id !== approverId);
    setSavingLayer({ teamId, layer });
    setTeamsError(null);
    try {
      const options = await setApproverOverride(teamId, employee.id, layer, nextIds);
      setApproverOptionsByTeam((cur) => ({ ...cur, [teamId]: options }));
    } catch (e: unknown) {
      setTeamsError(message(e, "Could not update the approver for this layer."));
    } finally {
      setSavingLayer(null);
    }
  }

  async function handleResetLayer(teamId: string, layer: number) {
    setSavingLayer({ teamId, layer });
    setTeamsError(null);
    try {
      const options = await clearApproverOverride(teamId, employee.id, layer);
      setApproverOptionsByTeam((cur) => ({ ...cur, [teamId]: options }));
    } catch (e: unknown) {
      setTeamsError(message(e, "Could not reset this layer."));
    } finally {
      setSavingLayer(null);
    }
  }

  // Cheap and honest: the payload is flat, so a stringify comparison catches
  // any edit without maintaining a per-field dirty map that would drift as
  // sections are added.
  const dirty =
    (profile !== null && baseline !== null && JSON.stringify(profile) !== JSON.stringify(baseline)) ||
    JSON.stringify(placement) !== JSON.stringify(placementBase);

  const activePolicies = useMemo(() => policies.filter((p) => !p.isArchived), [policies]);

  function set<K extends keyof EmployeeProfile>(key: K, value: EmployeeProfile[K]) {
    setProfile((current) => (current ? { ...current, [key]: value } : current));
  }

  // Both lists live on the profile as a JSON string. Editing them as arrays
  // and serializing on every change keeps a single source of truth — the dirty
  // check and the save payload stay exactly what they were.
  const children = parseChildRelief(profile?.childReliefJson);
  const allowances = parseFixedAllowances(profile?.fixedAllowancesJson);

  const setChildren = (rows: ChildRelief[]) => set("childReliefJson", serializeList(rows));
  const patchChild = (index: number, patch: Partial<ChildRelief>) =>
    setChildren(children.map((c, i) => (i === index ? { ...c, ...patch } : c)));

  const setAllowances = (rows: FixedAllowance[]) =>
    set("fixedAllowancesJson", serializeList(rows));
  const patchAllowance = (index: number, patch: Partial<FixedAllowance>) =>
    setAllowances(allowances.map((a, i) => (i === index ? { ...a, ...patch } : a)));

  function addAdjustment(kind: AdjustmentKind) {
    const category = DEFAULT_CATEGORY[kind];
    setAllowances([
      ...allowances,
      { category, name: labelForCategory(category), amount: null },
    ]);
  }

  function discard() {
    setProfile(baseline);
    setPlacement(placementBase);
    setError(null);
  }

  async function handleSave() {
    if (!profile) return;
    setSaving(true);
    setError(null);
    try {
      // Spread the current membership underneath: this PUT replaces the record,
      // so anything omitted (employee number, job title, shift, module grants)
      // would be written as null.
      const updated = await updateEmployee(employee.id, {
        ...toUpdateEmployee(employee),
        role: placement.role,
        policyId: placement.policyId === NONE ? null : placement.policyId,
        // The endpoint patches these: an empty string clears the field, which
        // is exactly what an admin emptying the box means. Sending null (or
        // omitting it) would silently keep the old value.
        name: placement.name.trim() || employee.name,
        email: placement.email.trim() || employee.email,
        employeeNumber: placement.employeeNumber.trim(),
        jobTitle: placement.jobTitle.trim(),
        joinDate: blank(placement.joinDate),
      });
      // The whole profile goes back: PUT replaces the record, so a partial
      // payload would null every field the other sections own. The employee
      // EPF rate is computed, not admin-entered — stamp the current branch's
      // rate in so what's saved always matches what the locked field shows.
      const savedProfile = await saveEmployeeProfile(employee.id, {
        ...profile,
        epfEmployeeRate: epfInfo.employeeRate,
      });

      setProfile(savedProfile);
      setBaseline(savedProfile);
      setPlacementBase(placement);
      onSaved(updated);

      // Archive status, join date, and other saved fields all drive which
      // LHDN forms are enabled and what badge they show — refetch so the
      // card reflects what was just saved instead of the pre-save state.
      getLhdnForms(employee.id)
        .then(setLhdnForms)
        .catch(() => {
          // Non-fatal: the profile save already succeeded. The card just
          // keeps showing its last-known state until the next reload.
        });
    } catch (e: unknown) {
      setError(message(e, "Could not save this employee."));
    } finally {
      setSaving(false);
    }
  }

  const name = placement.name.trim() || profile?.name?.trim() || employee.email;
  const ready = profile ? isReadyForPayroll(profile) : false;
  const sectionGaps = profile ? missingFields(profile, section) : [];

  // Statutory branch detection — drives the locked EPF rate display and the
  // SOCSO recommendation hint. Kept at this level (not just inside the tab's
  // JSX) so handleSave can stamp the computed EPF rate into the save payload.
  const isMalaysianCitizen = isMalaysianNationality(profile?.nationality);
  const isForeignWorker = profile ? !(isMalaysianCitizen || profile.hasPr) : false;
  const employeeAge = calculateAge(profile?.dateOfBirth);
  const epfBranch = pickEpfBranch({
    isMalaysianCitizen,
    hasPr: profile?.hasPr ?? false,
    epfMemberBefore1998: profile?.epfMemberBefore1998 ?? false,
    age: employeeAge,
  });
  const epfInfo = epfBranchInfo(
    epfBranch,
    profile?.salaryType === "MONTHLY" ? profile.monthlySalary : null,
  );
  const recommendedScheme = recommendSocsoScheme({
    dateOfBirth: profile?.dateOfBirth ?? null,
    isMalaysianCitizen,
  });
  const needsManualSocsoChoice = socsoSchemeNeedsManualChoice({
    dateOfBirth: profile?.dateOfBirth ?? null,
    isMalaysianCitizen,
  });

  return (
    <div className="space-y-4 pb-24">
      <button
        type="button"
        onClick={onBack}
        className="inline-flex items-center gap-1.5 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" />
        All employees
      </button>

      {/* Identity header. Kept separate from the form so it stays readable
          while scrolling a long section. */}
      <section className={`${CARD} p-5 sm:p-6`}>
        <div className="flex flex-wrap items-center gap-4">
          <div className="min-w-0 flex-1 basis-full sm:basis-0">
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="truncate text-lg font-black text-foreground sm:text-xl">{name}</h2>
              {placement.employeeNumber.trim() ? (
                <span className="text-sm font-semibold text-muted-foreground">
                  {placement.employeeNumber}
                </span>
              ) : null}
              <span className="rounded-full bg-muted px-2.5 py-1 text-[11px] font-bold text-muted-foreground">
                {placement.role}
              </span>
            </div>
            <p className="truncate text-sm text-muted-foreground">
              {[placement.jobTitle.trim(), employee.email].filter(Boolean).join(" · ")}
            </p>
          </div>

          {/* The verdict, in the same place the previous system puts it: one
              answer to "can I run payroll for this person yet?" */}
          {profile ? (
            profile.isArchived ? (
              <span className="inline-flex shrink-0 items-center gap-1.5 rounded-full border border-border bg-muted px-3 py-1.5 text-xs font-bold text-muted-foreground">
                Archived
              </span>
            ) : ready ? (
              <span className="inline-flex shrink-0 items-center gap-1.5 rounded-full border border-success/30 bg-success/10 px-3 py-1.5 text-xs font-bold text-success">
                <Check className="h-3.5 w-3.5" />
                Ready for payroll
              </span>
            ) : (
              <span className="inline-flex shrink-0 items-center gap-1.5 rounded-full bg-warning px-3 py-1.5 text-xs font-bold text-warning-foreground">
                <CircleAlert className="h-3.5 w-3.5" />
                Needs setup
              </span>
            )
          ) : null}
        </div>
      </section>

      {error ? (
        <p className="rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm font-medium text-destructive">
          {error}
        </p>
      ) : null}

      {loading ? (
        <section className={`${CARD} p-6 text-sm text-muted-foreground`}>Loading profile…</section>
      ) : !profile ? null : (
        <div className="space-y-4">
          {/* The count is what's still missing in that section; a finished one
              carries no badge at all. */}
          <OverflowTabList<SectionId>
            items={SECTIONS.map((sec) => ({
              id: sec.id,
              label: sec.label,
              badge: missingFields(profile, sec.id).length,
            }))}
            value={section}
            onChange={setSection}
            variant="underline"
            className="border-b border-border/50"
            ariaLabel="Profile sections"
          />

          <div className="space-y-4">
            {/* What this section still needs, spelled out — the pill's count
                tells you there's a gap, this tells you which field. */}
            {sectionGaps.length > 0 ? (
              <p className="flex items-start gap-2 rounded-2xl border border-warning bg-warning/40 px-4 py-3 text-sm font-medium text-warning-foreground">
                <CircleAlert className="mt-0.5 h-4 w-4 shrink-0" />
                <span>
                  Still needed here: <strong>{sectionGaps.join(", ")}</strong>
                </span>
              </p>
            ) : null}

            {section === "personal" ? (
              <>
                <Group title="Contact">
                  <Field label="Full name">
                    <Text
                      value={placement.name}
                      onChange={(v) => setPlacement((p) => ({ ...p, name: v ?? "" }))}
                    />
                  </Field>
                  <Field label="Email">
                    <Text
                      type="email"
                      value={placement.email}
                      onChange={(v) => setPlacement((p) => ({ ...p, email: v ?? "" }))}
                    />
                  </Field>
                  <Field label="Phone">
                    <Text type="tel" value={profile.phone} onChange={(v) => set("phone", v)} />
                  </Field>
                  <Field label="Alternate email">
                    <Text
                      type="email"
                      value={profile.alternateEmail}
                      onChange={(v) => set("alternateEmail", v)}
                    />
                  </Field>
                </Group>

                <Group title="Identity">
                  <Field label="Gender">
                    <Picker
                      value={profile.gender}
                      onChange={(v) => set("gender", v)}
                      allowNone
                      placeholder="Not set"
                      options={GENDERS.map((g) => ({
                        value: g,
                        label: g === "MALE" ? "Male" : "Female",
                      }))}
                    />
                  </Field>
                  <Field label="Date of birth">
                    <Text
                      type="date"
                      value={profile.dateOfBirth}
                      onChange={(v) => set("dateOfBirth", v)}
                    />
                  </Field>
                  <Field label="ID type">
                    <Picker
                      value={profile.idType}
                      onChange={(v) => set("idType", v)}
                      allowNone
                      placeholder="Not set"
                      options={ID_TYPES.map((t) => ({ value: t, label: ID_TYPE_LABELS[t] }))}
                    />
                  </Field>
                  <Field label="ID number">
                    <Text value={profile.idNumber} onChange={(v) => set("idNumber", v)} />
                  </Field>
                  <Field label="Nationality">
                    <Text value={profile.nationality} onChange={(v) => set("nationality", v)} />
                  </Field>
                  <Field label="Race">
                    <Text value={profile.race} onChange={(v) => set("race", v)} />
                  </Field>
                  <Field label="Marital status">
                    <Picker
                      value={profile.maritalStatus}
                      onChange={(v) => set("maritalStatus", v)}
                      allowNone
                      placeholder="Not set"
                      options={MARITAL_STATUSES.map((m) => ({
                        value: m,
                        label: m.charAt(0) + m.slice(1).toLowerCase(),
                      }))}
                    />
                  </Field>
                </Group>

                {/* Together because all three change statutory treatment, and
                    reading them side by side is how a wrong one gets caught. */}
                <Group title="Status" hint="These affect what payroll deducts." columns={3}>
                  <Toggle
                    label="Malaysian PR"
                    checked={profile.hasPr}
                    onChange={(v) => set("hasPr", v)}
                  />
                  <Toggle
                    label="Tax resident"
                    hint="Non-residents are taxed at a flat rate."
                    checked={profile.isResident}
                    onChange={(v) => set("isResident", v)}
                  />
                  <Toggle
                    label="OKU"
                    hint="Registered disability; carries extra relief."
                    checked={profile.isOku}
                    onChange={(v) => set("isOku", v)}
                  />
                </Group>

                <Group title="Address" columns={3}>
                  <Field label="Address line 1" span>
                    <Text value={profile.addressLine1} onChange={(v) => set("addressLine1", v)} />
                  </Field>
                  <Field label="Address line 2" span>
                    <Text value={profile.addressLine2} onChange={(v) => set("addressLine2", v)} />
                  </Field>
                  <Field label="City">
                    <Text value={profile.city} onChange={(v) => set("city", v)} />
                  </Field>
                  <Field label="Postcode">
                    <Text value={profile.postcode} onChange={(v) => set("postcode", v)} />
                  </Field>
                  <Field label="State">
                    <Text value={profile.state} onChange={(v) => set("state", v)} />
                  </Field>
                </Group>

                <Group title="Emergency contact" columns={3}>
                  <Field label="Name">
                    <Text
                      value={profile.emergencyContactName}
                      onChange={(v) => set("emergencyContactName", v)}
                    />
                  </Field>
                  <Field label="Phone">
                    <Text
                      type="tel"
                      value={profile.emergencyContactPhone}
                      onChange={(v) => set("emergencyContactPhone", v)}
                    />
                  </Field>
                  <Field label="Relationship">
                    <Text
                      value={profile.emergencyContactRelation}
                      onChange={(v) => set("emergencyContactRelation", v)}
                    />
                  </Field>
                </Group>

                {profile.maritalStatus === "MARRIED" ? (
                  <Group
                    title="Spouse"
                    hint="Used for the employee's own PCB relief, not the spouse's."
                  >
                    <TriToggle
                      label="Spouse working"
                      value={profile.spouseWorking}
                      onChange={(v) => set("spouseWorking", v)}
                    />
                    <TriToggle
                      label="Spouse disabled"
                      value={profile.spouseDisabled}
                      onChange={(v) => set("spouseDisabled", v)}
                    />
                    <Field label="Spouse ID number">
                      <Text
                        value={profile.spouseIdNumber}
                        onChange={(v) => set("spouseIdNumber", v)}
                      />
                    </Field>
                    <Field label="Spouse PCB number">
                      <Text
                        value={profile.spousePcbNumber}
                        onChange={(v) => set("spousePcbNumber", v)}
                      />
                    </Field>
                  </Group>
                ) : null}

                <Stack
                  title="Dependent children"
                  hint="Used for PCB child relief (QC). Up to 10 children."
                  action={
                    <button
                      type="button"
                      onClick={() =>
                        setChildren([
                          ...children,
                          {
                            abilityStatus: "NORMAL",
                            currentlyStudying: "UNDER_18",
                            pcbDeduction: "FULL",
                          },
                        ])
                      }
                      className="inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground transition hover:border-primary hover:text-primary"
                    >
                      <Plus className="h-3.5 w-3.5" />
                      Add child
                    </button>
                  }
                >
                  {children.length === 0 ? (
                    <p className="text-sm text-muted-foreground">
                      No children added. Add to claim child relief in PCB.
                    </p>
                  ) : (
                    children.map((child, index) => {
                      const isAdult = isAdultChild(child);
                      return (
                        <div
                          key={index}
                          className="rounded-2xl border border-border/60 bg-card p-4 shadow-sm"
                        >
                          <div className="mb-3 flex items-center justify-between">
                            <p className="text-sm font-semibold text-foreground">
                              Child {index + 1}
                            </p>
                            <button
                              type="button"
                              onClick={() => setChildren(children.filter((_, i) => i !== index))}
                              className="inline-flex h-7 items-center gap-1 text-xs font-bold text-destructive transition hover:underline"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                              Remove
                            </button>
                          </div>
                          <div className="grid items-end gap-3 sm:grid-cols-2 lg:grid-cols-4">
                            <Field label="Age bracket">
                              <Picker
                                value={isAdult ? "ADULT" : "UNDER_18"}
                                onChange={(bracket) =>
                                  patchChild(index, {
                                    // Default the 18+ pick to Pre-University; the
                                    // admin can switch to Diploma / Degree Abroad
                                    // in the field beside it.
                                    currentlyStudying:
                                      bracket === "UNDER_18" ? "UNDER_18" : "PRE_UNIVERSITY",
                                  })
                                }
                                options={[
                                  { value: "UNDER_18", label: "Under 18" },
                                  { value: "ADULT", label: "18 and above" },
                                ]}
                              />
                            </Field>
                            <Field label="Education level">
                              <Picker
                                value={child.currentlyStudying}
                                disabled={!isAdult}
                                onChange={(v) =>
                                  patchChild(index, { currentlyStudying: v ?? "UNDER_18" })
                                }
                                options={CHILD_STUDYING.map((c) => ({
                                  value: c,
                                  label: CHILD_STUDYING_LABELS[c],
                                  disabled: c === "UNDER_18" ? isAdult : !isAdult,
                                }))}
                              />
                            </Field>
                            <Field label="Ability">
                              <Picker
                                value={child.abilityStatus}
                                onChange={(v) =>
                                  patchChild(index, { abilityStatus: v ?? "NORMAL" })
                                }
                                options={CHILD_ABILITY.map((a) => ({
                                  value: a,
                                  label: CHILD_ABILITY_LABELS[a],
                                }))}
                              />
                            </Field>
                            <Field label="PCB share">
                              <Picker
                                value={child.pcbDeduction}
                                onChange={(v) => patchChild(index, { pcbDeduction: v ?? "FULL" })}
                                options={CHILD_DEDUCTION.map((d) => ({
                                  value: d,
                                  label: CHILD_DEDUCTION_LABELS[d],
                                }))}
                              />
                            </Field>
                          </div>
                        </div>
                      );
                    })
                  )}
                </Stack>
              </>
            ) : section === "employment" ? (
              <>
                <Group title="Compensation" hint="The salary structure payroll calculates from.">
                  <Field label="Basis">
                    <Picker
                      value={profile.salaryType}
                      onChange={(v) => set("salaryType", v ?? "MONTHLY")}
                      options={SALARY_TYPES.map((t) => ({
                        value: t,
                        label: t === "MONTHLY" ? "Monthly" : "Hourly",
                      }))}
                    />
                  </Field>
                  {/* Only the field that matches the basis, so there's never a
                      monthly salary AND an hourly rate on record disagreeing. */}
                  {profile.salaryType === "MONTHLY" ? (
                    <Field label="Monthly salary">
                      <Money
                        value={profile.monthlySalary}
                        onChange={(v) => set("monthlySalary", v)}
                      />
                    </Field>
                  ) : (
                    <Field label="Hourly rate">
                      <Money value={profile.hourlyRate} onChange={(v) => set("hourlyRate", v)} />
                    </Field>
                  )}
                </Group>

                <Stack
                  title="Fixed adjustments"
                  hint="Recurring monthly additions or deductions added to every payroll run. One-off amounts belong on the run itself."
                  action={
                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={() => addAdjustment("ALLOWANCE")}
                        className="inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground transition hover:border-primary hover:text-primary"
                      >
                        <Plus className="h-3.5 w-3.5" />
                        Add allowance
                      </button>
                      <button
                        type="button"
                        onClick={() => addAdjustment("DEDUCTION")}
                        className="inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground transition hover:border-primary hover:text-primary"
                      >
                        <Plus className="h-3.5 w-3.5" />
                        Add deduction
                      </button>
                    </div>
                  }
                >
                  {allowances.length === 0 ? (
                    <p className="text-sm text-muted-foreground">
                      No fixed adjustments. Add one for recurring monthly allowances, benefits, or
                      deductions.
                    </p>
                  ) : (
                    allowances.map((row, index) => {
                      const kind = kindOf(row.category);
                      return (
                        <RepeaterRow
                          key={index}
                          removeLabel="Remove this adjustment"
                          onRemove={() => setAllowances(allowances.filter((_, i) => i !== index))}
                        >
                          <Field label={kind === "DEDUCTION" ? "Deduction" : "Allowance"} span>
                            <Picker
                              value={row.category}
                              onChange={(v) =>
                                patchAllowance(index, {
                                  // Follow the category with the label, unless the
                                  // admin has renamed the row themselves.
                                  category: v ?? DEFAULT_CATEGORY[kind],
                                  name:
                                    row.name === labelForCategory(row.category) || !row.name
                                      ? labelForCategory(v ?? DEFAULT_CATEGORY[kind])
                                      : row.name,
                                })
                              }
                              options={categoriesFor(kind).map((c) => ({
                                value: c.code,
                                label: c.label,
                              }))}
                            />
                          </Field>
                          <Field label="Shown on the payslip as">
                            <Text
                              value={row.name}
                              onChange={(v) => patchAllowance(index, { name: v ?? "" })}
                            />
                          </Field>
                          <Field
                            label="Amount"
                            hint={
                              kind === "DEDUCTION" ? "Taken off every run." : "Added to every run."
                            }
                          >
                            <Money
                              value={row.amount}
                              onChange={(v) => patchAllowance(index, { amount: v })}
                            />
                          </Field>
                        </RepeaterRow>
                      );
                    })
                  )}
                </Stack>

                <Group
                  title="Employment dates"
                  hint="The join date pro-rates a partial first month. An end date belongs to Archive, below."
                  columns={1}
                >
                  <Field label="Join date">
                    <Text
                      type="date"
                      value={placement.joinDate}
                      onChange={(v) => {
                        // Written to the membership (which pro-rates accrual) AND
                        // to the profile copy, so the two can't disagree.
                        setPlacement((p) => ({ ...p, joinDate: v ?? "" }));
                        set("joinDate", v);
                      }}
                    />
                  </Field>
                </Group>

                <Group
                  title="Previous employment (TP3)"
                  hint="For a mid-year joiner: what a previous employer already paid and deducted, so PCB isn't calculated as if this is their only income."
                >
                  <Field label="Year">
                    <Text
                      value={profile.prevEmploymentYear === null ? null : String(profile.prevEmploymentYear)}
                      onChange={(v) => set("prevEmploymentYear", v === null ? null : Number(v))}
                      placeholder={String(new Date().getFullYear())}
                    />
                  </Field>
                  <Field label="Remuneration">
                    <Money
                      value={profile.prevRemuneration}
                      onChange={(v) => set("prevRemuneration", v)}
                    />
                  </Field>
                  <Field label="EPF paid">
                    <Money value={profile.prevEpf} onChange={(v) => set("prevEpf", v)} />
                  </Field>
                  <Field label="PCB paid">
                    <Money value={profile.prevPcb} onChange={(v) => set("prevPcb", v)} />
                  </Field>
                  <Field label="Allowable deductions">
                    <Money
                      value={profile.prevAllowableDeductions}
                      onChange={(v) => set("prevAllowableDeductions", v)}
                    />
                  </Field>
                  <Field label="Zakat paid">
                    <Money value={profile.prevZakat} onChange={(v) => set("prevZakat", v)} />
                  </Field>
                  <Field label="Includes an earlier spell here" span>
                    <Toggle
                      label="Covers a prior period at this company"
                      hint="Tick when the figures above include time they worked here before."
                      checked={profile.prevIncludesPriorThisOrgPeriod}
                      onChange={(v) => set("prevIncludesPriorThisOrgPeriod", v)}
                    />
                  </Field>
                </Group>

                <Group
                  title="Archive"
                  hint="Ending someone's employment happens here — one place, so a leave date and an archive flag can't disagree."
                >
                  <Field label="Last day" hint="Their final day of work. Pro-rates the last payroll run.">
                    <Text
                      type="date"
                      value={profile.leaveDate}
                      onChange={(v) => set("leaveDate", v)}
                    />
                  </Field>
                  <Field label="Archived" span>
                    <Toggle
                      label="Archive this employee"
                      hint="Keeps their history and payslips, but leaves them out of new payroll runs."
                      checked={profile.isArchived}
                      onChange={(v) => set("isArchived", v)}
                    />
                  </Field>
                  {profile.isArchived ? (
                    <Field label="Reason" hint="Why they were archived — resignation, end of contract." span>
                      <Text
                        value={profile.archiveReason}
                        onChange={(v) => set("archiveReason", v)}
                      />
                    </Field>
                  ) : null}
                </Group>
              </>
            ) : section === "statutory" ? (
              <>
                {isForeignWorker ? (
                  <p className="flex items-start gap-2 rounded-2xl border border-warning bg-warning/40 px-4 py-3 text-sm font-medium text-warning-foreground">
                    <CircleAlert className="mt-0.5 h-4 w-4 shrink-0" />
                    <span>
                      <strong>Foreign worker statutory profile.</strong> Detected
                      because nationality is not Malaysian and PR is not set.{" "}
                      {profile.epfMemberBefore1998
                        ? "Pre-1998 EPF member: standard EPF rates apply (Part A / C)."
                        : "EPF runs on the post-1998 non-Malaysian branch (2% / 2%, effective Oct 2025 salary)."}{" "}
                      EIS still applies (Act 800 covers foreign workers on valid
                      permits, age 18–60). SOCSO scheme should typically be
                      "Employment injury only".
                    </span>
                  </p>
                ) : null}

                <Group
                  title="EPF"
                  hint="Employees Provident Fund. Statutory rates below come from EPF Act 452 (Third Schedule) — shown locked for reference. Voluntary contributions on top are editable."
                >
                  <Field label="EPF number" span>
                    <Text value={profile.epfNumber} onChange={(v) => set("epfNumber", v)} />
                  </Field>
                  <Field label="Employer mandatory rate" locked>
                    <LockedValue value={epfInfo.employerText} />
                    <span className="text-xs text-muted-foreground">
                      KWSP branch: {epfInfo.branchLabel}.
                    </span>
                  </Field>
                  <Field label="Employee mandatory rate" locked>
                    <LockedValue value={formatEpfRate(epfInfo.employeeRate)} />
                    <span className="text-xs text-muted-foreground">{epfInfo.employeeNote}</span>
                  </Field>
                  <Field label="Employee voluntary" hint="On top of the statutory rate.">
                    <Percent
                      value={profile.epfEmployeeVoluntary}
                      onChange={(v) => set("epfEmployeeVoluntary", v)}
                    />
                  </Field>
                  <Field label="Employer voluntary">
                    <Percent
                      value={profile.epfEmployerVoluntary}
                      onChange={(v) => set("epfEmployerVoluntary", v)}
                    />
                  </Field>
                  <Toggle
                    label="Contributes to EPF"
                    hint="Off stops both employee and employer contributions."
                    checked={profile.contributeToEpf}
                    onChange={(v) => set("contributeToEpf", v)}
                  />
                  <Toggle
                    label="EPF member before Aug 1998?"
                    hint="Only meaningful for a non-Malaysian, non-PR employee — keeps them on the standard rates instead of the post-1998 branch."
                    checked={profile.epfMemberBefore1998}
                    onChange={(v) => set("epfMemberBefore1998", v)}
                  />
                </Group>

                <Group title="SOCSO, EIS & SKBBK">
                  <Field label="SOCSO number">
                    <Text value={profile.socsoNumber} onChange={(v) => set("socsoNumber", v)} />
                  </Field>
                  <Field label="Scheme">
                    <Picker
                      value={profile.socsoScheme}
                      onChange={(v) => set("socsoScheme", v)}
                      allowNone
                      placeholder="Not set"
                      options={SOCSO_SCHEMES.map((s) => ({
                        value: s,
                        label: SOCSO_SCHEME_LABELS[s],
                      }))}
                    />
                    {recommendedScheme &&
                    profile.socsoScheme === recommendedScheme &&
                    !originalSocsoScheme ? (
                      <span className="text-xs text-muted-foreground">
                        Auto-selected from age{employeeAge ? ` (${employeeAge})` : ""}.
                        Override above if needed.
                      </span>
                    ) : null}
                    {needsManualSocsoChoice && profile.socsoScheme === null ? (
                      <span className="text-xs font-medium text-warning-foreground">
                        Age {employeeAge} — please pick manually: Scheme 1 if the
                        employee has an existing SOCSO number from a previous job,
                        Scheme 2 if this is their first-time PERKESO registration.
                      </span>
                    ) : null}
                    {recommendedScheme &&
                    profile.socsoScheme !== null &&
                    profile.socsoScheme !== recommendedScheme ? (
                      <span className="text-xs font-medium text-warning-foreground">
                        PERKESO would normally recommend{" "}
                        {SOCSO_SCHEME_LABELS[recommendedScheme]} for this employee
                        (age {employeeAge}). Confirm before saving.
                      </span>
                    ) : null}
                  </Field>
                  <Toggle
                    label="Contributes to EIS"
                    checked={profile.contributeToEis}
                    onChange={(v) => set("contributeToEis", v)}
                  />
                  <Toggle
                    label="Contributes to SKBBK"
                    checked={profile.contributeToSkbbk}
                    onChange={(v) => set("contributeToSkbbk", v)}
                  />
                </Group>

                <Group title="Income tax">
                  <Field label="Income tax number">
                    <Text
                      value={profile.incomeTaxNumber}
                      onChange={(v) => set("incomeTaxNumber", v)}
                    />
                  </Field>
                  <Field label="SSFW number" hint="Foreign-worker social security, where it applies.">
                    <Text value={profile.ssfwNumber} onChange={(v) => set("ssfwNumber", v)} />
                  </Field>
                  <Toggle
                    label="PCB borne by employer"
                    hint="The company pays the tax instead of deducting it."
                    checked={profile.pcbBorneByEmployer}
                    onChange={(v) => set("pcbBorneByEmployer", v)}
                  />
                  <Toggle
                    label="Reported to LHDN"
                    checked={profile.reportedToLhdn}
                    onChange={(v) => set("reportedToLhdn", v)}
                  />
                </Group>

                <Group title="Bank / payout" hint="Where the money goes once payroll has run.">
                  <Field label="Method" span>
                    <Picker
                      value={profile.paymentMethod}
                      onChange={(v) => set("paymentMethod", v ?? "BANK_TRANSFER")}
                      options={PAYMENT_METHODS.map((m) => ({
                        value: m,
                        label: PAYMENT_METHOD_LABELS[m],
                      }))}
                    />
                  </Field>
                  {/* Bank details are only meaningful for a transfer. */}
                  {profile.paymentMethod === "BANK_TRANSFER" ? (
                    <>
                      <Field label="Bank">
                        <Text value={profile.bankName} onChange={(v) => set("bankName", v)} />
                      </Field>
                      <Field label="Account number">
                        <Text
                          value={profile.bankAccountNumber}
                          onChange={(v) => set("bankAccountNumber", v)}
                        />
                      </Field>
                      <Field
                        label="Account holder"
                        hint="As printed by the bank — a mismatch bounces the transfer."
                        span
                      >
                        <Text
                          value={profile.bankAccountHolderName}
                          onChange={(v) => set("bankAccountHolderName", v)}
                        />
                      </Field>
                    </>
                  ) : null}
                </Group>
              </>
            ) : section === "company" ? (
              <>
                <Group title="Identity at work" columns={3}>
                  <Field label="Employee number">
                    <Text
                      value={placement.employeeNumber}
                      onChange={(v) => setPlacement((p) => ({ ...p, employeeNumber: v ?? "" }))}
                      placeholder="EMP-001"
                    />
                  </Field>
                  <Field label="Job title">
                    <Text
                      value={placement.jobTitle}
                      onChange={(v) => setPlacement((p) => ({ ...p, jobTitle: v ?? "" }))}
                    />
                  </Field>
                  <Field label="Role">
                    <Picker
                      value={placement.role}
                      onChange={(v) => setPlacement((p) => ({ ...p, role: v ?? p.role }))}
                      // Employee ⇄ Supervisor only. Granting administrative
                      // access is a different decision on a different screen,
                      // and doing it here would quietly put an admin into an
                      // approval chain they're meant to sit outside of.
                      options={STAFF_ROLES.map((r) => ({ value: r, label: r }))}
                    />
                  </Field>
                  <Field label="Policy">
                    <Picker
                      value={placement.policyId}
                      onChange={(v) => setPlacement((p) => ({ ...p, policyId: v ?? NONE }))}
                      placeholder="Default"
                      options={[
                        { value: NONE, label: "Default policy" },
                        ...activePolicies.map((p) => ({ value: p.id, label: p.name })),
                      ]}
                    />
                  </Field>
                  <Field label="Department">
                    <Text value={profile.department} onChange={(v) => set("department", v)} />
                  </Field>
                </Group>

                <Stack
                  title="Approval routing"
                  hint="Project, then team, then which level they sit at — their approvers follow from that."
                >
                  {teamsError ? (
                    <p className="text-sm font-medium text-destructive">{teamsError}</p>
                  ) : null}
                  {teamsLoading ? (
                    <p className="text-sm text-muted-foreground">Loading teams…</p>
                  ) : myProjects.length === 0 ? (
                    // Not on any team means no chain at all, and a request with
                    // no chain approves itself on submission. That is worth
                    // saying out loud rather than showing an empty box.
                    <p className="flex items-start gap-2 rounded-2xl border border-warning bg-warning/40 px-4 py-3 text-sm font-medium text-warning-foreground">
                      <CircleAlert className="mt-0.5 h-4 w-4 shrink-0" />
                      <span>
                        Not on any project yet, so nobody approves for them —{" "}
                        <strong>their claims, leave and overtime approve themselves on submission.</strong>{" "}
                        Add a project below.
                      </span>
                    </p>
                  ) : (
                    myProjects.map(({ project, team, member }) => {
                      const options = approverOptionsByTeam[team.id] ?? [];
                      const projectTeams = teams.filter((t) => t.projectId === project.id);
                      return (
                        <div
                          key={project.id}
                          className="space-y-3 rounded-2xl border border-border/60 bg-card p-4"
                        >
                          <div className="flex flex-wrap items-start justify-between gap-3">
                            <div>
                              <p className="text-sm font-black text-foreground">{project.name}</p>
                              <p className="text-xs text-muted-foreground">
                                Team and approvers for this project.
                              </p>
                            </div>
                            <button
                              type="button"
                              disabled={savingAssignment}
                              onClick={() => void handleRemoveFromTeam(team.id)}
                              className="shrink-0 text-xs font-bold text-destructive transition hover:underline disabled:opacity-50"
                            >
                              Remove from this project
                            </button>
                          </div>

                          <div className="grid gap-4 sm:grid-cols-2">
                            <Field label="Team">
                              <Picker
                                value={team.id}
                                onChange={(v) => {
                                  // Moving team within the same project: leave
                                  // the old one and join the new at the same
                                  // level, so the level survives the move.
                                  if (v && v !== team.id) void handleMoveTeam(team.id, v, member.layer);
                                }}
                                options={projectTeams.map((t) => ({
                                  value: t.id,
                                  label: `${t.name} · ${t.layerCount} level${t.layerCount === 1 ? "" : "s"}`,
                                }))}
                              />
                            </Field>
                            <Field label="Their level">
                              <Picker
                                value={String(member.layer)}
                                onChange={(v) =>
                                  void handleChangeLayer(team.id, v ? Number(v) : member.layer)
                                }
                                options={Array.from({ length: team.layerCount }, (_, layer) => ({
                                  value: String(layer),
                                  label: `L${layer + 1} — ${layerLabel(team, layer)}`,
                                }))}
                              />
                            </Field>
                          </div>

                          {approverOptionsLoading ? (
                            <p className="text-sm text-muted-foreground">Loading approvers…</p>
                          ) : options.length === 0 ? (
                            <p className="text-sm text-muted-foreground">
                              Top level of {team.name} — nobody approves above them on this project.
                            </p>
                          ) : (
                            <div className="space-y-3">
                              <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                                Their supervisors
                              </p>
                              {options.map((option) => (
                                <div
                                  key={option.layer}
                                  className="rounded-2xl border border-border/60 bg-surface-low/40 p-4"
                                >
                                  <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                                    <p className="text-sm font-semibold text-foreground">
                                      L{option.layer + 1} — {option.layerLabel}
                                    </p>
                                    <div className="flex items-center gap-2">
                                      <span
                                        className={`rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${
                                          option.isOverridden
                                            ? "bg-warning text-warning-foreground"
                                            : "bg-muted text-muted-foreground"
                                        }`}
                                      >
                                        {option.isOverridden ? "Chosen" : "Everyone at this level"}
                                      </span>
                                      {option.isOverridden ? (
                                        <button
                                          type="button"
                                          disabled={
                                            savingLayer?.teamId === team.id &&
                                            savingLayer.layer === option.layer
                                          }
                                          onClick={() => void handleResetLayer(team.id, option.layer)}
                                          className="text-xs font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
                                        >
                                          Reset
                                        </button>
                                      ) : null}
                                    </div>
                                  </div>
                                  {option.candidates.length === 0 ? (
                                    <p className="text-xs text-muted-foreground">
                                      Nobody sits at this level yet — assign someone in Company Structure
                                      first.
                                    </p>
                                  ) : (
                                    <div className="grid gap-2 sm:grid-cols-2">
                                      {option.candidates.map((candidate) => (
                                        <label
                                          key={candidate.employeeId}
                                          className="flex cursor-pointer items-center gap-2 text-sm text-foreground"
                                        >
                                          <input
                                            type="checkbox"
                                            checked={option.effectiveApproverIds.includes(
                                              candidate.employeeId,
                                            )}
                                            disabled={
                                              savingLayer?.teamId === team.id &&
                                              savingLayer.layer === option.layer
                                            }
                                            onChange={(e) =>
                                              void handleToggleApprover(
                                                team.id,
                                                option.layer,
                                                candidate.employeeId,
                                                e.target.checked,
                                              )
                                            }
                                            className="h-4 w-4 rounded border-border accent-primary"
                                          />
                                          {candidate.email ?? candidate.employeeId}
                                        </label>
                                      ))}
                                    </div>
                                  )}
                                </div>
                              ))}
                            </div>
                          )}
                        </div>
                      );
                    })
                  )}

                  {/* Adding follows the same order as reading: project, then a
                      team inside it, then the level. Always available — an
                      employee can work on several projects at once. */}
                  <div className="rounded-2xl border border-dashed border-border/70 bg-surface-low/40 p-4">
                    <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                      Add a project
                    </p>
                    {/* An empty project picker has two different causes and an
                        admin can act on both — but only if we say which. */}
                    {assignableProjects.length === 0 ? (
                      <p className="mt-2 text-sm text-muted-foreground">
                        {projectsWithoutTeams.length > 0
                          ? `No team exists yet on ${projectsWithoutTeams
                              .map((p) => p.name)
                              .join(", ")} — create one in Company Structure first.`
                          : "They are already on every project that has a team."}
                      </p>
                    ) : null}
                    {/* One step at a time, in order. Each answer is what makes
                        the next question askable: the project decides which
                        teams exist, the team decides how many levels there are.
                        Showing all three at once invites picking a level before
                        anything knows how many there are. */}
                    <div className="mt-3 space-y-3">
                      <Field label="1. Project">
                        <Picker
                          value={assigningProjectId}
                          onChange={(v) => {
                            setAssigningProjectId(v ?? NONE);
                            setAssigningTeamId(NONE);
                            setAssigningLayer(0);
                          }}
                          placeholder="Choose a project"
                          allowNone
                          noneLabel="Choose a project"
                          options={assignableProjects.map((p) => ({ value: p.id, label: p.name }))}
                        />
                      </Field>

                      {assigningProjectId === NONE ? null : (
                        <Field
                          label="2. Team"
                          hint={
                            assigningProjectTeams.length === 0
                              ? "This project has no team yet — create one in Company Structure."
                              : undefined
                          }
                        >
                          <Picker
                            value={assigningTeamId}
                            onChange={(v) => {
                              setAssigningTeamId(v ?? NONE);
                              setAssigningLayer(0);
                            }}
                            placeholder="Choose a team"
                            allowNone
                            noneLabel="Choose a team"
                            options={assigningProjectTeams.map((t) => ({
                              value: t.id,
                              label: t.name,
                            }))}
                          />
                        </Field>
                      )}

                      {!assigningTeam ? null : (
                        <Field
                          label="3. Their level"
                          hint={`${assigningTeam.name} has ${assigningTeam.layerCount} level${
                            assigningTeam.layerCount === 1 ? "" : "s"
                          }. Everyone above the level you pick approves for them.`}
                        >
                          <Picker
                            value={String(assigningLayer)}
                            onChange={(v) => setAssigningLayer(v ? Number(v) : 0)}
                            options={Array.from(
                              { length: assigningTeam.layerCount },
                              (_, layer) => ({
                                value: String(layer),
                                label: `L${layer + 1} — ${layerLabel(assigningTeam, layer)}`,
                              }),
                            )}
                          />
                        </Field>
                      )}

                      {/* Who that choice lands them under, before they commit to
                          it — the supervisors are editable per layer once the
                          person is on the team, but an admin should be able to
                          see the consequence first. */}
                      {!assigningTeam ? null : assigningPreviewApprovers.length === 0 ? (
                        <p className="text-sm text-muted-foreground">
                          Top level — nobody would approve above them on this project.
                        </p>
                      ) : (
                        <div className="rounded-2xl border border-border/60 bg-card p-4">
                          <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                            4. Their supervisors would be
                          </p>
                          <div className="mt-2 space-y-1.5">
                            {assigningPreviewApprovers.map(({ layer, label, names }) => (
                              <p key={layer} className="text-sm text-foreground">
                                <span className="font-semibold">
                                  L{layer + 1} — {label}:
                                </span>{" "}
                                {names.length > 0 ? (
                                  names.join(", ")
                                ) : (
                                  <span className="text-muted-foreground">
                                    nobody at this level yet
                                  </span>
                                )}
                              </p>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>
                    <button
                      type="button"
                      disabled={assigningTeamId === NONE || savingAssignment}
                      onClick={() => void handleAssignTeam()}
                      className="mt-3 inline-flex h-11 items-center gap-2 rounded-2xl border border-border bg-card px-4 text-sm font-bold text-foreground transition hover:border-primary hover:text-primary disabled:pointer-events-none disabled:opacity-50"
                    >
                      {savingAssignment ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                      Assign
                    </button>
                  </div>
                </Stack>
              </>
            ) : (
              <>
                <div className={`${CARD} p-4 sm:p-5`}>
                  <h3 className="text-sm font-black text-foreground">LHDN Forms</h3>
                  <p className="mt-0.5 text-xs text-muted-foreground">
                    Per-employee statutory PDFs. Each summarises the LHDN-required fields in an AltomateHR
                    layout — transcribe onto the official LHDN form before submission, or paste values into
                    e-PCB.
                  </p>

                  <div className="mt-4 max-w-[140px]">
                    <Field label="Year" hint="Only used by year-scoped forms, like PCB 2(II).">
                      <Num
                        value={lhdnYear}
                        min={2000}
                        max={2100}
                        onChange={(v) => setLhdnYear(v ?? new Date().getFullYear())}
                      />
                    </Field>
                  </div>

                  {lhdnFormsError ? (
                    <p className="mt-4 text-sm font-medium text-destructive">{lhdnFormsError}</p>
                  ) : null}

                  <div className="mt-4 grid gap-3 sm:grid-cols-2">
                    {lhdnFormsLoading ? (
                      <p className="text-sm text-muted-foreground sm:col-span-2">Loading forms…</p>
                    ) : (
                      lhdnForms.map((form) => (
                        <div
                          key={form.kind}
                          className={`rounded-2xl border border-border/60 bg-card p-4 ${
                            form.enabled ? "" : "opacity-60"
                          }`}
                        >
                          <div className="flex items-start justify-between gap-2">
                            <div className="flex flex-wrap items-center gap-1.5">
                              <span className="text-sm font-black text-foreground">{form.code}</span>
                              <span className="rounded-full bg-muted px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-muted-foreground">
                                {form.title}
                              </span>
                              {form.badge ? (
                                <span
                                  className={`rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${
                                    form.badgeVariant === "success"
                                      ? "bg-success/10 text-success"
                                      : form.badgeVariant === "rejected"
                                        ? "bg-destructive/10 text-destructive"
                                        : form.badgeVariant === "pending"
                                          ? "bg-warning/40 text-warning-foreground"
                                          : "bg-muted text-muted-foreground"
                                  }`}
                                >
                                  {form.badge}
                                </span>
                              ) : null}
                            </div>
                            {form.enabled ? (
                              <button
                                type="button"
                                onClick={() => void handleDownloadLhdnForm(form)}
                                disabled={downloadingLhdnKind === form.kind}
                                className="inline-flex h-8 shrink-0 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground transition hover:border-primary hover:text-primary disabled:pointer-events-none disabled:opacity-50"
                              >
                                {downloadingLhdnKind === form.kind ? (
                                  <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
                                ) : null}
                                Download PDF
                              </button>
                            ) : (
                              <span className="h-8 shrink-0 rounded-xl border border-border/60 px-3 text-xs font-bold leading-8 text-muted-foreground">
                                Download PDF
                              </span>
                            )}
                          </div>
                          <p className="mt-2 text-xs text-muted-foreground">{form.description}</p>
                          {form.disabledReason ? (
                            <p className="mt-2 text-xs font-semibold text-warning-foreground">
                              {form.disabledReason}
                            </p>
                          ) : null}
                        </div>
                      ))
                    )}
                  </div>
                </div>

                <Stack
                title="Documents"
                hint="ID scans, contracts, certificates — up to 10 MB each (PDF, Word, or image)."
                action={
                  <label className="inline-flex h-9 cursor-pointer items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground transition hover:border-primary hover:text-primary has-[:disabled]:pointer-events-none has-[:disabled]:opacity-50">
                    {uploadingDocument ? (
                      <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
                    ) : (
                      <Plus className="h-3.5 w-3.5" />
                    )}
                    Upload
                    <input
                      type="file"
                      className="hidden"
                      disabled={uploadingDocument}
                      onChange={(e) => {
                        const file = e.target.files?.[0];
                        e.target.value = "";
                        if (file) void handleUploadDocument(file);
                      }}
                    />
                  </label>
                }
              >
                {documentsError ? (
                  <p className="text-sm font-medium text-destructive">{documentsError}</p>
                ) : null}
                {documentsLoading ? (
                  <p className="text-sm text-muted-foreground">Loading documents…</p>
                ) : documents.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No documents uploaded yet.</p>
                ) : (
                  documents.map((doc) => (
                    <div
                      key={doc.id}
                      className="flex items-center justify-between gap-3 rounded-2xl border border-border/60 bg-card px-4 py-3"
                    >
                      <button
                        type="button"
                        onClick={() => void handleDownloadDocument(doc)}
                        className="min-w-0 flex-1 text-left"
                      >
                        <span className="block truncate text-sm font-semibold text-foreground hover:underline">
                          {doc.name}
                        </span>
                        <span className="text-xs text-muted-foreground">
                          {formatFileSize(doc.sizeBytes)} · uploaded{" "}
                          {new Date(doc.uploadedAt).toLocaleDateString()}
                        </span>
                      </button>
                      <button
                        type="button"
                        onClick={() => void handleDeleteDocument(doc.id)}
                        className="shrink-0 text-xs font-bold text-destructive transition hover:underline"
                      >
                        Remove
                      </button>
                    </div>
                  ))
                )}
              </Stack>
              </>
            )}
          </div>
        </div>
      )}

      {/* Appears only once something has changed — so "did that save?" is never
          a question, and an untouched record shows no call to action. */}
      {dirty ? (
        <div className="fixed inset-x-0 bottom-0 z-40 border-t border-border/70 bg-card/95 px-4 py-3 backdrop-blur-xl">
          <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-3">
            <p className="text-sm font-semibold text-foreground">Unsaved changes</p>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={discard}
                disabled={saving}
                className="inline-flex h-11 items-center gap-1.5 rounded-full border border-border bg-card px-4 text-sm font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
              >
                <RotateCcw className="h-3.5 w-3.5" />
                Discard
              </button>
              <button
                type="button"
                onClick={() => void handleSave()}
                disabled={saving}
                className="inline-flex h-11 items-center gap-2 rounded-full bg-primary px-5 text-sm font-bold text-primary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-60"
              >
                {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                Save changes
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
