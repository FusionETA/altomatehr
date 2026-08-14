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
