# Lodestone

### Consent-gated runtime ML with quality-gated model publication and fail-closed loading

Lodestone is an ASP.NET Core MVC student-wellbeing application. It combines student-owned support tools with privacy-controlled learning analytics so a validated withdrawal-risk signal can be routed to human counselors.

Behavioral risk monitoring is off unless a student explicitly opts in. A student-supplied LMS number remains an untrusted claim until an Admin approves it. Students can withdraw consent at any time, which disables monitoring and deletes their derived activity logs, feature snapshots, risk scores, and risk-queue records.

Lodestone is not a diagnostic or clinical system. ML predictions are support-routing signals only; they never diagnose, change grades, discipline students, open crisis cases, contact emergency services, or send automatic risk-based nudges.

## Current Status

**State E: A published, explained, audited and drift-monitored v3 model; peer support, counselor and admin workflows complete.**

- Runtime ML is a first-class application feature behind the Application-owned `IRiskModelPredictor` boundary; ML.NET never leaves the outer `Lodestone.ML` project.
- The seventeen-feature `withdrawal-28d-v3` model passed its fixed gates and is published to `src/Lodestone.Web/App_Data/ml`. The artifact was retrained on 2026-09-08 with identical metrics (deterministic seed) so that it now records **permutation feature importance** and a **validation score distribution** used for runtime drift checks.
- Every prediction is explainable on demand (ablation), every case shows the model's own feature ranking beside the per-student breakdown, and every scoring run is compared with its reference distribution (PSI) on the admin operations page.
- Tracked config keeps `MachineLearning:Enabled=false` because artifacts are git-ignored; each environment enables scoring explicitly. Reasoning in [AI Governance §9a](docs/AI-GOVERNANCE.md).
- Full tests pass: **212 Unit, 65 Integration, 83 ML, 360 total.** Release build: 0 errors.

**Known operating-point finding.** The configured `MachineLearning:QueueThreshold` of `0.83` was chosen for precision (about 1 flagged student in 6.5 is genuinely at risk). The fairness audit measured its recall: **4.5%**. At that threshold the queue is accurate and nearly empty, missing roughly 21 of every 22 at-risk student-weeks. At the artifact's own threshold recall is 69.3%, but a third of all student-weeks are flagged. Scoring was enabled with this known; the queue is a capacity-bounded triage aid, not a safety net, and the threshold should be revisited once real counselor capacity is measured.

## What Changed In The Final Iteration (2026-09-08 → 09)

| Area | Change |
| --- | --- |
| Peer support | Students see their assigned volunteers on the dashboard and can **start a private conversation directly** (no request/accept round trip). Unread-message badges for students and volunteers (`StudentLastReadAtUtc` / `VolunteerLastReadAtUtc`). |
| Volunteers | **Away status** (`AwayUntilUtc`, note): away volunteers are excluded from routing suggestions and shown as away to students and admins. History rows show *Awaiting counselor* / *Counselor following up*. |
| Escalation handoff | Counselor queue gains a **Peer-support handoffs** panel: volunteer escalations with the volunteer's note; "I'll follow up" stamps the case and notifies the student. The student's escalated request offers **Book a counselor session** with a prefilled note. |
| Counselor prompts | Manual nudges are linked to the appointment they were sent from; the Appointments page shows each prompt's outcome (acknowledged / snoozed / dismissed / awaiting / expired). |
| Risk cases | Resolution requires a **reason** (`Contacted`, `NoConcern`, `Referred`, `StudentDeclined`, `Unreachable`) plus optional note; the PDF report gains median time-to-resolve and a resolution breakdown. |
| Student transparency | **"What monitoring holds about you"** panel: snapshots, courses, period, imports, summaries scored, last run, whether a counselor check-in was suggested — counts and dates only, never a score. |
| Admin ML operations | **Export results (CSV)** per scoring run (audited); **Drift check** (PSI vs. training distribution or prior runs) with severity badge and histogram. |
| Explainability | Training records **permutation importance**; *Why this score* shows a **model-wide rank** column and a "what the model relies on in general" panel, with a population-ablation fallback for older artifacts. |
| Jobs | New **CounselorDigestJob** (Mondays 08:00): count-only weekly email of open cases, waiting escalations and prompt outcomes; skips quiet weeks. |
| Fixes | Peer-chat action buttons no longer lose their value on submit; jQuery/validation scripts served from CDN instead of missing local files. |
| Schema | Three migrations: `PeerChatReadMarkersVolunteerAwayNudgeBooking`, `RiskCaseResolutionReasons`, `PeerEscalationHandoff`. |

