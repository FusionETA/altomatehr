import { apiGetFresh } from "@/shared/lib/api-client";

// Superadmin-only: how the API behaves across every company. Always fetched
// fresh — a cached copy of "what is failing right now" is the wrong answer.

export type ApiEndpointSummary = {
  method: string;
  route: string;
  calls: number;
  clientErrors: number;
  serverErrors: number;
  errorRate: number;
  averageMs: number;
  slowestMs: number;
};

export type ApiMonitoringCompany = {
  organizationId: string;
  organizationName: string | null;
  calls: number;
};

export type ApiMonitoringSummary = {
  from: string;
  to: string;
  totalCalls: number;
  totalClientErrors: number;
  totalServerErrors: number;
  averageMs: number;
  endpoints: ApiEndpointSummary[];
  companies: ApiMonitoringCompany[];
};

export type ApiRequestError = {
  id: string;
  createdAt: string;
  method: string;
  route: string;
  statusCode: number;
  durationMs: number;
  organizationId: string | null;
  organizationName: string | null;
  callerType: string;
  callerId: string | null;
  errorMessage: string | null;
  exceptionType: string | null;
  exceptionSource: string | null;
};

export type ApiMonitoringFilter = {
  from: string;
  to: string;
  organizationId?: string | null;
};

function query(params: Record<string, string | number | null | undefined>) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== "") search.set(key, String(value));
  }
  return search.toString();
}

export const getApiMonitoringSummary = (filter: ApiMonitoringFilter) =>
  apiGetFresh<ApiMonitoringSummary>(
    `/platform/api-monitoring/summary?${query({ ...filter })}`,
  );

export const getApiMonitoringErrors = (
  filter: ApiMonitoringFilter & {
    method?: string | null;
    route?: string | null;
    status?: number | null;
    limit?: number;
  },
) =>
  apiGetFresh<ApiRequestError[]>(`/platform/api-monitoring/errors?${query({ ...filter })}`);
