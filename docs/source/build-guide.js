// Builds docs/Gplus-Guide.docx — User Guide, Step-by-Step Setup (incl. every Google/Gmail step), Technical Guide.
const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType, Table, TableRow, TableCell,
  WidthType, ShadingType, BorderStyle, LevelFormat, PageBreak, Header, Footer, PageNumber,
  TableOfContents, ImageRun, TabStopType,
} = require("docx");

const OUT = process.argv[2];
const LOGO = process.argv[3];

// ---------- look ----------
const FONT = "Arial", MONO = "Consolas";
const ACCENT = "B83227", DARK = "1F2024", MUTED = "5D6068";
const W = 9360; // content width, US Letter with 1" margins (DXA)
const border = { style: BorderStyle.SINGLE, size: 4, color: "C9CBD1" };
const cellBorders = { top: border, bottom: border, left: border, right: border };

// ---------- inline markup: **bold**, `code`, [[key]] (UI label) ----------
function runs(text, base = {}) {
  const out = [];
  for (const part of String(text).split(/(\*\*[^*]+\*\*|`[^`]+`|\[\[[^\]]+\]\])/)) {
    if (!part) continue;
    if (part.startsWith("**")) out.push(new TextRun({ ...base, text: part.slice(2, -2), bold: true }));
    else if (part.startsWith("`")) out.push(new TextRun({ ...base, text: part.slice(1, -1), font: MONO, size: 19,
      shading: { type: ShadingType.CLEAR, color: "auto", fill: "EEEFF2" } }));
    else if (part.startsWith("[[")) out.push(new TextRun({ ...base, text: part.slice(2, -2), bold: true, color: "1A5FB4" }));
    else out.push(new TextRun({ ...base, text: part }));
  }
  return out;
}

const body = [];
const add = (...x) => body.push(...x);
const headings = []; // for the pre-filled table of contents (fill-toc.js)
const H1 = (t, pageBreak = true) => { headings.push({ level: 1, text: t }); add(new Paragraph({ heading: HeadingLevel.HEADING_1, pageBreakBefore: pageBreak, children: [new TextRun(t)] })); };
const H2 = (t) => { headings.push({ level: 2, text: t }); add(new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun(t)] })); };
const H3 = (t) => add(new Paragraph({ heading: HeadingLevel.HEADING_3, children: [new TextRun(t)] }));
const P = (t, opts = {}) => add(new Paragraph({ spacing: { after: 120 }, ...opts, children: runs(t) }));

function Bullets(items, level = 0) {
  for (const it of items) {
    if (Array.isArray(it)) { Bullets(it, level + 1); continue; }
    add(new Paragraph({ numbering: { reference: "bullets", level }, spacing: { after: 60 }, children: runs(it) }));
  }
}

// Numbered steps. Each call restarts at 1. Items: string | { t, sub: [...], code: [...], note: [kind, text] }
const numberingConfigs = [];
let stepListId = 0;
function Steps(items) {
  const ref = `steps-${++stepListId}`;
  numberingConfigs.push({
    reference: ref,
    levels: [
      { level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT,
        style: { paragraph: { indent: { left: 540, hanging: 360 } }, run: { bold: true, color: ACCENT } } },
      { level: 1, format: LevelFormat.LOWER_LETTER, text: "%2.", alignment: AlignmentType.LEFT,
        style: { paragraph: { indent: { left: 1080, hanging: 360 } } } },
    ],
  });
  for (const it of items) {
    const o = typeof it === "string" ? { t: it } : it;
    add(new Paragraph({ numbering: { reference: ref, level: 0 }, spacing: { before: 60, after: 60 }, children: runs(o.t) }));
    if (o.code) Code(o.code, 540);
    for (const s of o.sub || []) add(new Paragraph({ numbering: { reference: ref, level: 1 }, spacing: { after: 40 }, children: runs(s) }));
    if (o.note) Note(o.note[0], o.note[1], 540);
  }
}

function Code(lines, indent = 0) {
  lines.forEach((l, i) => add(new Paragraph({
    indent: { left: indent },
    spacing: { before: i === 0 ? 60 : 0, after: i === lines.length - 1 ? 120 : 0, line: 260 },
    shading: { type: ShadingType.CLEAR, color: "auto", fill: "F3F4F6" },
    border: i === 0 && lines.length === 1
      ? { top: border, bottom: border, left: border, right: border }
      : i === 0 ? { top: border, left: border, right: border }
      : i === lines.length - 1 ? { bottom: border, left: border, right: border }
      : { left: border, right: border },
    children: [new TextRun({ text: l.length ? l : " ", font: MONO, size: 19 })],
  })));
}

const NOTE_STYLES = {
  Note: { fill: "E8F0FE", bar: "1A5FB4" },
  Important: { fill: "FDECEA", bar: ACCENT },
  Tip: { fill: "E6F4EA", bar: "2E7D32" },
};
function Note(kind, text, indent = 0) {
  const s = NOTE_STYLES[kind];
  add(new Paragraph({
    indent: { left: indent },
    spacing: { before: 100, after: 140 },
    shading: { type: ShadingType.CLEAR, color: "auto", fill: s.fill },
    border: { left: { style: BorderStyle.SINGLE, size: 24, color: s.bar, space: 6 } },
    children: [new TextRun({ text: kind + ":  ", bold: true, color: s.bar }), ...runs(text)],
  }));
}

function Tbl(headers, rows, widths) {
  const total = widths.reduce((a, b) => a + b, 0);
  const cell = (t, w, head) => new TableCell({
    borders: cellBorders,
    width: { size: w, type: WidthType.DXA },
    shading: head ? { type: ShadingType.CLEAR, color: "auto", fill: "2B2D33" } : undefined,
    margins: { top: 70, bottom: 70, left: 110, right: 110 },
    children: String(t).split("\n").map((line) => new Paragraph({
      spacing: { after: 0 },
      children: head ? [new TextRun({ text: line, bold: true, color: "FFFFFF", size: 19 })] : runs(line, { size: 19 }),
    })),
  });
  add(new Table({
    width: { size: total, type: WidthType.DXA },
    columnWidths: widths,
    rows: [
      new TableRow({ tableHeader: true, children: headers.map((h, i) => cell(h, widths[i], true)) }),
      ...rows.map((r) => new TableRow({ cantSplit: true, children: r.map((c, i) => cell(c, widths[i], false)) })),
    ],
  }));
  add(new Paragraph({ spacing: { after: 120 }, children: [] }));
}

// =====================================================================================
// COVER
// =====================================================================================
add(new Paragraph({ spacing: { before: 600 }, alignment: AlignmentType.CENTER,
  children: [new ImageRun({ type: "png", data: fs.readFileSync(LOGO), transformation: { width: 150, height: 150 },
    altText: { title: "Gplus logo", description: "G+ logo", name: "gplus-logo" } })] }));
add(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 400, after: 120 },
  children: [new TextRun({ text: "Gplus", bold: true, size: 72, color: DARK })] }));
add(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 480 },
  children: [new TextRun({ text: "Gmail Junk Remover for Windows", size: 34, color: ACCENT })] }));
add(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 700 },
  children: [new TextRun({ text: "User Guide  ·  Step-by-Step Setup  ·  Technical Guide", size: 26, color: DARK })] }));