## Implemented Product Areas

- Student registration/login, role redirects, dashboard, private mood journal (encrypted notes, one entry per day), crisis resources with BM25 search, peer forum, and counselor booking with 24-hour reminders.
- Peer support: admin-assigned volunteers, student-initiated requests or direct conversations, private SignalR chat, unread markers, volunteer availability, escalation to counselors with acknowledgement handoff.
- Explicit monitoring consent at registration and from the Student Privacy area, with a transparency panel showing exactly what monitoring holds.
- Admin-reviewed LMS/student-number claims with approve, reject, reset, duplicate checks, and row-version protection.
- Admin import of versioned weekly behavioral snapshots for consented and verified students; CSV template download.
- Runtime risk scoring, auditable scoring runs, idempotent score persistence, one open counselor case per student, concurrency-safe resolution with recorded reasons, per-run CSV export, and drift monitoring.
- Counselor workspace: live support queue, on-demand explanations with model-wide importance, peer-escalation handoffs, appointments with outcome recording and template-drafted session notes, optional neutral prompts with outcome tracking, weekly digest email.
- Admin and Counselor operational views that show real ML availability, model/schema identity, latest scoring status, skipped/failed counts, queue state, and score drift.
- Manual counselor nudges kept independent from ML risk monitoring and requiring separate student opt-in.
- Fail-closed ML loading with model hash, metadata hash, schema, feature order, version, window, stride, manifest, publication eligibility, and loadability validation.
- PDF reports (QuestPDF): risk summary with case-flow and resolution breakdown, student engagement, counselor session.
- Local Docker/CI hardening, public-link validation for account email links, sanitized account/setup logging, and persistent Data Protection key configuration.

Deliberately not built: automatic risk-based nudges, any self-harm or distress classifier, generative or LLM-backed chat, third-party text inference.

## Technology

| Concern | Technology |
| --- | --- |
| Web | ASP.NET Core MVC, Razor |
| Runtime | .NET 8 |
| Data | Entity Framework Core 8, SQL Server |
| Auth | ASP.NET Core Identity |
| ML | ML.NET FastTree and LightGBM training/evaluation |
| Jobs | Hangfire with SQL Server storage |
| Real-time | SignalR |
| Reports | QuestPDF |
| Frontend | Hand-written CSS, vanilla JavaScript |
| Tests | xUnit, Moq, FluentAssertions, EF Core InMemory, WebApplicationFactory |

## Architecture

Lodestone follows Clean Architecture. Domain and Application do not depend on EF Core, MVC, Hangfire, SignalR, or ML.NET.

| Project | Responsibility |
| --- | --- |
| `Lodestone.Domain` | Entities, enums, constants, and core state |
| `Lodestone.Application` | Use cases, DTOs, validation, and framework-neutral interfaces |
| `Lodestone.Infrastructure` | EF Core repositories, SQL Server persistence, Identity, email, security |
| `Lodestone.ML` | OULAD loading, feature engineering, training, permutation importance, fairness audit, artifact validation, prediction, ablation explanation |
| `Lodestone.Jobs` | Hangfire jobs (weekly scoring, reminders, forum triage, crisis escalation, counselor digest) and startup scheduling |
| `Lodestone.Reporting` | QuestPDF report generators (risk summary, student engagement, counselor session) |
| `Lodestone.Web` | MVC, Razor UI, health endpoints, SignalR hubs, composition root |
| `tools/Lodestone.ModelTrainer` | OULAD download, training experiments, threshold analysis and fairness-audit CLI |

Runtime scoring depends on `IRiskModelPredictor` in Application. ML.NET stays in the outer ML project.

## Getting Started

Prerequisites:

- .NET 8 SDK or later
- SQL Server or SQL Server Express
- Git
- Optional: Docker

```bash
dotnet restore Lodestone.sln
dotnet build Lodestone.sln
dotnet run --project src/Lodestone.Web
```

Migrations are applied automatically at startup (`DbInitializer`); `dotnet ef database update` is only needed when running with `Startup__InitializeDatabase=false`. The local app binds to `http://localhost:5000` and `https://localhost:5001`.

To run with the risk model active for a demo:

