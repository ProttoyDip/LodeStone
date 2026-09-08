# Lodestone

### Consent-gated runtime ML with quality-gated model publication and fail-closed loading

Lodestone is an ASP.NET Core MVC student-wellbeing application. It combines student-owned support tools with privacy-controlled learning analytics so a validated withdrawal-risk signal can be routed to human counselors.

Behavioral risk monitoring is off unless a student explicitly opts in. A student-supplied LMS number remains an untrusted claim until an Admin approves it. Students can withdraw consent at any time, which disables monitoring and deletes their derived activity logs, feature snapshots, risk scores, and risk-queue records.

Lodestone is not a diagnostic or clinical system. ML predictions are support-routing signals only; they never diagnose, change grades, discipline students, open crisis cases, contact emergency services, or send automatic risk-based nudges.

## Current Status

**State D: A published v3 model, explained and audited, with runtime scoring enabled per environment.**

- Runtime ML is implemented as a first-class application feature through the Application-owned `IRiskModelPredictor` boundary.
- The seventeen-feature `withdrawal-28d-v3` experiment passed its gates and published a runtime artifact to `src/Lodestone.Web/App_Data/ml`.
- Tracked config keeps `MachineLearning:Enabled=false` because the artifacts are git-ignored; each environment that holds them enables scoring explicitly (user-secrets, `MachineLearning__Enabled`, or `LODESTONE_ML_ENABLED` in Docker). The decision and its reasoning are recorded in [AI Governance §9a](docs/AI-GOVERNANCE.md).
- Predictions are explainable on demand from the counselor queue ("Why this score"), and the model has been audited for subgroup performance at both the artifact and deployed thresholds.
- Full tests pass: 192 Unit, 65 Integration, 81 ML, 338 total.

**Known operating-point finding.** The configured `MachineLearning:QueueThreshold` of `0.83` was chosen for precision (about 1 flagged student in 6.5 is genuinely at risk). The fairness audit measured its recall: **4.5%**. At that threshold the queue is accurate and nearly empty, missing roughly 21 of every 22 at-risk student-weeks. At the artifact's own threshold recall is 69.3%, but a third of all student-weeks are flagged. Scoring was enabled with this known; the queue is a capacity-bounded triage aid, not a safety net, and the threshold should be revisited once real counselor capacity is measured.

## Implemented Product Areas

- Student registration/login, role redirects, dashboard, private mood journal, crisis resources, peer forum, and counselor booking.
- Explicit monitoring consent at registration and from the Student Privacy area.
- Admin-reviewed LMS/student-number claims with approve, reject, reset, duplicate checks, and row-version protection.
- Admin import of versioned weekly behavioral snapshots for consented and verified students.
- Runtime risk scoring, auditable scoring runs, idempotent score persistence, one open counselor case per student, and concurrency-safe counselor resolution.
- Admin and Counselor operational views that show real ML availability, model/schema identity, latest scoring status, skipped/failed counts, and queue state.
- Manual counselor nudges from eligible counselor/student interactions, kept independent from ML risk monitoring and requiring separate student opt-in.
- Fail-closed ML loading with model hash, metadata hash, schema, feature order, version, window, stride, manifest, publication eligibility, and loadability validation.
- Local Docker/CI hardening, public-link validation for account email links, sanitized account/setup logging, and persistent Data Protection key configuration.

Deferred areas are deliberately not advertised as complete: PDF report generation, generic analytics templates, Admin notification real-time badge wiring, and automatic risk-based nudges.

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
| Frontend | Hand-written CSS, vanilla JavaScript |
| Tests | xUnit, Moq, FluentAssertions, EF Core InMemory, WebApplicationFactory |

## Architecture

Lodestone follows Clean Architecture. Domain and Application do not depend on EF Core, MVC, Hangfire, SignalR, or ML.NET.