add(new Paragraph({ spacing: { after: 120 }, children: [new TextRun({ text: "How this guide is organized", bold: true, size: 26 })] }));
Tbl(["Part", "Who it's for", "What's in it"], [
  ["1. User Guide", "Anyone using Gplus day to day", "What Gplus removes and why, how to rescue or teach it from Gmail, how to read its log, simple settings"],
  ["2. Step-by-Step Setup", "Whoever installs it (once)", "Every click in Google Cloud and Gmail, signing in, installing the Windows service, checking it works, removing it"],
  ["3. Technical Guide", "Developers and administrators", "Architecture, scoring rules, learning algorithms, Gmail API use, security, files, configuration and command reference, building and deploying"],
], [2100, 2400, 4860]);
add(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 900 },
  children: [new TextRun({ text: "Version 1.0  ·  September 2026", size: 22, color: MUTED })] }));
add(new Paragraph({ alignment: AlignmentType.CENTER,
  children: [new TextRun({ text: "Source: github.com/muleskin/Gplus (private)", size: 20, color: MUTED })] }));

// Contents
add(new Paragraph({ pageBreakBefore: true, spacing: { after: 200 },
  children: [new TextRun({ text: "Contents", bold: true, size: 36, color: ACCENT })] }));
add(new TableOfContents("Contents", { hyperlink: true, headingStyleRange: "1-2" }));
P("To refresh the page numbers after editing, right-click the list in Word and choose **Update Field → Update entire table**.",
  { spacing: { before: 200 } });

// =====================================================================================
// PART 1 — USER GUIDE
// =====================================================================================
H1("Part 1 — User Guide");
H2("1.1 What Gplus does");
P("Gplus is a small program that runs quietly in the background on a Windows PC. Every 5 minutes it looks at the **unread** messages in your Gmail inbox and removes the junk that Gmail's own spam filter lets through. It works on your real Gmail account, so the result shows up everywhere you read mail: the Gmail website, your phone and your tablet.");
P("It targets mail that Gmail usually treats as legitimate:");
Tbl(["Kind of junk", "Examples"], [
  ["Loans and credit", "\"You're pre-approved\", debt relief, cash advances, credit repair"],
  ["Marketing", "Sales, % off, coupons, \"last chance\", store newsletters"],
  ["Temu and bargain shopping", "Temu, Shein, AliExpress, Wish, \"spin the wheel\", coupon bundles"],
  ["Horoscopes and psychics", "Daily horoscopes, tarot, \"Mercury retrograde\""],
  ["Weather reports", "Forecasts, storm alerts from newsletters and weather sites"],
  ["Crime reports and local news", "Crime alerts, police blotters, \"breaking news\" digests"],
  ["Prizes and scams", "\"You've won\", gift-card rewards, fake invoices, phishing, \"final notice\""],
  ["Insurance and home offers", "Car warranties, Medicare plans, solar, walk-in tubs, life insurance"],
  ["Other", "Miracle pills, crypto schemes, political fundraising, dating spam, survey nags"],
], [2800, 6560]);

H2("1.2 Why it doesn't remove your real mail");
Bullets([
  "**People you write to are always safe.** Gplus reads the addresses in your Sent folder. Mail from anyone you have ever emailed is never removed.",
  "**Receipts and account mail are protected.** Order confirmations, shipping notices, login codes, statements, bills, appointments and payment notices count strongly against removal.",
  "**Starred mail is never touched,** and neither is anything you have sent yourself.",
  "**Nothing is permanently deleted.** Junk goes to Gmail's Trash, where it stays for 30 days, and every removed message is tagged with the label `Gplus-Removed` so you can always find it.",
  "**It only looks at unread mail in your inbox** from the last 30 days. Mail you've read, archived or filed is left alone.",
]);

H2("1.3 How it decides");
P("Every unread message gets a **score**. Signs of junk add points; signs of real mail subtract points. A message scoring **6 or more** is removed. Some examples of what adds or subtracts points:");
Tbl(["Sign", "Points", "Why it matters"], [
  ["Subject or body matches a junk category (\"pre-approved\", \"horoscope\")", "+1 to +6", "The strongest content clue"],
  ["Sent from a known junk site (temu.com, horoscope.com, accuweather.com)", "+7 to +10", "These senders only send junk"],
  ["Has an \"unsubscribe\" header (bulk mail)", "+3", "Real personal mail never has one"],
  ["Gmail filed it under Promotions", "+3", "Gmail thinks it's marketing too"],
  ["Pretends to be a brand (\"PayPal\" sent from a random site)", "+5", "Classic phishing"],
  ["Failed Gmail's sender-authenticity checks", "+4", "Often forged"],
  ["Learned words and phrases look like your past junk", "+3 or +5", "See 1.6"],
  ["Looks like a receipt, code, statement or appointment", "−6", "Mail you need"],
  ["Gmail marked it Important", "−2", "Gmail thinks you care"],
], [4800, 1100, 3460]);
Note("Tip", "Spammers often disguise words to dodge filters, for example 𝐁𝐎𝐋𝐃 or ＷＩＤＥ letters, invisible characters hidden between letters, or text hidden inside the email. Gplus undoes all of these tricks before it checks the words.");

H2("1.4 Finding and rescuing removed mail");
P("Removed mail is in Gmail's **Trash** with the label **Gplus-Removed**. To see everything Gplus has removed, type this into the Gmail search box:");
Code(["label:Gplus-Removed"]);
P("If Gplus removed something it shouldn't have, **don't just move it back**: tell Gplus, so it learns. Put the message in the **Gplus-NotJunk** label (step-by-step instructions in 2.13). Within about 5 minutes Gplus will:");
Bullets([
  "move the message back to your inbox (out of Trash or Spam),",
  "remember that sender as **always allowed**, and",
  "count the message as a good example for the word filter.",
]);
Note("Note", "Gmail empties Trash automatically after 30 days. Rescue anything important before then.");

H2("1.5 Teaching Gplus about junk it missed");
P("When junk gets through, put it in the **Gplus-Train** label (see 2.13). Within about 5 minutes Gplus will:");
Bullets([
  "remove the message like any other junk,",
  "**block the sender** from now on, and",
  "re-check your recent unread mail, so other messages from that sender go too.",
]);
P("How much Gplus blocks depends on the sender:");
Tbl(["Sender", "What gets blocked"], [
  ["A company's own domain, e.g. deals.somestore.com", "The **whole domain**"],
  ["A personal-mail address (gmail.com, yahoo.com, outlook.com, …)", "**Only that one address**; other people on that service are unaffected"],
  ["A bulk-mail platform shared by many brands (sendgrid.net, robly.com, …)", "**Only that one address**"],
  ["A big brand you may also need (amazon.com, paypal.com, …)", "**Only that one address**, so receipts from the brand still arrive"],
  ["A company where someone you've emailed works", "**Only that one address**"],
  ["You, someone you've emailed, or a sender on your allow list", "**Nothing is learned**; the message is still removed"],
], [4400, 4960]);
P("You can use both labels from the Gmail website or the Gmail app on your phone, and from any folder, including Trash and Spam.");

H2("1.6 The word filter (it gets smarter over time)");
P("Besides its built-in rules, Gplus learns which words and two-word phrases appear in **your** junk compared with **your** good mail, like the spam filters in Thunderbird and SpamBayes. It learns from:");
Tbl(["Counted as junk", "Counted as good"], [
  ["Messages you put in Gplus-Train", "Messages you put in Gplus-NotJunk"],
  ["Mail Gplus removed at least 2 days ago that you didn't rescue", "Mail you've filed under your own labels, or starred"],
  ["Gmail's Spam folder", ""],
], [4680, 4680]);
P("It stays silent until it has seen **30 junk and 30 good** examples. After that it adds **+5** points when it is 99% sure a message is junk, **+3** when it is 90% sure, and subtracts **3** when a message looks clearly good. On its own it can't remove a message (6 points are needed); it always needs at least one other sign of junk. It never overrides your allow list, starred mail or people you've emailed.");