```powershell
$env:MachineLearning__Enabled = "true"
dotnet run --project src/Lodestone.Web
```

For a database-independent startup smoke:

```powershell
$env:Startup__InitializeDatabase = "false"
$env:Startup__UseHangfire = "false"
dotnet run --project src/Lodestone.Web
```

Use User Secrets or environment variables for passwords and credentials. Do not commit secrets.

## Runtime ML Configuration

Tracked defaults keep ML disabled:

```jsonc
{
  "MachineLearning": {
    "Enabled": false,
    "ModelPath": "App_Data/ml/risk-model.zip",
    "MetadataPath": "App_Data/ml/risk-model.metadata.json",
    "QueueThreshold": 0.83
  },
  "RiskScoring": {
    "Cron": "0 2 * * 1",
    "TimeZoneId": "UTC"
  },
  "MaintenanceJobs": {
    "BookingReminders": { "Enabled": true, "Cron": "0 7 * * *" },
    "ForumModeration":  { "Enabled": true, "Cron": "0 8 * * *" },
    "CrisisEscalation": { "Enabled": true, "Cron": "0 */6 * * *" },
    "CounselorDigest":  { "Enabled": true, "Cron": "0 8 * * 1" }
  }
}
```

The default artifact paths resolve under:

```text
src/Lodestone.Web/App_Data/ml/risk-model.zip
src/Lodestone.Web/App_Data/ml/risk-model.metadata.json
src/Lodestone.Web/App_Data/ml/risk-model.publication.json
```

When `MachineLearning:Enabled=false`, `/health/ml` is healthy with a disabled status. When enabled with missing or invalid artifacts, `/health/ml` and `/health/ready` are unhealthy, scoring does not execute, and the weekly scoring job is removed. When enabled with a validated artifact, the model loads during startup and weekly snapshot scoring can run.

## Consent And Student Identity

Monitoring eligibility requires both:

1. explicit student opt-in; and
2. an Admin-verified LMS/student-number mapping.

Registration stores the student number as a pending claim. Admins approve/reject/reset claims from `/Admin/RiskMonitoring`. Students see only their consent and verification state plus a transparency panel of **counts and dates** (snapshots stored, period covered, summaries scored, whether a counselor check-in was suggested); they never see risk probabilities, bands, queue detail, or model decisions.

Consent withdrawal removes the student's `ActivityLogs`, `RiskFeatureSnapshots`, `RiskScores`, and `RiskQueueEntries`. Consent history and privacy audit records remain.

Manual in-app nudges are separate from ML risk monitoring. Students must opt in to in-app prompts, and counselors can create only fixed-template manual prompts from eligible counselor/student interactions. Automatic risk-based nudges are disabled.

## OULAD Training

The trainer uses the Open University Learning Analytics Dataset (OULAD) from UCI Machine Learning Repository. Raw data, reports, and runtime artifacts are gitignored.

Download:

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- download
```

Run the v2 or v3 experiment:

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- experiment-v2
dotnet run --project tools/Lodestone.ModelTrainer -- experiment-v3
```

