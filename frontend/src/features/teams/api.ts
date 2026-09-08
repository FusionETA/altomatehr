import { apiDelete, apiGet, apiPost, apiPut } from "@/shared/lib/api-client";

export type TeamMember = {
  employeeId: string;
  email: string | null;
  layer: number;
};

// Module name → layer indices that must approve. Absent module = all layers.
export type ModuleApprovalConfig = Record<string, number[]>;

// Modules whose approvals route through the chain. OT/ATTENDANCE are stored for
// forward-compat but not enforced until those modules land.
export const APPROVAL_MODULES = ["CLAIMS", "OT", "LEAVE", "ATTENDANCE"] as const;

export type Team = {
  id: string;
  projectId: string;
  name: string;
  layerCount: number;
  layerLabels: string[];
  moduleApprovalConfig: ModuleApprovalConfig;
  members: TeamMember[];
};

export type CreateTeam = {
  projectId: string;
  name: string;
  layerCount: number;
  layerLabels: string[];
  moduleApprovalConfig: ModuleApprovalConfig;
};

export type SaveTeam = {
  name: string;
  layerCount: number;
  layerLabels: string[];
  moduleApprovalConfig: ModuleApprovalConfig;
};

export type SaveMembership = { employeeId: string; layer: number };

// Label for a layer index, falling back to "Layer N" (bottom = 0).
export function layerLabel(team: Pick<Team, "layerLabels">, layer: number) {
  return team.layerLabels[layer]?.trim() || `Layer ${layer + 1}`;
}

export const getTeams = () => apiGet<Team[]>("/teams");
export const createTeam = (body: CreateTeam) => apiPost<Team>("/teams", body);
export const updateTeam = (id: string, body: SaveTeam) => apiPut<Team>(`/teams/${id}`, body);
export const deleteTeam = (id: string) => apiDelete<void>(`/teams/${id}`);
export const addTeamMember = (teamId: string, body: SaveMembership) =>
  apiPost<Team>(`/teams/${teamId}/members`, body);
export const removeTeamMember = (teamId: string, employeeId: string) =>
  apiDelete<Team>(`/teams/${teamId}/members/${employeeId}`);

// ---- Explicit per-employee approver overrides ----
//
// A layer's approvers default to "everyone else at that layer" (implicit).
// An admin can override that for one specific employee at one specific
// layer — this is what these endpoints manage. `effectiveApproverIds` is
// always populated (the override if one exists, else the same set as
// `candidates`), so the UI can show "what's actually happening" without the
// caller working out the default itself.

export type ApproverCandidate = { employeeId: string; email: string | null };

export type LayerApproverOptions = {
  layer: number;
  layerLabel: string;
  candidates: ApproverCandidate[];
  effectiveApproverIds: string[];
  isOverridden: boolean;
};

export const getApproverOptions = (teamId: string, employeeId: string) =>
  apiGet<LayerApproverOptions[]>(`/teams/${teamId}/members/${employeeId}/approver-options`);

export const setApproverOverride = (teamId: string, employeeId: string, layer: number, approverIds: string[]) =>
  apiPut<LayerApproverOptions[]>(`/teams/${teamId}/members/${employeeId}/approvers/${layer}`, { approverIds });

export const clearApproverOverride = (teamId: string, employeeId: string, layer: number) =>
  apiDelete<LayerApproverOptions[]>(`/teams/${teamId}/members/${employeeId}/approvers/${layer}`);