H2("1.7 Checking what Gplus did");
P("Every decision is written to a daily log you can open in Excel:");
Code(["C:\\ProgramData\\Gplus\\logs\\decisions-YYYY-MM-DD.tsv"]);
Tbl(["Column", "Meaning"], [
  ["Time", "When the message was judged"],
  ["Action", "`KEPT`, `REMOVED:TRASH` (or `:SPAM`, `:ARCHIVE`), `WOULD-REMOVE` (preview / dry run), `TRAINED:…`, `RESTORED`"],
  ["Score", "The total points (6 or more = junk)"],
  ["Category", "The junk category that contributed most, e.g. \"Loans & credit\""],
  ["FromName / FromAddress / Subject", "Who sent it and what it was"],
  ["Reasons", "Every point awarded, e.g. `+5 Loans & credit: subject \"Debt Relief\"; +3 List-Unsubscribe`"],
  ["MessageId", "Gmail's internal id for the message"],
], [2600, 6760]);
P("Tip: in Excel, sort by **Score** to see close calls, or filter **Action** to `REMOVED:TRASH` to review everything that was removed.");

H2("1.8 Simple changes you can make");
P("Most people never need these. All the files are in `C:\\ProgramData\\Gplus` and are plain text (open them with Notepad **as administrator**). Changes take effect within 5 minutes; there's no need to restart anything.");
Tbl(["I want to…", "Do this"], [
  ["Always keep mail from a sender", "Put a message from them in **Gplus-NotJunk**, or reply to them once, or add them to `AllowSenders` in `rules.json`"],
  ["Always remove mail from a sender", "Put a message from them in **Gplus-Train**, or add them to `BlockSenders` in `rules.json`"],
  ["Stop removing a whole kind of mail (e.g. weather)", "In `rules.json`, find that category and set `\"Enabled\": false`"],
  ["Make it more or less aggressive", "Change `Threshold` in `rules.json` (default 6; lower = more aggressive)"],
  ["Send junk to Spam instead of Trash", "Set `\"Action\": \"Spam\"` in `appsettings.json` (this also trains Gmail's own filter)"],
  ["Only see what it would do, without removing anything", "Set `\"DryRun\": true` in `appsettings.json`"],
], [3600, 5760]);
P("Entries in `AllowSenders` and `BlockSenders` can be a full address (`mom@example.com`), a wildcard (`*@example.com`) or a bare domain (`example.com`, which also covers `mail.example.com`).");

H2("1.9 Questions and answers");
Tbl(["Question", "Answer"], [
  ["Does my email leave my computer?", "No. Gplus runs only on your PC and talks only to Google's Gmail service. Nothing is sent to the developer or anyone else."],
  ["Can it delete mail permanently or send email?", "No. Its Google permission can read, label and move mail to Trash or Spam, but it cannot send mail or permanently delete anything."],
  ["Does the PC have to be on?", "Yes. Gplus checks your mail only while the PC is running. When it starts again it catches up on unread mail from the last 30 days."],
  ["What if I read a junk message before Gplus gets to it?", "Gplus only checks unread mail, so it leaves that message alone. Put it in Gplus-Train if you want it removed and the sender blocked."],
  ["Can I undo a block?", "Yes: put one of that sender's messages in Gplus-NotJunk. It removes any learned block that matched and allows the sender."],
  ["How do I turn it off?", "See 2.15 (pause or uninstall)."],
], [3300, 6060]);

// =====================================================================================
// PART 2 — STEP-BY-STEP SETUP
// =====================================================================================
H1("Part 2 — Step-by-Step Setup");
P("Follow these sections in order the first time. Setup takes about 30 minutes, and the Google part only has to be done once. Screens on Google's websites change from time to time; if a button has moved, look for the same words nearby.");

H2("2.1 What you need");
Bullets([
  "A Windows 10 or 11 PC (64-bit) that stays on most of the day, with an **administrator** account.",
  "The Gmail account you want cleaned (for example you@gmail.com) and its password.",
  "A web browser (Chrome or Edge).",
  "The file `Gplus.exe` (see 2.10 if you need to build it).",
  "Two public web pages, a home page and a privacy policy, which Google requires before it lets your app stay signed in permanently. 2.6 explains a free way to host them.",
]);
Note("Important", "Google no longer lets programs sign in to Gmail with your password. Instead you create your own free \"app\" in Google Cloud, and Gmail asks you once to give it permission. That is what sections 2.2 to 2.9 set up.");

H2("2.2 Create a Google Cloud project");
Steps([
  "Go to **https://console.cloud.google.com** and sign in with the Gmail account you want Gplus to clean.",
  { t: "If Google asks you to agree to the Google Cloud terms of service, choose your country, read them and click [[Agree and continue]]. No payment details are needed; everything Gplus uses is free." },
  "At the top of the page, click the **project picker** (it shows the current project name, or \"Select a project\").",
  "In the window that opens, click [[New project]] (top right).",
  { t: "For **Project name**, type `Gplus`. Leave **Location** as \"No organization\". Click [[Create]]." },
  { t: "Wait a few seconds for the notification bell to show the project is ready, then open the project picker again and select **Gplus**.",
    note: ["Tip", "Always check the project name at the top of the page says **Gplus** before you change anything. Settings made in another project won't apply."] },
]);

H2("2.3 Turn on the Gmail API");
Steps([
  "Click the **☰ menu** (top left) → [[APIs & Services]] → [[Library]].",
  "In the search box type `Gmail API` and press Enter.",
  "Click the **Gmail API** result (by Google Enterprise API).",
  { t: "Click [[Enable]]. After a few seconds the page changes to show the API's details and a [[Disable API]] button, which means it's on.",
    note: ["Important", "If you skip this, Gplus signs in but then fails with **\"Gmail API has not been used in project … or it is disabled\"**."] },
]);

H2("2.4 Set up the sign-in screen (Google Auth Platform)");
Steps([
  "Click the **☰ menu** → [[Google Auth Platform]]. (It may also be listed as APIs & Services → OAuth consent screen.)",
  "If you see \"Google Auth Platform not configured yet\", click [[Get started]].",
  { t: "**App information:**", sub: ["App name: `Gplus`", "User support email: choose your Gmail address", "Click [[Next]]"] },
  { t: "**Audience:** choose [[External]], then [[Next]].",
    sub: ["\"Internal\" is only available to Google Workspace organizations; ordinary Gmail accounts must use External."] },
  { t: "**Contact information:** type your email address, then [[Next]]." },
  { t: "**Finish:** tick the box agreeing to the Google API Services User Data Policy, click [[Continue]], then [[Create]]." },
]);

H2("2.5 Create the app's login file (OAuth client)");
Steps([
  "In Google Auth Platform, click [[Clients]] in the left menu.",
  "Click [[+ Create client]].",
  { t: "For **Application type**, choose [[Desktop app]]." },
  { t: "For **Name**, type `Gplus desktop` (any name works). Click [[Create]]." },
  { t: "A window shows the new client. Click [[Download JSON]]. Your browser saves a file named like `client_secret_123456789-abc….apps.googleusercontent.com.json` to your Downloads folder.",
    note: ["Important", "Treat this file like a key. Don't email it or post it online. Gplus copies it to a protected folder when you sign in (2.11), after which you can keep it somewhere safe or delete it."] },
  "Click [[OK]].",
]);