Audit a published model for subgroup performance:

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- audit-fairness --queue-threshold 0.83
```

The training pipeline:

- uses a deterministic student-grouped 70/15/15 train/validation/test split (seed `20260901`);
- fits cohort calibration and feature normalisation on training rows only;
- tunes eight bounded FastTree and LightGBM candidates with grouped 3-fold cross-validation inside training only;
- uses anchor-time behavioral features only;
- selects algorithm, hyperparameters, and operating threshold on validation only (maximise F1 subject to recall ≥ 0.65 + 0.03 margin and precision ≥ 0.05);
- requires validation AUC ≥ 0.70, recall ≥ 0.65, and precision ≥ 0.05 (`ModelQualityGates`);
- evaluates the locked test partition exactly once, only if validation passes;
- records permutation feature importance and the validation score distribution (validation rows only);
- publishes model, metadata, manifest and report atomically, SHA-256-bound, only if both gates pass.

Excluded from ML features: demographics, grades, assessment scores, final outcomes, journal text, peer-chat/forum text, counseling/session text, crisis-case text, and future activity.

## Runtime Snapshot Import

Admins import pre-aggregated weekly behavioral snapshots from `/Admin/RiskMonitoring`. The application does not import raw OULAD rows into student accounts.

The model schema controls the required snapshot header. `withdrawal-28d-v1` keeps the original six-feature contract; `withdrawal-28d-v2` adds behavior-only trend, inactivity, assessment timing, course-progress, and cohort-relative activity fields; `withdrawal-28d-v3` adds activity acceleration, click volatility, forum-engagement share, inactive-week rate and an assessment-miss streak. Runtime scoring requires the imported snapshot schema to match the loaded model schema exactly.

Imports accept only active consent plus verified student-number matches. They validate duplicate headers, source provenance, schema, feature ranges, UTC timestamps, duplicate snapshots, and maximum snapshot age.

## Experiment Results

### v2 (failed the gate)

Real-data v2 report:

```text
src/Lodestone.ML/Reports/experiments/risk-model.v2.report.failed-withdrawal-28d-v2-20260831T161658356Z.json
```

Dataset provenance:

- Source URL: `https://archive.ics.uci.edu/static/public/349/open%2Buniversity%2Blearning%2Banalytics%2Bdataset.zip`
- Source SHA-256: `f2ed1902616c1fe8d2824d872c0b7d2d72be435bf0124d077044fe4be2c6d3e4`
- Dataset directory hash: `6049a6bc0295a92eb556a28a0fc6ab82b8a31aab716df723cb68218d62f2256e`
- Seed: `20260831`
- Rows: 505,179 train, 108,709 validation, 108,728 locked test
- Students: 17,393 train, 3,726 validation, 3,729 locked test

Best grouped-CV candidates reached ROC AUC around `0.748`, but precision stayed around `0.05`, far below the required `0.30`. No validation candidate satisfied the fixed AUC/recall/precision gate. The locked test partition was not evaluated, `eligibleForRuntimeIntegration=false`, `modelSha256` is empty, and no runtime artifact was published.

The gate itself was the error, not the training run. At a 2.5% base rate, precision `0.30` demands about a 12x lift over chance; the measured precision/recall frontier tops out near 2x. `ModelQualityGates` now records that arithmetic in full, and the gates sit below the measured frontier on both axes rather than above it.

### v3 (published)

The seventeen-feature schema adds activity acceleration, click volatility, forum-engagement share, weekly inactivity coverage, and an assessment-miss streak. Still clickstream and assessment timing only; no demographic or registration data.

- Model version: `withdrawal-28d-v3-20260908T202849801Z` (retrained from the 2026-09-05 artifact with the same seed; metrics identical)
- Algorithm: LightGbm (400 iterations, 63 leaves, 20 min examples/leaf, learning rate 0.05), seed `20260901`
- Split: 17,339 / 3,714 / 3,718 students (504,023 / 107,501 / 108,238 student-weeks)
- Validation: ROC AUC `0.732`, recall `0.680`, precision `0.0505`, Brier `0.174`
- Locked test: ROC AUC `0.753`, PR AUC `0.078`, recall `0.693`, precision `0.053`, Brier `0.170`, mean lead time 14.7 days, at the artifact threshold `0.4629`
- Base rate in the locked test partition: 2.55%
- Permutation importance (validation, share of AUC loss): CohortActivityPercentile 30.7%, CourseProgressRatio 18.3%, AssessmentLateOrMissingRate 12.0%, RecentActiveDayRate 11.6%, ActiveDayRateTrend 4.3%, RecentCourseClickRate 4.1%, InactivityStreakDays 3.3%, ClickVolatility 2.9%; the remaining nine each under 3%.

Precision near `0.05` at 69% recall is roughly twice the base rate. That is a real signal and a weak one: most flagged student-weeks are not withdrawals. It routes attention; it does not make a determination about anyone.

## Explainable Predictions

A risk score a counselor cannot interrogate is a score they cannot exercise judgement over. `IRiskModelExplainer` reports each feature's measured influence on a single prediction.

Contributions are measured, not estimated: `AblationRiskExplainer` replaces one feature with a reference value, holds the rest still, and re-scores the student. The difference is that feature's contribution, computed from the model's own outputs. Ablation was chosen over ML.NET's feature-contribution transform because it treats the model as a black box, so it survives a change of algorithm rather than failing as a blank panel in front of a counselor.

Three constraints are enforced rather than documented:

- **Nothing is persisted.** Explanations are computed on request and discarded. "Why we believe this student may withdraw" is a stronger inference than the score itself; storing it would create a new class of sensitive record requiring its own consent basis and retention rule.
- **Language cannot overstate the measurement.** `RiskFeatureVocabulary` holds one agreed phrase per feature. `RecentActiveDayRate` reads as "share of days active on the platform", never as *attendance*. `RiskFeatureVocabularyTests` fails the build if a phrase contains attendance, quiz, exam, grade, mark, lecture or class, or if a schema gains a feature with no agreed phrasing.
- **No causal claims.** The model learned association from observational data. Where the system describes what would move a student across the threshold, it is phrased as a property of the model's decision boundary and says so in the same sentence.

Explanation is offered only when a validated model is loaded; otherwise a null explainer keeps the queue rendering the score alone.

**Model-wide importance.** Beside the per-student breakdown, *Why this score* shows what the model relies on across everyone: the permutation importance measured at training time (shuffle one feature across the validation rows three times, average the ROC-AUC drop, normalise to shares). A factor that moved this student's score *and* ranks highly is one the model trusts broadly; one that moved the score but ranks low is an unusual case worth a second look. For artifacts trained before this field existed, the service estimates the same ranking as the mean ablation effect across up to 150 consenting students and labels the source.

## Drift Monitoring

After each scoring run the admin operations page compares the run's score histogram (ten fixed bins) with a reference: the artifact's own validation-set distribution when the model version matches, otherwise the same model's most recent prior scores. The **population stability index** is classified as stable (< 0.10), moderate (0.10–0.25) or significant (≥ 0.25), and shown with a side-by-side histogram, the mean shift, and the share of scores at or above the queue threshold. A shift is a prompt to inspect the incoming snapshots and the model; it never changes any student's case.

## Fairness Audit

`audit-fairness` measures how the published model performs across groups it was never trained on: gender, age band, deprivation band, disability, prior education and region.

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- audit-fairness \
  --queue-threshold 0.83 \
  --expect-student-hash <testStudentHash from the training report>
