// The release notes behind the "What's new" panel, newest first.
//
// Written for the person using the screen, not for developers: name the page
// and what they can now do, one line per change. Maintained by the
// `whats-new` agent (.claude/agents/whats-new.md) from what reaches main.
//
// This repo is public — never a client's name, an employee, or a real figure.
//
// Bookmark: the last commit on main these notes already cover. The next update
// writes up everything after it — from any branch — then moves it forward.
// whats-new-covered-up-to: 48268b1

export type WhatsNewEntry = {
  /** Release day, yyyy-MM-dd (Malaysia time). Also what "unread" compares. */
  date: string;
  /** Optional one-line headline for the day. */
  title?: string;
  new?: string[];
  improved?: string[];
  fixed?: string[];
};

export const WHATS_NEW: WhatsNewEntry[] = [
  {
    date: "2026-10-08",
    fixed: [
      "Xero: reloading the connection page after a successful connect no longer shows 'Xero didn't finish connecting'. A failed reconnection attempt while still connected shows a calm 'nothing changed — still connected' note instead of an error.",
      "Attendance (Today, Employees tab) and Leave balances/bulk export now list staff only — the Owner and admins no longer appear.",
    ],
  },
  {
    date: "2026-10-07",
    new: [
      "Admins: System Settings → Admins → Manage access (three tabs): per-module Off / View / Manage; Employees (all or only those on chosen policies); Settings (change company settings). View-only admins can read and download but not create/edit/delete. Limited admins see only their policy's employees everywhere, can view runs but not run payroll or download company-wide files. Only the Owner can add, remove or change an admin's role.",
      "Admins: the Owner can now remove an admin (System Settings → Admins → Remove). Their access to this company ends at once; their login stays for any other company. Only the Owner can set an admin's password.",
      "Adding an employee: leave Employee ID blank to get the next number in your company's pattern automatically. Admins and owners don't need an employee ID.",
      "Employee profile → Payroll → Bank / payout: new payment method \"Other bank / e-wallet\" (Merchantrade, overseas banks) for people paid by hand. The bank payroll file now only includes Malaysian bank transfers.",
      "Payroll → Run downloads: new \"Manual Payments\" Excel lists everyone paid outside the bank file (other bank, cash, cheque, or missing bank details), with amounts and a total.",
      "Payroll → Payment Schedule PDF: split into Bank file and Paid manually, each with a total that together equal the run's net pay.",
      "Claims → Settings → How approved claims are paid: new option \"Don't send anywhere\" — approved claims stay in AltomateHR and you reimburse them your own way.",
      "Payslips → Form EA: lists each year you were paid in; downloadable once all 12 months are approved. Until then it shows when ready and the count of approved months.",
      "Admins: Employee profile → Documents → LHDN Forms: Form EA for each year an employee was paid in, using the year picker. Enabled when all 12 months are approved; disabled with the reason, or \"No approved payroll for this employee in <year>\". File name EA_<employee no>_<year>.pdf.",
    ],
  },
  {
    date: "2026-10-06",
    new: [
      "Admins: Employee profile → Transfer: move an employee to another company you run, now or on a date you set. Personal details always transfer; payroll, bank and this year's year-to-date transfer when you tick them. The employee list shows a \"Transfer → company on date\" tag; you can cancel a pending transfer.",
      "Admins: Employee profile → Duplicate: employ the same person at another company you run, keeping them active here. Personal details and staff number transfer; statutory numbers and bank transfer when you tick them. Salary and history stay separate per company.",
      "Employee profile → Employment: a history of joins, leaves, transfers between companies and restores from archive.",
      "Payroll: archived employees are paid up to their last day; the run picker shows employees per month.",
      "Payroll → Run: \"Pay figures are final\" flag for ABPay runs. When set, hours sent are paid exactly as received, with no extra proration.",
    ],
    improved: [
      "Sign-in lands on the company you work for today. Former companies show as \"Former · payslips only\" (read-only); you can remove them with \"Leave company\".",
      "Transferring or duplicating an employee marks payroll drafts stale in both companies. When the old company submits, the new company's carried year-to-date is recalculated.",
    ],
  },
  {
    date: "2026-10-05",
    new: [
      "Employee profile → Personal → Identity: work permit number and expiry for foreign workers, showing when it expires or that it has expired.",
      'Executive Overview: a "Work permits" card lists foreign workers whose permit expired or expires within 60 days, soonest first.',
    ],
    improved: [
      'Employee import/export spreadsheet: "Work Permit No" and "Work Permit Expiry" columns added to the Personal section.',
    ],
    fixed: [
      'Employee profile: a nationality saved as "Malaysian." (with a full stop, e.g. from an import) is now shown as Malaysian, as payroll already treated it.',
    ],
  },
  {
    date: "2026-10-04",
    new: [
      "Account menu → Guide: opens the user guide for this portal in a new tab.",
    ],
  },
  {
    date: "2026-10-01",
    new: [
      "Account menu → What's new: AltomateHR's release notes, newest first. A dot shows on the menu until you've read the latest.",
    ],
    improved: [
      "Admins → API integrations: an API key can now only do what its permissions allow — a key with read access can no longer create employees, approve leave or change settings. Things people do for themselves (clocking in, applying for leave, filing claims or overtime) and account actions (passwords, switching company) aren't available to API keys at all.",
      "Settings → Projects: a new project keeps the geofence sites and allowed IPs it's created with.",
    ],
    fixed: [
      "Payroll: a Malaysian employee is always taxed as a tax resident, matching what their profile shows. Some profiles brought over from the previous system were taxed at the 30% non-resident rate; re-run any draft month to pick up the change. Submitted months are not changed.",
      "Payroll: children brought over from the previous system now count for PCB child relief. Some were shown on the employee's profile but left out of the calculation; re-run any draft month to pick them up. Submitted months are not changed.",
    ],
  },
  {
    date: "2026-09-30",
    new: [
      "Employee profile → SOCSO number: \"Use ID number\" fills it from the IC, and employee imports do the same when the SOCSO number is left blank.",
    ],
    improved: [
      "Attendance: the clock-in card shows whether you're on site or off site, and how far away, before you clock in.",
      "Payroll → PCB calculation details: the C-suite (Table 4) formula is shown without the rebate it doesn't have.",
    ],
    fixed: [
      "Attendance: records are marked off-site using your organization's own radius instead of a fixed 200 m.",
    ],
  },
  {
    date: "2026-09-29",
    new: [
      "Payroll → Annual reports: PCB 2(II) in LHDN's official layout, with receipt numbers per month and a download for everyone at once.",
      "Payroll → Annual reports: Form EA in LHDN's C.P.8A layout and the CP8D file in its current 22-field format.",
      "Employee profile: employment status and contract end date, used in CP8D.",
      "Payroll → Import year-to-date: search the preview by name or staff number.",
      "Leave: admins can cancel any employee's leave; employees can ask to cancel an already-approved leave (the request goes through approval).",
    ],
    improved: [
      "Payroll → PCB calculation details: shows the zakat actually paid this month, any zakat carried forward, and the chargeable income before it's floored at zero.",
      "Signing in through Altomate: New company, Change password and Log out are managed there, so they're hidden here.",
      "Searching inside a dropdown keeps your typing in the search box, and switching to a year you've already opened shows it straight away.",
    ],
    fixed: [
      "Employee profile: the voluntary EPF rate saves and shows as the percentage you typed.",
      "Imported payslips show the employee's staff number instead of an internal id.",
    ],
  },
];
