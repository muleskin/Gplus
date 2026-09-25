# Gplus: Gmail junk remover (Windows service)

Sweeps your **unread Gmail inbox** every 5 minutes and removes junk that Gmail lets through:
loan and credit offers, marketing, Temu/Shein, horoscopes and psychics, weather reports, crime
reports, local-news digests, prize scams, warranty and insurance pitches, miracle pills, crypto
schemes, political fundraising, dating spam and phishing.

## Why it does better than Gmail

| Technique | What it catches |
|---|---|
| **Never touches people you write to.** It learns every address in your Sent folder. | Lets the filter be aggressive with no risk to real conversations |
| Category rules (subject, body and sender domain) you can edit in `rules.json` | Everything Gmail treats as legitimate "promotions" |
| Structural tells: `List-Unsubscribe`, bulk-mail platforms, marketing subdomains (`e.brand.com`), invisible preview-text padding, 15+ links | Marketing with no junk words, e.g. "Heritage quilts made for layering" |
| Undoes filter-evasion text: 𝐁𝐨𝐥𝐝 / ＦＵＬＬＷＩＤＴＨ letters, zero-width characters (`L​O​A​N`), accents, text hidden with `display:none` | Spam built to slip past keyword filters |
| Brand-impersonation check: "PayPal" display name sent from a non-PayPal domain; SPF/DKIM/DMARC failures | Phishing |
| Transactional protection: receipts, codes, statements, appointments, shipping and payments subtract points | Keeps the mail you actually need |

Every removed message also gets the label **`Gplus-Removed`**, and by default it goes to
Trash, where Gmail keeps it for 30 days. Every decision (kept or removed) is logged with its
score and reasons.

## One-time setup (about 10 minutes)

Google no longer lets apps sign in with your password, so Gplus uses the official Gmail API
and your own free OAuth client:

1. Go to <https://console.cloud.google.com/> and create a project (for example "Gplus").
2. **APIs & Services → Library → Gmail API → Enable.**
3. **Google Auth Platform → Get started.** Enter an app name ("Gplus") and your email, and
   choose **Audience: External**.
4. **Audience → Publish app** (set it to *In production*).
   > Skip this and Google expires your login every **7 days** ("Testing" mode). You don't
   > need to submit anything for verification; it's only for your own account.
5. **Clients → Create client → Desktop app → Create → Download JSON.**
6. From an **elevated** prompt in the publish folder:
   ```bash
   Gplus.exe authorize C:\path\to\client_secret_xxx.json
   ```
   A browser opens. You'll see *"Google hasn't verified this app"*; click **Advanced → Go to
   Gplus**, then allow access. Gplus only asks for `gmail.modify`, which can read, label,
   trash and report spam, but **cannot send mail or permanently delete anything**.

## Try it before trusting it

```bash
Gplus.exe scan
```
This shows the verdict, score and reasons for each unread inbox message. **Nothing is
changed.** Add `--why` to see reasons for kept mail too, `--junk-only` to hide kept mail, or
`--query "in:inbox newer_than:7d"` to look beyond unread mail.
`Gplus.exe scan --apply` removes the junk once, right now.

## Install as a service

Build the single self-contained exe (no .NET install needed on the target PC):
```bash
dotnet publish src/Gplus -p:PublishProfile=SingleExe
```
That produces one file, `publish\Gplus.exe` (~37 MB). The default settings and rules are built
into it; your editable copies live in `%ProgramData%\Gplus`. Copy the exe somewhere permanent
(for example `C:\Program Files\Gplus\Gplus.exe`), then from an **elevated** prompt:
```bash
"C:\Program Files\Gplus\Gplus.exe" install
```
The service is "Gplus Gmail Junk Remover": delayed auto-start, restarts on failure, runs
as LocalSystem. `Gplus.exe uninstall | start | stop | status` manage it.

**Updating** (or first install) in one step, from an elevated prompt after publishing:
```bash
powershell -ExecutionPolicy Bypass -File tools\deploy-service.ps1
```
It stops the service, copies `publish\Gplus.exe` to `C:\Program Files\Gplus`, points the
service there (quoted path), starts it, and logs to `%ProgramData%\Gplus\logs\deploy.log`.

## Files (`%ProgramData%\Gplus`)