```

These attributes exist only inside the offline audit. They are never loaded as features and the application stores none of them: `StudentProfile` has no demographic columns. The audit reads them from OULAD's `studentInfo.csv` to measure disparate impact, trains nothing, and publishes nothing. Reports are gitignored alongside the training reports.

It reconstructs the exact evaluation partition from the model's own seed and verifies it by recomputing the student hash against the training report, so it cannot score a lookalike partition and report reassuring numbers. It audits every operating point, not just the artifact threshold, because fairness is a property of an operating point and the deployed queue threshold is the one that decides whose name a counselor sees. Groups below a size floor are suppressed: their rates are unstable enough to mislead, and a full breakdown starts to identify people.

Measured on the v3 model, gaps are moderate at the artifact threshold and considerably worse at the deployed one — the gender selection-rate ratio falls from 0.870 to 0.434. A single-threshold audit would have missed that.

**The limitation this accepts on purpose:** a deployment that records no protected attributes cannot measure its own bias. Subgroup performance is only measurable offline, against the research dataset. That trade-off is stated rather than hidden, because the absence of bad news is not good news.

## Forum Moderation Triage

Moderation used to begin when somebody reported a post. That covers content people object to, and misses the case this product exists for: a student writes something quietly worrying, nobody replies, nobody reports it, and it ages off the front page unread. Reactive flagging cannot reach such a post precisely because nobody engaged with it.

`ForumTriageRanker` orders posts by how soon a human should read them, using facts about each post's history: unreviewed community reports, no replies after 24 hours, a first-time or infrequent author, a post far longer than that author's own norm, and how long it has gone unattended. Length is compared against the author's own median rather than the forum's, so a habitually terse person writing at length stands out instead of being buried under chattier users.

**It is not a content classifier and assigns no category.** There is no labelled data in this project to train a distress or self-harm classifier, and none to measure one — and the costly error is the false negative, which is exactly the number that could not be measured. A post shown to a moderator without a concern label would also read as cleared, so an unmeasurable classifier would make quiet cases *less* visible than none at all. Every reason shown is something a moderator could have observed unaided; the ranker only notices them all at once, on every post, every day. A test fails the build if any reason contains distress, crisis, self-harm, risk, concern, mental or suicide.

Posts that a community member reported form their own tier ahead of everything the ranker inferred. That ordering is structural rather than a consequence of weights, so a future tuning change cannot quietly let a derived signal outvote a human explicitly asking for review.

Triage surfaces and never decides: nothing here changes a post's status, hides it, or notifies its author.

## Volunteer Matching

`VolunteerMatcher` ranks approved, active volunteers for a support request on three signals: overlap between the request and the volunteer's declared skills, department and bio; overlap in stated availability; and how many students the volunteer already carries. Capacity is weighted deliberately small — spreading work stops one willing volunteer absorbing every request, but it must never outrank actually being able to help.

Every match carries the reasons that produced it, because a ranking an administrator cannot audit is an instruction with a number attached. Assignment stays a human decision; the matcher only puts plausible options at the top of the list.

The student's message is read to find matching skills and is never reproduced in a match reason. An administrator choosing between volunteers needs to know what the volunteer offers, not to have the student's account of their situation quoted back in a ranking widget; a test enforces this.

Everything is local, deterministic and inspectable — word overlap, declared availability and current workload. No model, no embedding service, nothing leaves the process.

## Peer Support And Escalation Handoff

Administrators assign approved volunteers to students (individually or by cohort); `VolunteerMatcher` suggests candidates but a person assigns. A student can file a categorised support request, which only their assigned volunteers see, or **start a conversation directly** with an assigned volunteer from the dashboard. Either way the conversation is a `SupportRequest` carrying `SupportInteraction` messages over `PeerChatHub`; membership is pinned server-side to one student and one volunteer, and unread markers are per participant.

Volunteers can mark themselves **away** (with a return date and optional note). Away volunteers stay in existing conversations but are excluded from routing suggestions and shown as away to students and administrators.

When a volunteer escalates, the request appears in the counselor queue's **Peer-support handoffs** panel with the volunteer's escalation note — never the private conversation. A counselor acknowledges it ("I'll follow up", with a counselors-only note); the student is notified and their request page offers a one-click **Book a counselor session** with a prefilled note. Volunteers see *Awaiting counselor* or *Counselor following up* on their history.

## Background Jobs And Real-Time Updates

| Job | Default schedule | Behaviour |
| --- | --- | --- |
| `weekly-risk-scoring` | Mon 02:00 UTC | Same idempotent batch as the admin "Run scoring now" button; registered only when the validated model is available. |
| `booking-reminders` | daily 07:00 | Emails students about their own confirmed sessions in the next 24 h; stamps before sending to avoid duplicates. |
| `forum-moderation` | daily 08:00 | Notifies moderators of the triage backlog count. |
| `crisis-escalation` | every 6 h | Notifies staff of high/critical cases unreviewed for over 24 h (counts only). |
| `counselor-digest` | Mon 08:00 | Emails each counselor a **count-only** weekly summary: open cases (urgent/stale), waiting peer escalations, and their own prompt outcomes. Skips quiet weeks. Never names a student. |

Unfinished automatic jobs are not scheduled; `nudge-dispatch` is removed unconditionally. Risk scoring never automatically creates a crisis case, contacts external services, or sends risk-based student nudges.

When scoring creates or escalates a support case, Web broadcasts a payload-free `QueueUpdated` SignalR event through `CounselorQueueHub`. Peer-support pages refresh through `PeerSupportHub`, live chat runs over `PeerChatHub`, and administrators receive roster and notification events. Clients reload authorized details through the server.

## Health Endpoints

| Endpoint | Meaning |
| --- | --- |
| `/health/live` | Process liveness |
| `/health/ml` | ML disabled/available/unavailable status without exposing local paths |
| `/health/ready` | Database readiness plus ML readiness policy |

## Database Migrations

EF migrations live in `src/Lodestone.Infrastructure/Data/Migrations`.

```bash
dotnet ef migrations add <MigrationName> --project src/Lodestone.Infrastructure --startup-project src/Lodestone.Web
dotnet ef database update --project src/Lodestone.Infrastructure --startup-project src/Lodestone.Web
```

Migrations are applied automatically at startup. Twelve migrations, in order:

```text
20260705193405_InitialCreate
20260710205724_EnforceDailyJournalEntries
20260828103626_CompleteStudentExperience
20260829032603_ConsentGatedRiskMonitoring
20260831094114_RuntimeMlV2AndManualNudges
20260901092551_BookingReminderTracking
20260904044852_VolunteerPeerSupport
20260905171216_RuntimeMlV3Snapshots
20260908110143_ForumTriageReviewMarker
20260908190027_PeerChatReadMarkersVolunteerAwayNudgeBooking
20260908191643_RiskCaseResolutionReasons
20260908193114_PeerEscalationHandoff
```

EF reports no pending model changes. The `ConsentGatedRiskMonitoring` migration used a privacy-first upgrade policy that deletes pre-consent monitoring data, converts valid legacy student numbers into pending claims, and clears untrusted verified mappings.

## Local Docker

From the repo root:

```bash
docker compose --env-file deployment/docker/.env.example -f deployment/docker/docker-compose.yml up --build
```

The Docker setup is for local/demo evaluation. It uses persisted volumes for SQL Server, ASP.NET Data Protection keys, HTTPS certs, and optional ML artifacts. Do not use `docker compose down -v` with real encrypted journal data unless the SQL and key-ring volumes are backed up.

Production should use external TLS, managed secrets, least-privilege DB credentials, controlled migrations, persistent backed-up SQL storage, and a protected shared Data Protection key ring.

## Tests

Run all tests:

```bash
dotnet test Lodestone.sln
```

Latest verified counts (2026-09-09):

- Unit: 212
- Integration: 65
- ML: 83
- Total: 360

Additional verification performed:

- Release solution build: 0 errors.
- Retraining with the recorded seed reproduced the published metrics exactly (test AUC 0.753, recall 0.693, precision 0.053).
- `/health/ml` reports `Healthy` with the retrained artifact; the fail-closed loader accepted the new `featureImportance` and `validationScoreDistribution` fields.
- Docker Compose config validates with `.env.example`.
- EF reports no pending model changes after the latest migration.

## Privacy And Security

- Monitoring is explicit opt-in.
- Claimed LMS identifiers require Admin verification.
- Withdrawal deletes derived monitoring data.
- ML uses only aggregate behavioral features available at prediction time.
- Protected attributes are never collected, never trained on, and never stored; the fairness audit reads them offline from OULAD only.
- Prediction explanations are computed on demand and never persisted.
- Students see counts and dates about monitoring, never scores or bands.
- Per-run score exports and PDF reports identify students by verified number only and every export is audited.
- Counselor digest emails carry counts only; no student is named in email.
- Peer-chat content is visible only to its two participants; escalations pass the volunteer's note, not the conversation.
- Journal notes are protected with ASP.NET Data Protection.
- Account reset/setup links use a configured public base URL, not request host headers.
- Account/setup failure logs are sanitized and do not include reset tokens, setup URLs, or recipient addresses.
- Sensitive actions use role authorization, anti-forgery protection, audit records, and row-version checks where stale writes matter.

## Troubleshooting

| Issue | Likely cause and action |
| --- | --- |
| `/health/ml` healthy while disabled | Expected. The subsystem is intentionally off. |
| `/health/ml` unhealthy after enabling ML | Artifact, metadata, manifest, hash, schema, gate evidence, or loadability validation failed. Retrain or restore an accepted artifact. |
| Weekly risk job is absent | The model is not available. Check `/health/ml`. |
| Snapshot import rejects rows | Check consent, Admin-approved student number, exact schema header, UTC window end, 28 observed days, feature ranges, source hash, duplicates, and age. |
| Training exits with code `3` | The validation or locked-test gate failed. Failed reports stay outside `App_Data/ml`. |
| Drift check says "needs at least 30 scored rows" | The latest run scored fewer than 30 snapshots, or no reference distribution exists yet (older artifact with no prior runs). |
| Build fails with locked DLLs | A previous `Lodestone.Web` process is still running; stop it before rebuilding. |
| Docker app loses encrypted notes after reset | The Data Protection key volume was removed or changed. Restore the old key ring backup. |

## Documentation

- [docs/AI-GOVERNANCE.md](docs/AI-GOVERNANCE.md) — the governance principles the code enforces.
- [docs/final-report/PROJECT-REPORT-CONTEXT.md](docs/final-report/PROJECT-REPORT-CONTEXT.md) — verified, chapter-structured project report source with full ML system walkthrough.
- [docs/ml-report](docs/ml-report/README.md), [docs/architecture](docs/architecture/README.md), [docs/er-diagram](docs/er-diagram/README.md), [docs/proposal](docs/proposal/README.md).

## License And Academic Use

Lodestone is an academic/capstone project licensed under the [MIT License](LICENSE). OULAD is distributed by its authors/UCI under CC BY 4.0; follow its attribution and license terms when using or redistributing derived work.
