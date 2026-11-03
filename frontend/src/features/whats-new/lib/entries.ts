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
// whats-new-covered-up-to: 5e7a30e

// A line is for everyone, or — wrapped in admins(…) — for admins and owners
// only. Employees and supervisors never see an admins line, so it doesn't
// start with "Admins:".
export type WhatsNewLine = string | { text: string; admins: true };

export const admins = (text: string): WhatsNewLine => ({ text, admins: true });

export type WhatsNewEntry = {
  /** Release day, yyyy-MM-dd (Malaysia time). Also what "unread" compares. */
  date: string;
  /** Optional one-line headline for the day. */
  title?: string;
  new?: WhatsNewLine[];
  improved?: WhatsNewLine[];
  fixed?: WhatsNewLine[];
};

export const WHATS_NEW: WhatsNewEntry[] = [
  {
    date: "2026-11-03",
    new: [
      admins("If you're limited to some policies, you can now create teams in System Settings → Teams, and change any team whose staff are all on your policies."),
      admins("On an employee's Statutory tab, you can now pick \"Not covered by SOCSO\". Nothing is deducted, the same as leaving it blank, and no SOCSO number is needed."),
    ],
    improved: [
      admins("Any admin can now add, rename, re-rate or remove overtime types, not just the owner."),
      admins("If you're limited to some policies, you can't change a team with someone outside them, such as another admin or the owner. The message says how many."),
    ],
    fixed: [
      "Some people no longer see \"The server hit an unexpected error\" on sign-in or across the app. An unrecognised SOCSO setting was causing it.",
      admins("If you're limited to some policies, Transfer and Duplicate now show on employees in your policies, and work for them. The buttons never showed for you before."),
    ],
  },
  {
    date: "2026-11-02",
    new: [
      admins("The Reimbursed claims card on a payroll run has a search for employee, staff ID or claim number, and an \"Add all\" button for the claims it shows."),
      admins("System Settings → Work Schedule has a new Overtime types card. The owner can rename or re-rate the three built-in types, and add their own, such as Night shift."),
      "When requesting overtime, you can now pick one of your company's own types, such as Night shift. Automatic lets the calendar pick, as before.",
      "Approvers see which overtime type each request is for.",
      admins("A payroll run's adjustments have a box for each overtime type, and the run's payslips list each type's hours."),
    ],
    improved: [
      admins("Leave a policy's overtime rate blank to use the company's rate. You can also switch off any of your own types for that policy."),
      admins("The \"in shift\" rates for rest days and public holidays are gone from policies. They only changed the rate preview on overtime requests, not what was paid."),
      admins("Changing an overtime type's rate flags draft payroll runs to re-run. Approved months keep what was filed."),
    ],
  },
  {
    date: "2026-10-28",
    new: [
      admins("OCBC (incl. OCBC Al-Amin) can now be picked as the payroll bank in Payroll → Settings. Its run download is an OCBC Velocity GIRO file, with a recipient reference like \"SALARY OCT 2026\"."),
      admins("The OCBC download asks for a Tape ID. It's 001 by default; use a new one for each file sent for the same payment date."),
      admins("The OCBC download has an optional ID check tickbox, off by default. It has the employee's bank match their IC or passport to the account."),
      admins("When connecting Xero finds several organisations, the Xero card asks \"Which Xero organisation is this company?\" The admin who connected picks one."),
    ],
    improved: [
      admins("Reconnecting Xero now keeps the organisation the company is already connected to."),
    ],
    fixed: [
      admins("A September run's Hong Leong bank-file reference was pre-filled as \"SALARY SEPTEMBER 202\", losing its last digit. It now reads \"SALARY SEP 2026\"."),
    ],
  },
  {
    date: "2026-10-27",
    new: [
      "Companies without Xero that have no claim item types now get 22 ready-made ones, such as Travel – Local, Telephone & Internet and Medical (Staff).",
      admins("Connecting Xero archives the ready-made claim item types, and disconnecting restores them. Your own archive choices stay either way."),
      "Mileage claims can now start from an optional Google Maps or Waze route screenshot. AI fills in the distance, start and end; the approver sees the screenshot.",
      admins("An employee's page has a new Resend welcome email button. It explains how to sign in, pointing to Forgot password if their first password no longer works."),
    ],
    improved: [
      "Without Xero, \"Chart of accounts\" is now called \"Claim item types\" everywhere in the app. A company set to No Xero is no longer asked to connect Xero on the claim settings or item types pages.",
      admins("The Xero card can say \"Uses your company's own Xero app\", and Connect waits for AltomateHR to set that app up. A company set to No Xero sees \"Xero isn't switched on for your company\" instead of Connect."),
      admins("The Motor Vehicle / Mileage item starts switched off, so mileage claims are refused until you switch it on after setting its per-km rate. Until one is on, the claim form tells staff to ask their admin."),
      admins("Posting payroll to Xero now stops on a reimbursed claim whose account isn't linked to Xero, and names the claim, instead of posting it under a code Xero may use for a different account."),
    ],
    fixed: [
      "Claims: on iPhone, the expense and mileage forms no longer slide sideways, now that the date fields fit the screen. Going back and switching from an expense to a mileage claim no longer keeps the receipt's details and attachment.",
    ],
  },
  {
    date: "2026-10-26",
    new: [
      "The first payslip of each year adds last year's PCB the company paid as a taxable perquisite on the EA form. It isn't paid in cash and carries no EPF, SOCSO or EIS. Imported months don't count, and the YTD import warns about them.",
      admins("Payroll → Settings → Xero sync has an optional \"PCB borne by employer\" account. Left blank, that PCB posts to Salary."),
    ],
    improved: [
      admins("Payroll: unpaid leave is now docked at the monthly salary ÷ the month's calendar days, whatever the working-days setting (the 26-day setting used ÷26). RM 3,000 in October is RM 96.77 a day."),
      "Only draft runs re-run from now on change, and approved runs keep what was filed. Imported months, and months with unpaid days typed on the run, are left alone.",
      admins("Payroll: unpaid leave approved after its month is deducted on that person's next run that year, as its own line, and settled once. If it's later cancelled or moved, the days are paid back as arrears of wages."),
      admins("The first re-run after this update also deducts late-approved leave for earlier months this year, so check any you handled by hand. Leave cancelled in months approved before this update isn't refunded automatically."),
      admins("Approving or reverting a run, or approving or cancelling leave for an earlier month, flags later draft runs that year for re-run. Reverting also reverts linked later runs, and the confirmation lists them."),
      admins("A run can't be sent for approval while an earlier month's run for any of its people is still unapproved."),
      "With \"PCB borne by employer\" on, the company pays that PCB, so it's not taken from net pay. Gross shows real pay, and payslips show \"+ RM x paid by employer\"; re-run drafts to pick it up.",
      "Past runs with it on, filed ones included, show the corrected gross and summaries automatically. Net pay and PCB amounts don't change.",
      admins("The summary PDF and Excel get an employer-paid PCB column when some PCB is company-paid. Total PCB payment is still the full amount sent to LHDN."),
      admins("With PCB borne by the employer, approving or reverting a run also flags next year's drafts. A revert is refused while next year's run for those people is approved."),
    ],
  },
  {
    date: "2026-10-25",
    new: [
      admins("Payroll → Run downloads has a new \"Download EPF, SOCSO & PCB (zip)\" button for the four statutory files. A file that isn't ready is left out, and its row says why."),
      admins("Settings → Admins now shows each admin's and the owner's email under their name. The owner can also Edit an admin's name and login email."),
    ],
    improved: [
      admins("In Payroll → Run downloads, the Allowance and Deduction reports are now one row, \"Allowance & Deduction Reports (ZIP)\", giving both Excel files in one zip."),
      admins("If a run has only one of the two reports, the zip holds that one and says which is missing."),
      admins("You can't change a login email if that login also opens a company you don't own, on the employee edit or the Admins page. Altomate support still can."),
      admins("Only the owner can change an admin's login email, and another owner's only through Altomate support."),
    ],
  },
  {
    date: "2026-10-23",
    new: [
      admins("Each employee can now have a Nickname, set per company next to their full name, in Add employee or in the import and export."),
      "It shows beside the full name across the app, and the employee list searches it. Payslips, statutory files, EA forms, bank files and letters keep the full name.",
      admins("Each person's adjustments on a payroll run now have an Employer HRDF levy box. Blank keeps the computed levy, shown greyed; any amount, 0 included, replaces it."),
      "The levy is an employer cost only, so the employee's pay doesn't change. The run's adjustments list shows it as \"HRDF levy RM x\".",
      "Re-run payroll after changing it. Only the months where you type a levy are affected.",
    ],
    improved: [
      admins("An imported month can no longer be reverted to draft. To correct it, import the file again under Import earlier payroll."),
    ],
    fixed: [
      admins("Earlier payroll imports now read HRDF columns with other names, such as \"Employer HRDF Levy\". Those months showed HRDF 0; import the file again to fix them."),
      admins("Reverting an earlier month no longer sends a later imported month back to draft, where its figures could be overwritten."),
    ],
  },
  {
    date: "2026-10-22",
    new: [
      admins("A month can now be paid in several runs, such as one for office staff and one for workers. Each person can be in only one run a month, and the picker greys out anyone already in another."),
      admins("A month with several runs gets an \"all runs\" row showing its combined totals, plus a combined Summary to download as PDF or Excel. It's marked as a draft until every run is approved."),
      admins("Rename any run with the pencil next to its title, whatever its status. Its payroll files and Xero journal narration carry the new name."),
      admins("In a run's adjustments, Basic this month pays a monthly employee a set basic for that month, such as a part-month joiner's basic. Their salary doesn't change."),
      admins("SOCSO scheme now has Not covered, for someone PERKESO doesn't cover, such as a director. No SOCSO or SKBBK is deducted or paid for them."),
    ],
    improved: [
      admins("In a run's Needs attention, each missing number links to that person's record in a new tab, at the right field. Names link there too, and a draft run refreshes when you return."),
      admins("Policies is no longer a per-admin box in Manage access. Every admin can edit the policies they cover, and an admin who sees all employees can edit all of them."),
      admins("An admin limited to some policies sees only those. One that also covers the company's admins or owner, usually the default, shows \"Owner only\" and can't be edited by them."),
      admins("Only the owner can create, archive or restore a policy, or change the default. Adding an employee starts on your own policy if you can't see the default one."),
      admins("The payroll overview's Latest card adds up all of the latest month's runs. It reads Not all approved until every one is approved."),
      admins("Reverting a run now only sends back later approved runs that share people with it."),
      "EA forms count a month with several runs only once every run is approved. PCB 2(II) shows each person's own run's LHDN receipt. Single-run months don't change.",
      admins("A staff loan installment now counts as paid only once the borrower's own payslip for that month is approved — a month they weren't paid in (left out of the run, or in another group's run) no longer shows as repaid."),
      "Annual and sick leave now meet the Employment Act minimum for years of service: annual 8, 12 or 16 days, sick 14, 18 or 22 days.",
      admins("The minimum uses each profile's join date, and is pro-rated for lump-sum annual leave in the join year. Reset a balance below it to raise it."),
      admins("A Malaysian aged 55–59 with no SOCSO scheme is flagged incomplete on their profile, and left out of runs until one is chosen."),
    ],
    fixed: [
      admins("Staff with a SOCSO number but no scheme were paid no SOCSO; now the age-based scheme applies. Re-run a draft payroll to apply; approved months keep what was filed."),
      "A run's own unpaid-leave line no longer stacks with approved Leave days, so those days are docked once, not twice. Re-run a draft payroll to apply; approved months keep what was filed.",
      admins("In Payroll, a run's generated and approved times now show the right time, not 8 hours early."),
    ],
  },
  {
    date: "2026-10-21",
    new: [
      admins("A payroll run's adjustments now have an Unpaid leave box, for monthly staff whose unpaid leave isn't in the Leave module yet."),
      "Type the unpaid days and they're docked like approved unpaid leave: their own payslip line, and off gross and the EPF, SOCSO, EIS and PCB wage.",
      "Leave the box blank to use the days approved in the Leave module. A typed number replaces them, so no day is docked twice.",
      "Re-run a draft payroll to apply. Only draft months change; approved months keep what was filed.",
    ],
  },
  {
    date: "2026-10-20",
    new: [
      admins("You can now change your own password from the account menu (⋮) in the admin portal. Changing it signs you out on every device. Not shown if you sign in through Altomate."),
    ],
    fixed: [
      "Changing your password or switching company no longer fails with a 401 error after you've been away a while. Your sign-in now renews quietly in the background.",
    ],
  },
  {
    date: "2026-10-19",
    new: [
      admins("Edit leave that hasn't started yet from Leave → History. Change its type, dates, half day or reason, and the employee is told what changed."),
      admins("The leave keeps its approval status. If it's unpaid leave, draft payroll runs for the old and new months are marked to re-run."),
    ],
    improved: [
      admins("In a payroll run's \"What gets remitted\", the EPF total now shows the employee and employer amounts underneath. The total itself is unchanged."),
    ],
  },
  {
    date: "2026-10-16",
    new: [
      admins("A leave history import now lists leave types you haven't set up, so you can add them and import again. History, Balances and Overview refresh after it."),
    ],
    improved: [
      admins("In Payroll, Gross now takes off deductions the company keeps (Miscellaneous, Loan Repayment), as PayrollPanda does, for past months too. Statutory amounts and files don't change."),
      admins("In Leave → Types, set each type to Everyone, Female only or Male only. Staff only see and apply for the types meant for them."),
      "The Launch Appraisify button in your account menu now opens Appraisify in a new tab, so AltomateHR stays open. If your browser blocks the new tab, or you use AltomateHR from your home screen, it opens here as before.",
      admins("The CIMB bank file no longer asks for a payment date in Payroll → Run downloads, as BizChannel sets it when you upload."),
    ],
    fixed: [
      admins("A bank file is refused, naming the person, if an account number is blank, has no digits such as N/A, or for CIMB is too long. CIMB no longer cuts a long number short. Approved months too."),
      admins("When a company pays an extra EPF rate on top of the statutory one, employer EPF and cost to employer were RM 1–6 too high. Re-run draft payroll to apply the fix; approved months keep what was filed."),
      admins("Saving a leave type in Leave → Types no longer turns off \"prorate first year\"."),
    ],
  },
  {
    date: "2026-10-15",
    improved: [
      admins("The CIMB bank file in Payroll → Run downloads is now CIMB's own BizChannel Excel template, for IC and passport holders alike. It applies to approved months too."),
      admins("CIMB no longer needs an organisation code in Payroll settings."),
      admins("An EPF number must be digits only. A placeholder like \"XXXXXX\" is refused on import, flagged on the profile even if saved earlier, and blocks a draft run's submission."),
    ],
    fixed: [
      admins("LHDN's e-PCB upload no longer refuses the PCB (CP39) file over its name, in Payroll → Run downloads. The name now has letters and digits only, for every month; amounts don't change."),
      admins("A CIMB run with someone paid by passport no longer has its bank file refused. They're included in the file with everyone else."),
      admins("The EPF file won't download while anyone in it has a placeholder EPF number; it names them so you can fix each one. This applies to approved months too."),
    ],
  },
  {
    date: "2026-10-14",
    new: [
      admins("A draft or a run awaiting approval now offers the payroll summary as PDF or Excel, marked NOT APPROVED, to send to the approver. Bank and statutory files still wait for approval."),
    ],
    improved: [
      admins("An employee's Previous employment (TP3) now needs a Year once you enter an amount. Without one, payroll ignored those amounts, so the profile won't save."),
      admins("The Salary adjustments import now accepts a change for someone already archived, if it starts on or before their last day. Later dates are still refused."),
      admins("Import earlier payroll now warns when a row's IC isn't on file, names anyone a re-import would remove, and shows who a misspelt name matched."),
    ],
    fixed: [
      admins("Re-run payroll on a draft now adds people who transferred in or joined after it was made. Anyone you unticked stays out; drafts made before this update and approved runs don't change."),
      admins("Import earlier payroll no longer gives a year to the wrong person. It refuses a row whose IC belongs to someone else, or two rows for one person, and says who to check."),
      admins("Cost to employer no longer counts Loan Repayment or Miscellaneous / Other deductions, which the company keeps. CP38, Additional PCB and zakat still count. Months already run change only when re-run or re-imported."),
      admins("PCB no longer taxes an advance, unpaid leave or salary correction again in later months. Months already run keep their PCB until re-run."),
    ],
  },
  {
    date: "2026-10-13",
    new: [
      admins("Add your company logo in Settings → Organization. It shows at the top of the letters you create."),
      admins("Save signatures for your letters in Settings → Organization → Signatures. Draw one on screen or upload a picture, and choose one as the company default."),
      admins("Letters can now be signed. When you create a letter, pick which signature to print, or none if you'd rather sign by hand. A template can also have its own default signature."),
      admins("You can now search a payroll run's payslips by name, staff ID or job title."),
      "Employees: in My leave, tap Edit on a request no approver has acted on yet to change its type, dates, reason or attachment. It stays pending; leave HR filed or imported can't be edited.",
    ],
    improved: [
      "Claims: tap an attached receipt or document to open it, and add or replace the main receipt on the claim form. \"Skip and fill manually\" is now called \"Skip AI and fill manually\"; an attached receipt stays with your claim.",
      admins("When you sync a claim to Xero, its receipt and supporting documents are now attached to the bill or spend money, so they show in Xero."),
      "Leave: when you apply or edit, the form now shows the working days you'll use (weekends and public holidays skipped), not calendar days. The balance warning is based on that count only.",
      "Payroll → Run downloads: the Breakdown sheet in Payroll Summary (Excel) now has one row per employee, a column for each pay item, and a Total Gross after loans and other deductions, with totals at the bottom.",
      "Benefits in kind, TP1 items, zakat and additional PCB sit apart from Total Gross and aren't added into it.",
      "Claims: after you replace a receipt that an AI read, the form says the details came from the old one, so you can check them.",
    ],
    fixed: [
      "Claims: adding more supporting documents no longer removes the ones you already added. You can attach up to 10, and remove any with the ✕.",
      "Claims: older claims with blank document slots now save normally, and a receipt or document that won't open shows a message instead of doing nothing.",
      "Leave: a half day on a weekend or public holiday no longer counts, so it's refused when you apply or edit. A request spanning New Year no longer skips the next year's public holidays.",
      admins("Payroll: an approved unpaid half day on a weekend or public holiday is no longer deducted from pay, in any month whose run is still a draft or gets regenerated. Approved runs don't change."),
      "Claims: the Employee and Company bank details on a claim now show names, not a code. Employees no longer see a code on their own claims.",
      admins("On leave today, Attendance and the dashboard now show names. Anyone you can't see shows as \"Unknown person\", not a code."),
    ],
  },
  {
    date: "2026-10-12",
    new: [
      admins("Let AI write a letter template for you in Documents → Templates → Write with AI. Describe the letter in English or Bahasa Melayu, then pick from two drafts."),
      admins("While editing a template, use Ask AI to change it, for example \"make it more formal\" or \"translate to Bahasa Melayu\". You can undo the last change."),
    ],
  },
  {
    date: "2026-10-08",
    new: [
      admins("Documents → Templates: a library of HR letters (offer, confirmation, resignation, warning, or your own). Start from four sample letters, write one in the editor, or upload a Word (.docx), .txt or .md file to review in the editor before saving. Documents access is set per admin under Manage access, and API integrations can be given letter access."),
      admins("Employee profile → Documents → Letters: generate a PDF letter for that employee. Details fill in from the record; any missing one (such as ID number or address) must be typed in or marked blank before Generate. Tick \"Also save to employee record\" to keep a typed detail there, which fills only blank fields. Letters on file are kept for admins only and never appear in the employee's portal; opening them needs view access to Employees."),
      admins("Employee profile → Employment and Company: new Probation (months) and Confirmation date fields, printed on offer and confirmation letters; and a Work location field next to Department, which fills the letter's Work location placeholder."),
      "Payroll → Run downloads: Payroll Summary can now be downloaded as Excel as well as PDF, with one row per employee to sort and filter, plus the totals and each person's itemised pay.",
      "Payroll → Run downloads: new AB Pay timesheet (Excel) for approved runs, for companies connected to ABPay. Each employee's pay is in AB Pay's timesheet layout, ready for ABPay to import, with a Statutory sheet (EPF, SOCSO, EIS, PCB, net pay) and notes on how Total Gross is worked out.",
      "Payroll → Run downloads: Allowance and Deduction reports (Excel). One row per employee, one column per item, with totals. Deductions exclude EPF, SOCSO, EIS, PCB and other statutory payments; benefits in kind and from-gross items (unpaid leave, advances, salary adjustments) are listed but kept out of the cash total. Also included in the download-all zip.",
    ],
    improved: [
      "Payroll → Annual forms: new setting \"Payroll at this company started in <month>\" for a company that began mid-year. Annual forms (EA, CP8D, Form E) then wait only from that month through December; by default they still wait for January–December. If you paid staff earlier in the year in another system, import those months (YTD import) instead of setting this.",
      "Claims → Settings: now shows for each route what happens to own-money and company-money claims.",
      "Payslips: opens on the latest year, with a tab for each earlier year you were paid in (newest first), instead of one long list of every payslip. The year tabs appear when you have payslips in more than one year.",
    ],
    fixed: [
      "Payroll → Settings → General: choosing when payroll started here (on Annual forms) no longer marks payroll as set up — General keeps its \"not set up\" note until you save your settings there.",
      "Payroll → Annual forms: for a year before this company's payroll started here, shows \"No payroll ran here in {year}\" instead of \"0/0 monthly runs approved\"; the employee's EA card no longer says \"(0 of 0 months so far)\".",
      "Company-money claims no longer get stuck on the \"Add to payroll\" route; they now go to Xero as Spend Money from the paying bank, or nowhere if Xero isn't connected.",
      admins("Xero: reloading the connection page after a successful connect no longer shows 'Xero didn't finish connecting'. A failed reconnection attempt while still connected shows a calm 'nothing changed — still connected' note instead of an error."),
      "Attendance (Today, Employees tab) and Leave balances/bulk export now list staff only — the Owner and admins no longer appear.",
      "Payroll → Bank files: the Public Bank payroll file no longer refuses to download when a run includes employees paid by Other bank / e-wallet (e.g. Merchantrade), cash or cheque. They're left out of the file and paid from the Manual payments sheet. This applies to any month's run with such employees; bank-transfer amounts are unchanged.",
      "Payroll → Import year-to-date: preview warnings now show only the months in the uploaded file, not months the import won't touch.",
    ],
  },
  {
    date: "2026-10-07",
    new: [
      admins("System Settings → Admins → Manage access (three tabs): per-module Off / View / Manage; Employees (all or only those on chosen policies); Settings (change company settings). View-only admins can read and download but not create/edit/delete. Limited admins see only their policy's employees everywhere, can view runs but not run payroll or download company-wide files. Only the Owner can add, remove or change an admin's role."),
      admins("The Owner can now remove an admin (System Settings → Admins → Remove). Their access to this company ends at once; their login stays for any other company. Only the Owner can set an admin's password."),
      "Adding an employee: leave Employee ID blank to get the next number in your company's pattern automatically. Admins and owners don't need an employee ID.",
      "Employee profile → Payroll → Bank / payout: new payment method \"Other bank / e-wallet\" (Merchantrade, overseas banks) for people paid by hand. The bank payroll file now only includes Malaysian bank transfers.",
      "Payroll → Run downloads: new \"Manual Payments\" Excel lists everyone paid outside the bank file (other bank, cash, cheque, or missing bank details), with amounts and a total.",
      "Payroll → Payment Schedule PDF: split into Bank file and Paid manually, each with a total that together equal the run's net pay.",
      "Claims → Settings → How approved claims are paid: new option \"Don't send anywhere\" — approved claims stay in AltomateHR and you reimburse them your own way.",
      "Payslips → Form EA: lists each year you were paid in; downloadable once all 12 months are approved. Until then it shows when ready and the count of approved months.",
      admins("Employee profile → Documents → LHDN Forms: Form EA for each year an employee was paid in, using the year picker. Enabled when all 12 months are approved; disabled with the reason, or \"No approved payroll for this employee in <year>\". File name EA_<employee no>_<year>.pdf."),
    ],
  },
  {
    date: "2026-10-06",
    new: [
      admins("Employee profile → Transfer: move an employee to another company you run, now or on a date you set. Personal details always transfer; payroll, bank and this year's year-to-date transfer when you tick them. The employee list shows a \"Transfer → company on date\" tag; you can cancel a pending transfer."),
      admins("Employee profile → Duplicate: employ the same person at another company you run, keeping them active here. Personal details and staff number transfer; statutory numbers and bank transfer when you tick them. Salary and history stay separate per company."),
      "Employee profile → Employment: a history of joins, leaves, transfers between companies and restores from archive.",
      admins("Payroll: archived employees are paid up to their last day; the run picker shows employees per month."),
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
      admins("Payroll: a Malaysian employee is always taxed as a tax resident, matching what their profile shows. Some profiles brought over from the previous system were taxed at the 30% non-resident rate; re-run any draft month to pick up the change. Submitted months are not changed."),
      admins("Payroll: children brought over from the previous system now count for PCB child relief. Some were shown on the employee's profile but left out of the calculation; re-run any draft month to pick them up. Submitted months are not changed."),
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

export type WhatsNewAudience = "everyone" | "admins";

export const lineText = (line: WhatsNewLine): string => (typeof line === "string" ? line : line.text);

// The notes as this reader sees them: admins see every line; everyone else
// only the lines for everyone, and not a day that has none of those.
export function whatsNewFor(audience: WhatsNewAudience): WhatsNewEntry[] {
  if (audience === "admins") return WHATS_NEW;
  const forEveryone = (lines?: WhatsNewLine[]) => lines?.filter((line) => typeof line === "string");
  return WHATS_NEW.map((entry) => ({
    ...entry,
    new: forEveryone(entry.new),
    improved: forEveryone(entry.improved),
    fixed: forEveryone(entry.fixed),
  })).filter((entry) => entry.new?.length || entry.improved?.length || entry.fixed?.length);
}
