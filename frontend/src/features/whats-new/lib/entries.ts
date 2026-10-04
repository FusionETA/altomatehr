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
// whats-new-covered-up-to: 4c5e254

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
