// Choices for the Form E / CP8D employer fields, with the exact values the
// previous system stored — v1's company info was migrated verbatim, so these
// strings are what the rows already hold. Changing a value here would leave
// every migrated company showing "Not set".
export const REFERENCE_TYPE_OPTIONS = [
  { value: "01 - SG", label: "01 - SG (Individual non-business)" },
  { value: "02 - OG", label: "02 - OG (Individual business)" },
  { value: "03 - C", label: "03 - C (Company)" },
  { value: "04 - D", label: "04 - D (Partnership)" },
  { value: "05 - F", label: "05 - F (Co-operative society)" },
  { value: "06 - TR", label: "06 - TR (Trust body)" },
  { value: "07 - LE", label: "07 - LE (Limited liability partnership)" },
];

export const EMPLOYER_CATEGORY_OPTIONS = [
  "1 - Statutory Body",
  "2 - Government Department",
  "3 - Local Authority",
  "4 - Public Sector (Other)",
  "5 - Private Sector (Other than Company)",
  "6 - Company",
].map((value) => ({ value, label: value }));

export const EMPLOYER_STATUS_OPTIONS = [
  "1 - In Operation",
  "2 - Dormant",
  "3 - In Receivership",
  "4 - In Liquidation",
  "5 - Dissolved",
].map((value) => ({ value, label: value }));

export const CP8D_FURNISH_TYPE_OPTIONS = [
  "1 - Via e-Data Praisi / e-CP8D",
  "2 - Via paper form",
].map((value) => ({ value, label: value }));
