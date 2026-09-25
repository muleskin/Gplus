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

### Tuning rules

* **Something slipped through?** Add its domain to `BlockSenders`, or add a pattern to a category.
* **Something wrongly removed?** Add the sender to `AllowSenders`, or just reply to them once:
  anyone you've emailed is protected from then on.
* Turn a whole category on or off with `"Enabled": true/false`. *Social notifications* is off by default.
* `Threshold` (default 6) is the overall aggressiveness: lower means more aggressive.
* Test rules offline: `Gplus.exe test samples.json [rules.json]`. Each sample is
  `{ "from", "subject", "body", "labels", "headers", "expect": true|false }`.