H2("2.6 Home page and privacy policy (needed to publish)");
P("To let Gplus stay signed in permanently, the app must be **published** (2.8), and Google only allows that once the Branding page has a home page and a privacy policy link. They only need to exist; Google doesn't review them for a personal app.");
P("If you don't have a website, a free GitHub Pages site works. The pages used for this installation are at:");
Bullets([
  "Home page: `https://muleskin.github.io/gplus-site/`",
  "Privacy policy: `https://muleskin.github.io/gplus-site/privacy.html`",
]);
P("To make your own, create a free GitHub account, create a **public** repository (for example `gplus-site`) containing an `index.html` and a `privacy.html` (copy the ones from `muleskin/gplus-site` and change the contact address), then in the repository go to **Settings → Pages**, set **Source: Deploy from a branch**, **Branch: main**, **/ (root)**, and click [[Save]]. After a minute or two the site is live at `https://YOUR-NAME.github.io/gplus-site/`.");

H2("2.7 Fill in the Branding page");
Steps([
  "In Google Auth Platform, click [[Branding]].",
  "Check that **App name** (`Gplus`) and **User support email** are filled in.",
  { t: "**Leave App logo empty.**",
    note: ["Important", "Uploading a logo forces Google's **brand verification** before the app can be published. If a logo is there, click [[Remove]] under it and confirm."] },
  { t: "Under **App domain**, fill in:", sub: [
    "Application home page: e.g. `https://muleskin.github.io/gplus-site/`",
    "Application privacy policy link: e.g. `https://muleskin.github.io/gplus-site/privacy.html`",
    "Application terms of service link: leave blank",
  ] },
  { t: "Under **Authorized domains**, click [[+ Add domain]] and type the domain of those pages, e.g. `muleskin.github.io`." },
  "Under **Developer contact information**, make sure an email address is listed.",
  "Click [[Save]] at the bottom. You should see \"Branding changes saved\".",
]);

H2("2.8 Publish the app");
Steps([
  "In Google Auth Platform, click [[Audience]].",
  { t: "Under **Publishing status: Testing**, click [[Publish app]].",
    note: ["Note", "If the button is grey, hover over it: Google shows what's missing. It's almost always the home page or privacy policy link (2.7), or a logo that needs removing."] },
  { t: "Read the **Push to production?** message and click [[Confirm]]. The status changes to **In production**." },
]);
Note("Important", "Why publish? While an app is in **Testing**, Google signs it out every **7 days**, and only people listed as \"test users\" can sign in at all. You do **not** need to submit anything for verification: an unverified app can be used by its owner (up to 100 users), and you'll simply see a warning screen once when signing in (2.11).");
P("**Alternative (temporary):** if you can't publish yet, stay in Testing and, on the Audience page under **Test users**, click [[+ Add users]], enter your Gmail address and click [[Save]]. Sign-in will work, but you'll need to repeat 2.11 every 7 days.");

H2("2.9 Google setup checklist");
Tbl(["Check", "Where", "Should show"], [
  ["Correct project", "Project picker, top of the page", "Gplus"],
  ["Gmail API on", "APIs & Services → Enabled APIs & services", "Gmail API listed"],
  ["Branding complete", "Google Auth Platform → Branding", "Name, support email, home page, privacy policy, authorized domain; **no logo**"],
  ["Published", "Google Auth Platform → Audience", "Publishing status: **In production**"],
  ["Desktop client", "Google Auth Platform → Clients", "A client of type Desktop; JSON downloaded"],
], [2000, 3700, 3660]);

H2("2.10 Put Gplus on the PC");
P("Gplus is a single file, `Gplus.exe` (about 37 MB). It doesn't need .NET or anything else installed.");
Steps([
  { t: "If you only have the source code, build the exe (needs the .NET 10 SDK):", code: [
    "cd C:\\path\\to\\Gplus",
    "dotnet publish src/Gplus -p:PublishProfile=SingleExe",
  ], sub: ["The result is `publish\\Gplus.exe`."] },
  "Create the folder `C:\\Program Files\\Gplus`.",
  "Copy `Gplus.exe` into it.",
]);

H2("2.11 Sign in to Gmail (authorize)");
Steps([
  { t: "Open an **administrator** command prompt: press the Windows key, type `cmd`, right-click **Command Prompt** and choose [[Run as administrator]], then click [[Yes]]." },
  { t: "Run (use the real name of your downloaded file):", code: [
    "\"C:\\Program Files\\Gplus\\Gplus.exe\" authorize \"%USERPROFILE%\\Downloads\\client_secret_….json\"",
  ] },
  "Your browser opens a Google sign-in page. Choose the **Gmail account you want cleaned**. (If several accounts are signed in, pick carefully.)",
  { t: "You'll see **\"Google hasn't verified this app\"**. This is expected for your own personal app. Click [[Advanced]], then [[Go to Gplus (unsafe)]]." },
  { t: "Google shows what Gplus is asking for: permission to read and change (but not delete) your Gmail. Tick the permission box if there is one, then click [[Continue]]." },
  { t: "The browser says the authentication is complete; you can close that tab. The command prompt shows:", code: [
    "Authorized: you@gmail.com",
    "Login saved (encrypted) under C:\\ProgramData\\Gplus\\token",
  ] },
]);
Note("Tip", "If you see **\"Access blocked: Gplus has not completed the Google verification process\" (Error 403: access_denied)**, the app is still in Testing and your address isn't a test user, or you used a login file from a different project. Recheck 2.8 and 2.9.");

H2("2.12 Preview, then install the service");
Steps([
  { t: "Preview what Gplus would remove. Nothing is changed:", code: ["\"C:\\Program Files\\Gplus\\Gplus.exe\" scan"],
    sub: [
      "The first run reads up to 2,000 of your sent messages to learn who you write to. This takes about 5 to 10 minutes, and only happens once.",
      "Each unread message is listed as **JUNK** (red, with reasons) or **keep**.",
      "Add `--why` to see the reasons for kept mail too.",
    ] },
  { t: "If mail arrives through other addresses or your own domain (forwarded into Gmail), tell Gplus they're you. Create `C:\\ProgramData\\Gplus\\appsettings.json` containing:", code: [
    "{ \"Gplus\": { \"MyAddresses\": [ \"yourdomain.com\", \"old.address@example.com\" ] } }",
  ] },
  { t: "When you're happy with the preview, install the service (still in the administrator prompt):", code: [
    "\"C:\\Program Files\\Gplus\\Gplus.exe\" install",
  ], sub: ["It installs **Gplus Gmail Junk Remover** (starts automatically with Windows, restarts itself if it crashes) and starts it straight away."] },
  { t: "Check it's running:", code: ["\"C:\\Program Files\\Gplus\\Gplus.exe\" status"], sub: ["Look for `STATE : 4 RUNNING`."] },
]);

H2("2.13 Gmail steps: labels, rescuing and training");
P("On its first run the service creates three labels in your Gmail: **Gplus-Removed**, **Gplus-Train** and **Gplus-NotJunk**. You'll find them in Gmail's left-hand list (click **More** if they're hidden).");

H3("Find removed mail");
Steps([
  "In Gmail, click in the search box at the top.",
  { t: "Type `label:Gplus-Removed` and press Enter. To see only what's in Trash, use `in:trash label:Gplus-Removed`." },
]);

H3("Rescue a message: Gmail website (computer)");
Steps([
  "Open the message (or tick its checkbox in the list). This works in Trash and Spam too.",
  { t: "Click the **Labels** icon (a tag shape) in the toolbar above the message." },
  { t: "Tick [[Gplus-NotJunk]] and click [[Apply]]." },
  "Within about 5 minutes the message is back in your inbox and the sender is always allowed.",
]);