| File | Purpose |
|---|---|
| `rules.json` | Your rules. **Edit freely**; the next sweep reloads them, no restart needed. A broken edit is logged and the previous rules keep running. |
| `learned.json` | Senders learned from the training labels (see below). Delete an entry to undo it. |
| `words.json` | The word filter's memory (see below). Safe to delete; it relearns. |
| `appsettings.json` *(optional)* | Overrides for the settings below |
| `logs\decisions-YYYY-MM-DD.tsv` | One line per judged message. Opens in Excel. |
| `logs\gplus-YYYY-MM-DD.log` | Service activity |
| `token\` | Your Google login, encrypted with Windows DPAPI (useless on another PC) |
| `correspondents.json`, `seen.json` | Learned state (safe to delete) |

### Settings (`appsettings.json`, `"Gplus"` section)

| Key | Default | |
|---|---|---|
| `Action` | `Trash` | `Trash`, `Spam` (also trains Gmail's own filter), or `Archive` (just removes it from the inbox) |
| `DryRun` | `false` | `true` = log only |
| `MyAddresses` | `["doctorate.cc"]` | Addresses or whole domains that forward into this Gmail and count as "you" |
| `PollIntervalSeconds` | `300` | |
| `Query` | `is:unread in:inbox newer_than:30d` | Which mail is considered |
| `LearnCorrespondents` | `true` | Protect everyone you've emailed |
| `LearnFromLabels` | `true` | Learn from the `Gplus-Train` and `Gplus-NotJunk` labels |
| `TrainLabel` / `NotJunkLabel` | `Gplus-Train` / `Gplus-NotJunk` | Names of those labels |
| `WordFilter` | `true` | Learn words and phrases and score mail with them |
| `WordFilterMinSpam` / `WordFilterMinHam` | `30` / `30` | Examples needed before it starts scoring |
| `WordFilterLearnPerSweep` | `40` | Examples gathered automatically per sweep |
| `WordFilterConfirmDays` | `2` | Days removed mail must go unrescued to count as junk |

### Teaching Gplus from Gmail

The first sweep creates two labels in Gmail. Use them like folders ("Move to" or "Label as",
on your phone or the web):

* **`Gplus-Train`**: junk that got through. On the next sweep (within 5 minutes) Gplus blocks the
  sender and removes the message like any other junk. It then re-checks recent unread mail, so
  other messages from that sender go too.
  * Normally it blocks the sender's **whole domain** (for example `deals.somestore.com`).
  * It blocks **only that one address** when the domain is shared: personal-mail providers
    (gmail.com, yahoo, outlook, ...), bulk-mail platforms (sendgrid.net, ...), brands in `Brands`
    (amazon.com, paypal.com, ...), or a domain where someone you've emailed works.
  * It **learns nothing** from mail sent by you, by someone you've emailed, or by a sender on your
    `AllowSenders` list. The message is still removed.
* **`Gplus-NotJunk`**: something that shouldn't have been removed. Label it, even in Trash or
  Spam. Gplus allows that address, removes any learned block that caught it, and moves the
  message back to your inbox.

Both labels also train the word filter (below).

What it learns goes in `learned.json`, never in your hand-edited `rules.json`. Every lesson is
also written to the decisions log (`Action` starts with `TRAINED` or is `RESTORED`). The labels
are ignored while `DryRun` is on, and by `scan` without `--apply`.

### Word and phrase filter

Besides its rules, Gplus learns which words and two-word phrases show up in your junk versus
your good mail. It works like a classic spam filter (the statistics used by SpamBayes and
Thunderbird). It catches junk that no rule mentions, like a new "reverse mortgage" sender, once
it has seen similar messages.

**What it learns from** (a few dozen messages per sweep, all read-only):

| Junk | Good |
|---|---|
| Messages you put in `Gplus-Train` | Messages you put in `Gplus-NotJunk` |
| Mail Gplus removed at least `WordFilterConfirmDays` (2) days ago that you didn't rescue | Mail you've filed under your own labels, or starred |
| Gmail's Spam folder | |

The training labels always win: moving a message to the other label re-files it on the other
side. Mail removed *only* because of the word filter is never fed back in as junk, so the filter
can't keep reinforcing its own mistakes.

**How it scores.** It stays off until it has `WordFilterMinSpam` (30) junk and `WordFilterMinHam`
(30) good examples. After that, each new message gets a junk probability, and that adds points
like any other signal (weights under `Signals` in `rules.json`):

| Word filter says | Points |
|---|---|
| 99%+ junk | `WordsSpam` (+5) |
| 90–99% junk | `WordsLikelySpam` (+3) |
| 10% or less junk | `WordsHam` (−3) |

On its own it never quite reaches the threshold of 6; it needs one other sign, like a
List-Unsubscribe header. It also never overrides the allow list, starred mail or people you've
emailed. The decision log shows its reasoning, for example
`+5 words 100% junk: "reverse mortgage", "home equity", "tax-free cash"`.

`Gplus.exe words` shows how many examples it has and the words and phrases it finds most telling.
Its memory is `words.json`. Delete that file to start over; it relearns from Gmail.

### Tuning rules

* **Something slipped through?** Put it in `Gplus-Train`, add its domain to `BlockSenders`, or
  add a pattern to a category.
* **Something wrongly removed?** Put it in `Gplus-NotJunk`, add the sender to `AllowSenders`, or
  just reply to them once: anyone you've emailed is protected from then on.
* Turn a whole category on or off with `"Enabled": true/false`. *Social notifications* is off by default.
* `Threshold` (default 6) is the overall aggressiveness: lower means more aggressive.
* Test rules offline: `Gplus.exe test samples.json [rules.json]`. Each sample is
  `{ "from", "subject", "body", "labels", "headers", "expect": true|false }`.