| Project | Responsibility |
| --- | --- |
| `Lodestone.Domain` | Entities, enums, constants, and core state |
| `Lodestone.Application` | Use cases, DTOs, validation, and framework-neutral interfaces |
| `Lodestone.Infrastructure` | EF Core repositories, SQL Server persistence, Identity, email, security |
| `Lodestone.ML` | OULAD loading, feature engineering, training, artifact validation, prediction |
| `Lodestone.Jobs` | Hangfire jobs and startup scheduling |
| `Lodestone.Reporting` | Reporting scaffold; generators are deferred |
| `Lodestone.Web` | MVC, Razor UI, health endpoints, SignalR hubs, composition root |
| `tools/Lodestone.ModelTrainer` | OULAD download and model-training CLI |

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
dotnet ef database update --project src/Lodestone.Infrastructure --startup-project src/Lodestone.Web
dotnet run --project src/Lodestone.Web
```

The local app binds to `http://localhost:5000` and `https://localhost:5001`.

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
    "MetadataPath": "App_Data/ml/risk-model.metadata.json"
  },
  "RiskScoring": {
    "Cron": "0 2 * * 1",
    "TimeZoneId": "UTC"
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

Registration stores the student number as a pending claim. Admins approve/reject/reset claims from `/Admin/RiskMonitoring`. Students see only their consent and verification state; they never see risk probabilities, queue status, hidden monitoring data, or model decisions.

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

The v2 pipeline:

- uses deterministic student-grouped 70/15/15 train/validation/test split;
- tunes FastTree and LightGBM candidates using grouped cross-validation inside training only;
- uses anchor-time behavioral features only;
- selects algorithm, hyperparameters, and operating threshold on validation only;
- requires validation AUC >= 0.70, recall >= 0.70, and precision >= 0.30;
- evaluates the locked test partition exactly once only if validation passes;
- publishes runtime artifacts only if both validation and locked-test gates pass.

Excluded from ML features: demographics, grades, assessment scores, final outcomes, journal text, peer-chat/forum text, counseling/session text, crisis-case text, and future activity.

## Runtime Snapshot Import

Admins import pre-aggregated weekly behavioral snapshots from `/Admin/RiskMonitoring`. The application does not import raw OULAD rows into student accounts.

The model schema controls the required snapshot header. `withdrawal-28d-v1` keeps the original six-feature contract. `withdrawal-28d-v2` adds behavior-only trend, inactivity, assessment timing, course-progress, and cohort-relative activity fields. Runtime scoring requires the imported snapshot schema to match the loaded model schema exactly.

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

- Model version: `withdrawal-28d-v3-20260905T175232755Z`
- Algorithm: LightGbm, seed `20260901`
- Split: 17,339 / 3,714 / 3,718 students (504,023 / 107,501 / 108,238 student-weeks)
- Locked test: ROC AUC `0.753`, PR AUC `0.078`, recall `0.693`, precision `0.053` at the artifact threshold `0.4629`
- Base rate in the locked test partition: 2.55%

Precision near `0.05` at 69% recall is roughly twice the base rate. That is a real signal and a weak one: most flagged student-weeks are not withdrawals. It routes attention; it does not make a determination about anyone.

## Explainable Predictions

A risk score a counselor cannot interrogate is a score they cannot exercise judgement over. `IRiskModelExplainer` reports each feature's measured influence on a single prediction.

Contributions are measured, not estimated: `AblationRiskExplainer` replaces one feature with a reference value, holds the rest still, and re-scores the student. The difference is that feature's contribution, computed from the model's own outputs. Ablation was chosen over ML.NET's feature-contribution transform because it treats the model as a black box, so it survives a change of algorithm rather than failing as a blank panel in front of a counselor.

Three constraints are enforced rather than documented:

- **Nothing is persisted.** Explanations are computed on request and discarded. "Why we believe this student may withdraw" is a stronger inference than the score itself; storing it would create a new class of sensitive record requiring its own consent basis and retention rule.
- **Language cannot overstate the measurement.** `RiskFeatureVocabulary` holds one agreed phrase per feature. `RecentActiveDayRate` reads as "share of days active on the platform", never as *attendance*. `RiskFeatureVocabularyTests` fails the build if a phrase contains attendance, quiz, exam, grade, mark, lecture or class, or if a schema gains a feature with no agreed phrasing.
- **No causal claims.** The model learned association from observational data. Where the system describes what would move a student across the threshold, it is phrased as a property of the model's decision boundary and says so in the same sentence.

Explanation is offered only when a validated model is loaded; otherwise a null explainer keeps the queue rendering the score alone.

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

## Background Jobs And Real-Time Updates

`WeeklyRiskScoringJob` is implemented and registered only when the validated model status is available. Without a valid model, the recurring risk job is removed.

Unfinished automatic jobs are not scheduled. Risk scoring never automatically creates a crisis case, contacts external services, or sends risk-based student nudges.

When scoring creates or escalates a support case, Web broadcasts a payload-free `QueueUpdated` SignalR event through `CounselorQueueHub`. Clients reload authorized queue details through the server.

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

Latest source migration:

```text
20260831094114_RuntimeMlV2AndManualNudges
```

This migration adds v2 snapshot columns, manual-nudge fields and preferences, and journal note protection versioning. EF currently reports no pending model changes. Apply the migration to the configured local/demo database before running the full app against persistent SQL data.

The prior `20260829032603_ConsentGatedRiskMonitoring` migration used a privacy-first upgrade policy that deletes pre-consent monitoring data, converts valid legacy student numbers into pending claims, and clears untrusted verified mappings.

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

Latest verified counts:

- Unit: 153
- Integration: 64
- ML: 81
- Total: 298

Additional verification performed:

- Release solution build: 0 warnings, 0 errors.
- All 13 shipped JavaScript files pass `node --check`.
- Docker Compose config validates with `.env.example`; local Docker config access warning is environment-specific.
- EF reports no pending model changes after the latest migration.
- `git diff --check` reports no whitespace errors, only expected LF-to-CRLF warnings.

## Privacy And Security

- Monitoring is explicit opt-in.
- Claimed LMS identifiers require Admin verification.
- Withdrawal deletes derived monitoring data.
- ML uses only aggregate behavioral features available at prediction time.
- Protected attributes are never collected, never trained on, and never stored; the fairness audit reads them offline from OULAD only.
- Prediction explanations are computed on demand and never persisted.
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
| Docker app loses encrypted notes after reset | The Data Protection key volume was removed or changed. Restore the old key ring backup. |

## License And Academic Use

Lodestone is an academic/capstone project licensed under the [MIT License](LICENSE). OULAD is distributed by its authors/UCI under CC BY 4.0; follow its attribution and license terms when using or redistributing derived work.