H3("Rescue a message: Gmail app (Android or iPhone)");
Steps([
  "Open the message.",
  { t: "Tap the **⋮** (Android) or **…** (iPhone) menu at the top right." },
  { t: "Tap [[Change labels]] (Android) or [[Label]] (iPhone)." },
  { t: "Tick [[Gplus-NotJunk]] and tap [[OK]] or the checkmark." },
]);

H3("Teach Gplus about junk it missed: Gmail website");
Steps([
  "Open the junk message, or tick it in the list.",
  { t: "Click the **Move to** icon (a folder with an arrow) and choose [[Gplus-Train]].",
    sub: ["Or use the Labels icon and tick Gplus-Train. Either works."] },
  "Within about 5 minutes the message is removed and the sender blocked (see 1.5 for how much is blocked).",
]);

H3("Teach Gplus about junk it missed: Gmail app");
Steps([
  "Open the message.",
  { t: "Tap **⋮** / **…** → [[Move to]] (Android) or [[Move to]] / [[Label]] (iPhone)." },
  { t: "Choose [[Gplus-Train]]." },
]);
Note("Note", "The labels are processed only while the service is running (not in dry-run mode). Changing your mind is fine: moving a message from one training label to the other re-files it on the other side.");


H2("2.14 Confirm it's working");
Tbl(["Check", "How", "Expected"], [
  ["Service running", "`Gplus.exe status`, or Windows **Services** app", "Gplus Gmail Junk Remover: Running"],
  ["Activity log", "Open `C:\\ProgramData\\Gplus\\logs\\gplus-YYYY-MM-DD.log`", "\"Connected to Gmail as …\", then \"Sweep: … removed\" lines"],
  ["Decisions", "Open today's `decisions-….tsv` in Excel", "One row per unread message with its score and reasons"],
  ["Gmail labels", "Gmail left-hand list", "Gplus-Removed, Gplus-Train, Gplus-NotJunk"],
  ["Word filter progress", "`Gplus.exe words`", "Counts of junk/good examples; starts scoring at 30 of each"],
], [2000, 4200, 3160]);

H2("2.15 Pause, update or uninstall");
H3("Pause without uninstalling");
P("Either stop the service (`Gplus.exe stop`, administrator prompt), or keep it running but make it harmless by setting `\"DryRun\": true` in `C:\\ProgramData\\Gplus\\appsettings.json`.");
H3("Update to a new version");
P("From an administrator prompt in the source folder, after building:");
Code(["dotnet publish src/Gplus -p:PublishProfile=SingleExe", "powershell -ExecutionPolicy Bypass -File tools\\deploy-service.ps1"]);
P("The deploy script stops the service, copies the new exe to `C:\\Program Files\\Gplus`, restarts it, and logs what it did to `C:\\ProgramData\\Gplus\\logs\\deploy.log`. Your settings, rules, login and learning are kept.");
H3("Uninstall completely");
Steps([
  { t: "Administrator prompt:", code: ["\"C:\\Program Files\\Gplus\\Gplus.exe\" uninstall"] },
  "Delete the folder `C:\\Program Files\\Gplus`.",
  "Delete `C:\\ProgramData\\Gplus` (this removes the saved login, rules, learning and logs).",
  { t: "Remove Gplus's access to your Google account: go to **https://myaccount.google.com/permissions** (Google Account → Security → Your connections to third-party apps & services), click **Gplus**, then [[Delete all connections you have with Gplus]] and confirm." },
  "Optional: in Gmail, delete the labels Gplus-Removed, Gplus-Train and Gplus-NotJunk (left-hand list → the label's ⋮ menu → Remove label). Messages keep existing; only the labels go.",
  "Optional: delete the Google Cloud project (console → IAM & Admin → Settings → [[Shut down]]).",
]);

H2("2.16 Troubleshooting");
Tbl(["Symptom", "Cause", "Fix"], [
  ["\"Access blocked … has not completed the Google verification process\" (403)", "App is in Testing and you're not a test user, or the login file is from another project", "Publish the app (2.8) or add yourself as a test user; use the JSON from the right project"],
  ["Publish app button is grey", "Branding is missing a home page / privacy policy, or has a logo", "Complete 2.7; remove the logo; hover the button to see what's missing"],
  ["\"Gmail API has not been used in project … or it is disabled\"", "Gmail API not enabled in this project", "Do 2.3, wait a minute, try again"],
  ["\"Quota exceeded … Units per minute per user\"", "Too many Gmail requests at once (usually the first run)", "Nothing; Gplus paces itself and retries automatically. Wait a few minutes"],
  ["Log says \"Google rejected the saved login … Run Gplus authorize again\"", "Login expired (Testing mode, 7 days) or access was removed", "Publish the app (2.8), then repeat 2.11"],
  ["Log says \"No OAuth client file\" or \"Gmail is not authorized yet\"", "2.11 hasn't been done on this PC", "Do 2.11 from an administrator prompt"],
  ["Nothing is being removed", "`DryRun` is on, the mail is read/older than 30 days, or the sender is someone you've emailed", "Check `appsettings.json`; check the decisions log for that message's reasons"],
  ["Good mail was removed", "It scored 6 or more", "Put it in Gplus-NotJunk (2.13); the sender is allowed from then on"],
  ["\"rules.json has an error, still using the previous rules\"", "A typo in a hand edit", "Fix the file (check commas and quotes); the old rules keep running meanwhile"],
  ["Rebuilding fails because Gplus.exe is in use", "The service runs from that exe", "Use `tools\\deploy-service.ps1` (2.15), which stops the service first"],
], [3000, 3000, 3360]);

// =====================================================================================
// PART 3 — TECHNICAL GUIDE
// =====================================================================================
H1("Part 3 — Technical Guide");
H2("3.1 Overview");
Tbl(["Item", "Detail"], [
  ["Platform", ".NET 10 (net10.0-windows) Worker Service, hosted as a Windows service via Microsoft.Extensions.Hosting.WindowsServices"],
  ["Gmail access", "Gmail API v1 (Google.Apis.Gmail.v1), OAuth 2.0 installed-app flow, scope `gmail.modify` only"],
  ["Service", "Name `Gplus`, display name \"Gplus Gmail Junk Remover\", LocalSystem, delayed auto-start, restart on failure (3 × 60 s, reset daily)"],
  ["Binary", "Single self-contained, compressed exe (~37 MB); default settings and rules embedded as resources"],
  ["Data", "`%ProgramData%\\Gplus` (override with the `GPLUS_DATA` environment variable)"],
  ["Source", "github.com/muleskin/Gplus (private); 4 test suites, xUnit"],
], [2000, 7360]);

