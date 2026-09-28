import type { ChartOfAccount } from "../api";

// Xero's four expense types, named and ordered as Xero's own chart lists them.
// Locally all four are type EXPENSE — which is all a claim needs — and
// `xeroType` keeps which one each account was, so the Expenses list can be
// filtered the way Xero's is.
export const EXPENSE_TYPES = [
  { id: "DEPRECIATN", label: "Depreciation" },
  { id: "DIRECTCOSTS", label: "Direct Costs" },
  { id: "EXPENSE", label: "Expense" },
  { id: "OVERHEADS", label: "Overhead" },
] as const;

export type ExpenseTypeId = (typeof EXPENSE_TYPES)[number]["id"];

// A hand-made account has no Xero type, and a Xero one has none until its next
// sync — both are plain expense accounts, so they file under Expense rather
// than vanishing from every filter but All.
export function expenseTypeOf(account: ChartOfAccount): ExpenseTypeId {
  const type = account.xeroType?.toUpperCase();
  return EXPENSE_TYPES.some((t) => t.id === type) ? (type as ExpenseTypeId) : "EXPENSE";
}

export const expenseTypeLabel = (id: ExpenseTypeId) =>
  EXPENSE_TYPES.find((t) => t.id === id)?.label ?? "Expense";