H2("3.2 Components");
Tbl(["File", "Responsibility"], [
  ["`Program.cs`", "CLI dispatch (authorize, scan, test, words, install…), host builder, configuration layering"],
  ["`Worker.cs`", "Service loop: sweep → wait `PollIntervalSeconds`; exponential backoff (30 s … 30 min) on failure; daily log pruning"],
  ["`Sweeper.cs`", "One sweep: connect, reload rules, refresh correspondents, process training labels, harvest word examples, list → fetch → classify → act → log"],
  ["`Filtering/JunkClassifier.cs`", "Hard gates and point scoring; brand-spoof, authentication and bulk-mail detection"],
  ["`Filtering/RuleSet.cs`", "`rules.json` model; compiles every regex at load (200 ms match timeout)"],
  ["`Filtering/SenderLearner.cs`", "Decides whether a Gplus-Train lesson blocks a domain, one address, or nothing"],
  ["`Filtering/WordFilter.cs`", "Robinson / chi-squared (SpamBayes-style) token filter"],
  ["`Mail/GmailMapper.cs`, `TextTools.cs`, `Addresses.cs`", "Gmail message → `MailItem`; charset decoding, HTML to text, evasion-resistant normalization, address parsing"],
  ["`Gmail/GmailConnector.cs`", "OAuth flows, request pacing and retry/backoff"],
  ["`Gmail/DpapiDataStore.cs`", "Token store encrypted with Windows DPAPI"],
  ["`State/*`", "Correspondents, seen ids, learned senders, decision log (all JSON/TSV, atomic writes)"],
  ["`ServiceInstaller.cs`", "`sc.exe` wrapper for install / uninstall / start / stop / status"],
  ["`tools/deploy-service.ps1`", "Stop → copy → set quoted ImagePath → start; transcript to `deploy.log`"],
  ["`tools/make-logo.ps1`", "Renders the G+ icon set (ico + png) from vectors"],
], [3300, 6060]);

H2("3.3 What happens in one sweep");
Steps([
  "**Connect** (first sweep, or after an error): load the refresh token, build the Gmail client, read the profile address and send-as aliases.",
  "**Reload rules** if `rules.json` or `learned.json` changed. A changed rule set clears the seen-list so recent unread mail is re-judged. A file with an error is logged and the previous rules keep running.",
  "**Refresh correspondents** when older than `CorrespondentRefreshHours` (12): first run reads up to 2,000 sent messages (To/Cc/Bcc headers only), later runs only newer ones. Paced to about 8 requests/s; progress is saved every 100 messages and resumes after interruption.",
  "**Training labels** (when applying and `LearnFromLabels`): every message in Gplus-Train is learned as junk, its sender block decided by SenderLearner, then removed; every message in Gplus-NotJunk is learned as good, matching learned blocks are dropped, the sender allowed, and the message untrashed and returned to the inbox. Each lesson is saved before Gmail is modified, so an interrupted sweep simply repeats it.",
  "**Harvest word examples** (when applying and `WordFilter`): up to `WordFilterLearnPerSweep` (40) settled messages, split between junk and good (see 3.6).",
  "**List candidates** with `Query` (`is:unread in:inbox newer_than:30d`), up to `MaxPerPass` (250). The service skips ids in the seen-list (35-day expiry); `scan` never uses it.",
  "**Fetch** each message in full format and map it to a `MailItem`: headers, labels, plain text (or HTML converted to text, hidden blocks dropped), link count, first 30,000 characters.",
  "**Classify** (3.4) and **act**: add the `Gplus-Removed` label and remove `INBOX`; then Trash (default), add `SPAM`, or leave archived, per `Action`.",
  "**Log** one TSV row per message and remember the id as seen.",
]);

H2("3.4 Classification");
H3("Hard gates (checked in this order)");
Tbl(["#", "Gate", "Result"], [
  ["1", "Sender matches `AllowSenders` (hand-written + learned)", "Keep"],
  ["2", "Message is starred", "Keep"],
  ["3", "Sender matches `BlockSenders` (hand-written + learned)", "Junk (score = threshold)"],
  ["4", "Sender is you (Gmail address, send-as aliases, `MyAddresses`)", "Keep"],
  ["5", "Sender is a learned correspondent", "Keep"],
  ["6", "Otherwise", "Score; junk when score ≥ `Threshold` (6)"],
], [600, 6200, 2560]);

H3("Categories");
P("Each enabled category contributes: **SenderWeight** when the sending domain is in its `SenderDomains`; **SubjectWeight** once when any subject pattern matches the subject plus display name; **BodyWeight** per distinct body pattern matched, up to `MaxBodyHits`. Patterns are case-insensitive .NET regular expressions applied to normalized text.");
Tbl(["Category", "Subject", "Body", "Sender", "On"], [
  ["Loans & credit", "5", "2", "8", "yes"],
  ["Marketing", "3", "1", "–", "yes"],
  ["Temu & bargain shopping", "6", "2", "10", "yes"],
  ["Horoscopes & psychics", "6", "2", "10", "yes"],
  ["Weather reports", "5", "1", "8", "yes"],
  ["Crime reports", "5", "1", "8", "yes"],
  ["Local news digests", "4", "1", "7", "yes"],
  ["Prizes & giveaways", "5", "2", "–", "yes"],
  ["Insurance, warranty & home offers", "5", "1", "–", "yes"],
  ["Miracle health & pills", "5", "2", "–", "yes"],
  ["Crypto & get-rich schemes", "5", "2", "–", "yes"],
  ["Political fundraising", "4", "3", "–", "yes"],
  ["Dating & adult", "6", "2", "–", "yes"],
  ["Scams & phishing", "5", "2", "–", "yes"],
  ["Surveys & feedback nags", "3", "1", "–", "yes"],
  ["Social notifications", "3", "0", "7", "no"],
], [4260, 1200, 1200, 1500, 1200]);

H3("Structural signals (weights in rules.json → Signals)");
Tbl(["Signal", "Default", "Detection"], [
  ["ListUnsubscribe", "+3", "`List-Unsubscribe` header present"],
  ["PrecedenceBulk", "+1", "`Precedence: bulk | list | junk`"],
  ["BulkMailer", "+1", "ESP headers (X-SG-EID, X-MC-User, X-SFMC, X-Emarsys, X-rpcampaign, …), X-Mailer names, or sender on an ESP domain. X-Mailgun-* is deliberately ignored: Mailgun also relays forwarded personal mail"],
  ["GmailPromotions / Social / Updates / Forums", "+3 / 0 / 0 / 0", "Gmail category labels"],
  ["GmailImportant", "−2", "`IMPORTANT` label"],
  ["AuthFail", "+4", "`dmarc=fail`, or SPF fail/softfail without `dkim=pass`, in Google's Authentication-Results"],
  ["BrandSpoof", "+5", "Display name is essentially a brand from `Brands` (\"PayPal\", \"Amazon.com Orders\") but the domain isn't one of that brand's; or the display name contains a different domain's address (\"x via List\" excluded)"],
  ["ReplyToMismatch", "+1", "Reply-To registrable domain ≠ From"],
  ["NotAddressedToMe", "+1", "None of To/Cc is you"],
  ["NoReplySender", "+1", "Local part like no-reply, marketing, news, offers, deals, hello…"],
  ["MarketingSubdomain", "+1", "From a subdomain like `e.`, `email.`, `news.`, `m.`, `t.`"],
  ["PreheaderPadding", "+1", "≥ 10 invisible padding characters (U+034F, ZWNJ, soft hyphen…) in the first 4,000 characters"],
  ["ShoutySubject", "+1", ">60% capitals, `!!`, `$$`, or 2+ emoji"],
  ["ManyLinks / ImageOnly", "+1 / +1", "≥ 15 links / links with < 120 characters of text"],
  ["WordsSpam / WordsLikelySpam / WordsHam", "+5 / +3 / −3", "Word-filter probability ≥ 0.99 / ≥ 0.90 / ≤ 0.10"],
  ["Transactional", "−6", "Subject matches receipts, codes, password resets, shipping, statements, payments, appointments, flights, shares (22 patterns)"],
], [2700, 1300, 5360]);

H3("Text normalization");
P("Before matching, subject, display name and body go through NFKC normalization (folds 𝐛𝐨𝐥𝐝, ＦＵＬＬＷＩＤＴＨ and circled letters), canonical decomposition with removal of combining marks (accents) and all Unicode *format* characters (zero-width spaces and joiners, soft hyphens, BOM), and whitespace collapsing. HTML bodies have `script`/`style`/`head` removed and blocks styled `display:none`, `font-size:0` or `visibility:hidden` dropped before tags are stripped.");

H2("3.5 Learning from the training labels");
P("`SenderLearner.BlockEntryFor` returns the entry to add to `learned.json` → `Block`:");
Tbl(["Condition (first match wins)", "Entry learned"], [
  ["No address; sender is you; a correspondent; or on hand-written `AllowSenders`", "None (message still removed)"],
  ["Personal-mail provider (≈50 domains plus yahoo/hotmail/aol/gmx/yandex families)", "The address"],
  ["Bulk-mail platform domain (robly.com, sendgrid.net, mcsv.net, …)", "The address"],
  ["Domain belongs to a brand in `Brands`", "The address"],
  ["A correspondent works at that domain", "The address"],
  ["Otherwise", "The whole domain (covers subdomains)"],
], [6000, 3360]);
P("Gplus-NotJunk removes every learned block matching the address and adds the address to `learned.json` → `Allow`. Gplus-Train likewise removes any learned allow matching the sender. Learned entries merge into the rule set at load and never modify `rules.json`. Each entry records the date and an example (\"from — subject\"), and can be deleted by hand.");

H2("3.6 Word filter");
Bullets([
  "**Tokens:** sender domain and its registrable root (`from:`), display-name words (`n:`), subject words and two-word phrases (`s:`), body words and two-word phrases (first 8,000 characters). Words are 2–24 characters; tokens with 5+ digits (order numbers, phone numbers) are dropped; at most 2,000 distinct tokens per message.",
  "**Token probability:** Robinson's smoothed estimate f(w) = (s·x + n·p(w)) / (s + n), with s = 0.45, x = 0.5, where p(w) compares the token's junk and good frequencies and n is its total count.",
  "**Message probability:** the up-to-150 most decisive tokens (|f − 0.5| ≥ 0.1) are combined with Fisher's chi-squared method: S = 1 − C⁻¹(−2 Σ ln(1−f), 2n), H = 1 − C⁻¹(−2 Σ ln f, 2n), P = (S − H + 1) / 2.",
  "**Readiness:** no points until `WordFilterMinSpam` (30) junk and `WordFilterMinHam` (30) good examples.",
  "**Automatic examples:** junk = `label:Gplus-Removed -in:inbox older_than:2d` or `in:spam`; good = `(has:userlabels OR is:starred)` excluding sent, chats, drafts, spam, trash and the Gplus labels. Newest first, up to 40 per sweep split evenly, 5,000 per side maximum, 10 pages of lookback.",
  "**No feedback loop:** a message removed only because of word points (score minus word points below threshold) is recorded in `NoHarvest` and never harvested as junk.",
  "**Retraining:** each message id is stored with its side; learning it the other way subtracts the old counts first. Rarely-seen tokens are pruned when the vocabulary passes 300,000.",
]);

H2("3.7 Gmail API usage");
Tbl(["Call", "Used for"], [
  ["`users.getProfile`, `users.settings.sendAs.list`", "Who \"me\" is"],
  ["`users.messages.list`", "Candidates (query), training labels, harvesting, sent mail"],
  ["`users.messages.get` (full / metadata)", "Classification and learning / correspondent headers"],
  ["`users.messages.modify`", "Add Gplus-Removed / SPAM, remove INBOX and training labels, restore to INBOX"],
  ["`users.messages.trash` / `untrash`", "Remove to Trash / rescue from Trash"],
  ["`users.labels.list` / `create`", "Create Gplus-Removed, Gplus-Train, Gplus-NotJunk on first use"],
], [4000, 5360]);
P("**Quota protection:** a global pacer spaces every request at least 40 ms apart (≤ 25/s, well under the per-user limit of 250 units/s, where a message fetch costs 5). A backoff handler retries HTTP 429, 5xx and 403 responses whose body says *rateLimitExceeded* or *Quota exceeded*: exponential from 1 s, up to 8 retries, capped at 64 s. The client's `NumTries` is raised to 9, because the default of 3 would end backoff after about 3 seconds while Gmail's quota windows are per minute.");

H2("3.8 Security");
Bullets([
  "**Least privilege:** only `https://www.googleapis.com/auth/gmail.modify`. It cannot send mail or permanently delete messages.",
  "**Token at rest:** the OAuth refresh token is stored in `%ProgramData%\\Gplus\\token` encrypted with DPAPI (LocalMachine scope, application entropy), so the interactive sign-in and the LocalSystem service share it but the file is useless on another PC. Writes are atomic (temp file + rename).",
  "**Folder permissions:** on first creation `%ProgramData%\\Gplus` is set to SYSTEM, Administrators and the creating user only, with inheritance removed.",
  "**No outbound data:** processing is local; the only network peer is Google's API. The client secret file is excluded from source control.",
  "**Service path:** registered with a quoted ImagePath (`\"C:\\Program Files\\Gplus\\Gplus.exe\"`) to avoid the unquoted-service-path hijack. The deploy script verifies the quotes.",
  "**Untrusted input:** all regexes have a 200 ms timeout; malformed headers fall back to tolerant parsing; message bodies are capped.",
]);

H2("3.9 Files");
Tbl(["File (in %ProgramData%\\Gplus)", "Format", "Written by", "Notes"], [
  ["`client_secret.json`", "Google JSON", "authorize", "Copied from your download"],
  ["`token\\*.bin`", "DPAPI blob", "authorize, service", "Encrypted refresh/access token"],
  ["`rules.json`", "JSON with comments", "You", "Created from the embedded defaults on first run; hot-reloaded"],
  ["`learned.json`", "JSON", "Service", "`Block` / `Allow` lists: sender, added, example"],
  ["`words.json`", "JSON", "Service", "Word filter counts; delete to relearn"],
  ["`correspondents.json`", "JSON", "Service", "Normalized addresses, last refresh, resume point"],
  ["`seen.json`", "JSON", "Service", "Message id → expiry (35 days)"],
  ["`appsettings.json`", "JSON with comments", "You (optional)", "Overrides the embedded settings; hot-reloaded"],
  ["`logs\\decisions-YYYY-MM-DD.tsv`", "TSV (UTF-8 BOM)", "Service, scan", "One row per decision; pruned after `LogRetentionDays`"],
  ["`logs\\gplus-YYYY-MM-DD.log`", "Text", "Service, scan", "Activity; errors also go to the Windows Event Log (source Gplus)"],
  ["`logs\\deploy.log`", "Text", "deploy-service.ps1", "Deployment transcript"],
], [3000, 1700, 1700, 2960]);

H2("3.10 Configuration reference");
P("Settings are layered, later overriding earlier: embedded defaults → `appsettings.json` beside the exe (if present) → `%ProgramData%\\Gplus\\appsettings.json`. All live under the `\"Gplus\"` section.");
Tbl(["Setting", "Default", "Meaning"], [
  ["Action", "Trash", "Trash | Spam | Archive"],
  ["RemovedLabel", "Gplus-Removed", "Label added to every removed message"],
  ["DryRun", "false", "Log only; training labels ignored"],
  ["PollIntervalSeconds", "300", "Seconds between sweeps (minimum 30)"],
  ["Query", "is:unread in:inbox newer_than:30d", "Gmail search for candidates"],
  ["MaxPerPass", "250", "Most messages examined per sweep"],
  ["MyAddresses", "[\"doctorate.cc\"]", "Extra addresses or whole domains that are \"you\""],
  ["LearnCorrespondents", "true", "Protect everyone you've emailed"],
  ["CorrespondentRefreshHours", "12", "How often to read new sent mail"],
  ["CorrespondentScanMax", "2000", "Sent messages read on the first build"],
  ["LearnFromLabels", "true", "Process Gplus-Train / Gplus-NotJunk"],
  ["TrainLabel / NotJunkLabel", "Gplus-Train / Gplus-NotJunk", "Training label names"],
  ["WordFilter", "true", "Learn and score words and phrases"],
  ["WordFilterMinSpam / MinHam", "30 / 30", "Examples needed before scoring"],
  ["WordFilterLearnPerSweep", "40", "Automatic examples per sweep"],
  ["WordFilterMaxPerSide", "5000", "Stop auto-harvesting a side at this many"],
  ["WordFilterConfirmDays", "2", "Days removed mail must go unrescued to count as junk"],
  ["LogRetentionDays", "60", "Days of logs kept"],
], [3000, 2700, 3660]);
P("`rules.json` top-level keys: `Threshold`, `AllowSenders`, `BlockSenders`, `Signals`, `Brands` (brand → its real domains), `Categories` (Name, Enabled, SubjectWeight, BodyWeight, MaxBodyHits, SenderWeight, SenderDomains, Patterns, Subject, Body), `Transactional` (Weight, Patterns).");

H2("3.11 Command reference");
Tbl(["Command", "Purpose"], [
  ["`Gplus authorize <client_secret.json>`", "Interactive Google sign-in; copies the client file and stores the encrypted token"],
  ["`Gplus scan [--apply] [--max N] [--query \"…\"] [--junk-only] [--why]`", "Run one sweep in the console. Preview unless `--apply`; never uses the seen-list"],
  ["`Gplus test <messages.json> [rules.json] [--why]`", "Classify sample messages offline; with `\"expect\"` fields, reports misses"],
  ["`Gplus words [--top N]`", "Show word-filter counts and the most telling words and phrases"],
  ["`Gplus install | uninstall | start | stop | status`", "Manage the Windows service (administrator)"],
  ["`Gplus` (no arguments)", "Run the worker in the console (also how the service starts it)"],
], [4600, 4760]);
Tbl(["Exit code", "Meaning"], [
  ["0", "Success"],
  ["1", "Usage error, or an administrator prompt is required"],
  ["2", "Not authorized (no client file or token)"],
  ["3", "`test`: one or more labelled samples misclassified"],
  ["4", "Gmail API error (message printed)"],
], [1600, 7760]);
P("Test-sample format: a JSON array of `{ \"from\", \"to\", \"replyTo\", \"subject\", \"body\", \"links\", \"labels\", \"headers\", \"expect\" }`, where `expect` is true (junk), false (keep) or omitted.");

H2("3.12 Build, test and deploy");
Code([
  "dotnet build Gplus.slnx",
  "dotnet test tests/Gplus.Tests",
  "dotnet publish src/Gplus -p:PublishProfile=SingleExe      # -> publish\\Gplus.exe",
  "powershell -ExecutionPolicy Bypass -File tools\\deploy-service.ps1   # elevated",
]);
Bullets([
  "The `SingleExe` profile is self-contained win-x64, single file, compressed, untrimmed, no debug symbols.",
  "`appsettings.json` and `rules.default.json` are embedded resources (`Gplus.appsettings.json`, `Gplus.rules.default.json`); the loose copies in `bin` exist only for tests and debugging.",
  "The service runs from `C:\\Program Files\\Gplus`, so rebuilding into `publish\\` never collides with the running exe.",
  "`tools/make-logo.ps1` regenerates `src/Gplus/Assets` (ico with 9 sizes, 256/512 px png); `-Glyph` changes the colour.",
]);

H2("3.13 Extending the rules");
P("To add a category, append an object to `Categories` in `%ProgramData%\\Gplus\\rules.json`, for example:");
Code([
  "{",
  "  \"Name\": \"Timeshares\",",
  "  \"SubjectWeight\": 5, \"BodyWeight\": 2, \"MaxBodyHits\": 3, \"SenderWeight\": 8,",
  "  \"SenderDomains\": [ \"example-timeshare.com\" ],",
  "  \"Patterns\": [ \"\\\\btimeshares?\\\\b\", \"\\\\bvacation ownership\\\\b\" ]",
  "}",
]);
P("In JSON, a regex `\\b` is written `\\\\b`. On its next sweep after the file is saved, the service reloads it and compiles every pattern. A bad pattern is reported in the activity log and the previous rules stay in force. Check a change offline first with `Gplus test samples.json rules.json`.");

// =====================================================================================
const doc = new Document({
  creator: "Gplus",
  title: "Gplus — User, Setup and Technical Guide",
  description: "Gmail junk remover for Windows",
  features: { updateFields: true },
  styles: {
    default: { document: { run: { font: FONT, size: 21, color: "222222" } } },
    paragraphStyles: [
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 36, bold: true, font: FONT, color: ACCENT },
        paragraph: { spacing: { before: 0, after: 240 }, outlineLevel: 0,
          border: { bottom: { style: BorderStyle.SINGLE, size: 8, color: ACCENT, space: 4 } } } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 28, bold: true, font: FONT, color: DARK },
        paragraph: { spacing: { before: 320, after: 140 }, outlineLevel: 1, keepNext: true } },
      { id: "Heading3", name: "Heading 3", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 23, bold: true, font: FONT, color: "3A3C43" },
        paragraph: { spacing: { before: 220, after: 100 }, outlineLevel: 2, keepNext: true } },
      { id: "TOC1", name: "toc 1", basedOn: "Normal", next: "Normal",
        run: { bold: true, size: 22, color: DARK }, paragraph: { spacing: { before: 160, after: 40 } } },
      { id: "TOC2", name: "toc 2", basedOn: "Normal", next: "Normal",
        run: { size: 20 }, paragraph: { indent: { left: 360 }, spacing: { after: 20 } } },
    ],
  },
  numbering: {
    config: [
      { reference: "bullets", levels: [
        { level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 540, hanging: 300 } } } },
        { level: 1, format: LevelFormat.BULLET, text: "–", alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 1080, hanging: 300 } } } },
      ] },
      ...numberingConfigs,
    ],
  },
  sections: [{
    properties: { page: { size: { width: 12240, height: 15840 }, margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 } },
      titlePage: true },
    headers: {
      default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT,
        children: [new TextRun({ text: "Gplus — User, Setup and Technical Guide", size: 17, color: MUTED })] })] }),
      first: new Header({ children: [new Paragraph({ children: [] })] }),
    },
    footers: {
      default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [
        new TextRun({ text: "Page ", size: 17, color: MUTED }),
        new TextRun({ children: [PageNumber.CURRENT], size: 17, color: MUTED }),
        new TextRun({ text: " of ", size: 17, color: MUTED }),
        new TextRun({ children: [PageNumber.TOTAL_PAGES], size: 17, color: MUTED }),
      ] })] }),
      first: new Footer({ children: [new Paragraph({ children: [] })] }),
    },
    children: body,
  }],
});

fs.writeFileSync(OUT + ".headings.json", JSON.stringify(headings, null, 1));
Packer.toBuffer(doc).then((buf) => { fs.writeFileSync(OUT, buf); console.log("wrote", OUT, buf.length, "bytes"); });
