# Lodestone

### A consent-gated behavioral early-warning and peer-support platform

Lodestone is an ASP.NET Core MVC application for student wellbeing. It combines student-owned
support tools with privacy-controlled learning analytics so institutions can identify possible
disengagement and route it to human counselors.

Behavioral risk monitoring is **off unless a student explicitly opts in**. A student-supplied LMS
number is only a claim until an Admin verifies it; imports cannot connect learning data to the
account before that approval. Students can withdraw at any time, which disables monitoring and
deletes their derived activity logs, feature snapshots, scores, and all risk-queue records.

> Lodestone is not a diagnostic or clinical system. A risk probability estimates withdrawal risk
> from limited behavioral features; it does not diagnose mental illness, determine grades, or make
> disciplinary decisions. Queue entries are reviewed by people.

---

## Table of Contents

- [Implemented product areas](#implemented-product-areas)
- [Technology](#technology)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Getting started](#getting-started)
- [Configuration](#configuration)
- [Consent and verified student numbers](#consent-and-verified-student-numbers)
- [ML model and OULAD training](#ml-model-and-oulad-training)
- [Runtime snapshot import and scoring](#runtime-snapshot-import-and-scoring)
- [Counselor queue](#counselor-queue)
- [Background jobs and real-time updates](#background-jobs-and-real-time-updates)
- [Database migrations](#database-migrations)
- [Tests](#tests)
- [Privacy and security](#privacy-and-security)
- [Current status](#current-status)
- [Troubleshooting](#troubleshooting)

---

## Implemented product areas

- Student authentication, dashboard, private mood journal, crisis resources, peer forum, and
  published-slot counselor booking.
- Explicit weekly risk-monitoring choice during registration and from the Student Privacy area.
- Admin review of pending LMS/student-number claims, with approve, reject, and reset workflows.
- Admin CSV import of versioned 28-day feature snapshots for consented, verified students.
- ML.NET OULAD download, leakage-safe feature engineering, grouped train/validation/test split,
  constrained threshold selection, quality-gated artifact publication, and fail-closed loading.
- Idempotent risk scoring, auditable scoring runs, one open counselor case per student, and
  concurrency-safe counselor resolution.
- Payload-free SignalR queue refresh notifications; confidential queue data remains behind an
  authorized server-side query.
- Live Admin operations views for model readiness, import status, scoring runs, student-number
  verification, forum moderation, notifications, bookings, and people records.

Some broader repository areas remain scaffolds. In particular, PDF report generators, the nudge
service, and the booking-reminder, forum-moderation, and crisis-escalation background jobs still
throw `NotImplementedException`. The generic analytics Dashboard view is also still a placeholder.

---

## Technology

| Concern | Technology |
| --- | --- |
| Web | ASP.NET Core MVC with Razor views |
| Runtime | .NET 8 |
| Data | Entity Framework Core 8, code-first migrations |
| Database | **SQL Server only** (SQL Express is the current local target) |
| Authentication | ASP.NET Core Identity and role/policy authorization |
| Machine learning | ML.NET |
| Background work | Hangfire with SQL Server storage |
| Real-time | ASP.NET Core SignalR |
| Frontend | Server-rendered Razor, hand-written CSS, vanilla JavaScript |
| Tests | xUnit, Moq, FluentAssertions, EF Core InMemory, WebApplicationFactory |

The tracked project does not include a PostgreSQL provider or PostgreSQL migrations.

---

## Architecture

Lodestone follows Clean Architecture. Domain rules and Application contracts do not depend on
EF Core, MVC, Hangfire, SignalR, or ML.NET.

| Project | Responsibility |
| --- | --- |
| `Lodestone.Domain` | Entities, enums, constants, and core state |
| `Lodestone.Application` | Use cases, DTOs, validation, and framework-neutral interfaces |
| `Lodestone.Infrastructure` | EF Core repositories, SQL Server persistence, Identity, email |
| `Lodestone.ML` | OULAD loading, feature engineering, training, artifact validation, prediction |
| `Lodestone.Jobs` | Hangfire job definitions and schedules |
| `Lodestone.Reporting` | QuestPDF/reporting scaffold; generators are not yet implemented |
| `Lodestone.Web` | MVC, Razor UI, health endpoint, SignalR hubs, composition root |
| `tools/Lodestone.ModelTrainer` | Offline dataset download and model-training CLI |

Runtime scoring depends on the Application-owned `IRiskModelPredictor` boundary. ML.NET stays in
the outer ML project, and Web supplies only composition and transport concerns.

---

## Prerequisites

- .NET 8 SDK or later
- SQL Server or SQL Server Express
- The `dotnet-ef` CLI for migrations
- Git
- Optional: the official OULAD dataset, downloaded by the included trainer tool

---

## Getting started

```bash
git clone <repository-url>
cd Lodestone
dotnet restore Lodestone.sln
dotnet build Lodestone.sln
dotnet ef database update --project src/Lodestone.Infrastructure --startup-project src/Lodestone.Web
dotnet run --project src/Lodestone.Web
```

The local configuration binds to `http://localhost:5000` and `https://localhost:5001`. Startup
normally applies migrations, seeds roles/reference data, and configures Hangfire. For a
database-independent health smoke test, use:

```powershell
$env:Startup__InitializeDatabase = "false"
$env:Startup__UseHangfire = "false"
dotnet run --project src/Lodestone.Web
```

Use User Secrets or environment variables for passwords and other credentials. Do not commit them.

---

## Configuration

Tracked defaults intentionally keep ML scoring disabled:

```jsonc
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=<sql-server>;Database=Lodestone;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True",
    "HangfireConnection": "Server=<sql-server>;Database=LodestoneHangfire;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  },
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

`ModelPath` and `MetadataPath` are resolved relative to the Web content root, so the defaults mean:

```text
src/Lodestone.Web/App_Data/ml/risk-model.zip
src/Lodestone.Web/App_Data/ml/risk-model.metadata.json
```

Set `MachineLearning__Enabled=true` only after a model and matching metadata sidecar have passed the
training gate. The runtime validates schema, feature order, window sizes, model hash, metadata, and
loadability. Any mismatch makes the predictor unavailable; it never silently falls back to an old,
synthetic, or incompatible model.

The ML-only readiness endpoint is:

```text
GET /health/ml
```

It reports healthy when ML is intentionally disabled, healthy with only non-sensitive model/schema
identifiers when a validated artifact is available, and unhealthy when ML is enabled but the model
cannot be safely loaded. It does not expose local artifact paths.

---

## Consent and verified student numbers

Monitoring eligibility requires both conditions:

1. The student has explicitly enabled weekly support monitoring.
2. An Admin has approved the student's submitted LMS/student number.

Registration requires a student number but stores it as a **pending claim**, not as a trusted
mapping. Students can inspect the verification state and resubmit after rejection from the Privacy
area. Admins review pending claims from `/Admin/RiskMonitoring`; duplicate verified numbers are
rejected, and row-version checks protect review actions from stale updates.

Turning monitoring off is a destructive privacy action for derived monitoring data. It removes that
student's `ActivityLogs`, `RiskFeatureSnapshots`, `RiskScores`, and `RiskQueueEntries`. The consent
history and privacy audit trail remain so the institution can demonstrate the choice without
retaining the withdrawn behavioral data.

An Admin identity reset performs the same purge, disables consent, clears the verified mapping, and
requires a new claim, Admin approval, and student opt-in before future imports can attach. Audit-log
details deliberately avoid recording the student number itself.

---

## ML model and OULAD training

The trainer uses the [Open University Learning Analytics Dataset (OULAD)](https://archive.ics.uci.edu/dataset/349/open)
from UCI Machine Learning Repository (dataset 349, DOI `10.24432/C5KK69`, CC BY 4.0). Raw dataset
files and generated artifacts are gitignored.

### Download

From the repository root:

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- download
```

The command downloads over HTTPS, computes SHA-256, extracts through a traversal-safe staging
directory, verifies exactly one copy of all seven canonical OULAD CSV tables, records provenance in
`source.json`, and atomically moves the dataset to:

```text
src/Lodestone.ML/Data/OULAD
```

Pin an expected archive hash when reproducibility requires it:

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- download --sha256 <64-character-sha256>
```

### Train

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- train
```

Defaults:

- Input: `src/Lodestone.ML/Data/OULAD`
- Model: `src/Lodestone.Web/App_Data/ml/risk-model.zip`
- Metadata: `src/Lodestone.Web/App_Data/ml/risk-model.metadata.json`
- Evaluation report: `src/Lodestone.ML/Reports/risk-model.report.json`
- Random seed: `42`
- Minimum untouched-test AUC: `0.70`
- Minimum recall: `0.70`
- Minimum precision: `0.30`

The pipeline trains a class-weighted ML.NET FastTree binary classifier on a deterministic 70/15/15
split.
Students, not observation rows, are grouped across training, validation, and untouched test sets,
preventing one student's rolling windows from leaking between partitions. The decision threshold is
selected on validation data from candidates satisfying recall >= 0.70 and precision >= 0.30; among
them it prefers the best F1 score. The untouched test set must then meet all three fixed gates.

Failed training exits with code `3`, writes a versioned `*.failed-*.json` report, and leaves any
previous production artifacts untouched. Successful publication verifies save/reload prediction
parity, hashes the model, writes the hash-bound metadata sidecar, and atomically publishes model,
metadata, and report.

### `withdrawal-28d-v1` semantics

Each row observes the previous 28 days, advances on a seven-day stride, and labels whether the
student unregisters in the following 28 days. It uses only behavioral fields:

| Feature | Meaning |
| --- | --- |
| `ActiveDayRate` | Days with any VLE click divided by 28 |
| `ActivitySpanDays` | Inclusive first-to-last active-day span; `0` when inactive |
| `DaysSinceLastAccess` | Days from the window end to last activity; `28` when inactive |
| `ForumInteractionCount` | `forumng` clicks in the observation window |
| `CourseInteractionCount` | Non-forum VLE clicks; forum clicks are not double-counted |
| `LateOrMissingAssignmentCount` | Assessments due in-window that were missing or late at the anchor; banked work is excluded |

Demographics, final results, assessment scores, private journal text, forum text, and counseling
content are not model features.

### Current artifact status

The official archive is downloaded locally with SHA-256
`f2ed1902616c1fe8d2824d872c0b7d2d72be435bf0124d077044fe4be2c6d3e4`. Two real-data attempts were
correctly rejected:

- The SDCA baseline reached validation AUC `0.654` and untouched-test AUC `0.648`; at threshold
  `0.5`, test recall was `0.612` and precision was `0.040`.
- The current FastTree trainer improved validation AUC to `0.678` and untouched-test AUC to `0.671`,
  but no validation threshold satisfied both recall >= `0.70` and precision >= `0.30`.

No runtime model or metadata artifact was published. `MachineLearning:Enabled` therefore remains
`false`; this is a safe, expected state rather than a synthetic-model fallback or a reason to lower
the approved quality gates.

---

## Runtime snapshot import and scoring

Runtime training and runtime data ingestion are intentionally separate. Admins import weekly,
pre-aggregated feature snapshots from `/Admin/RiskMonitoring`; the application does not import raw
OULAD rows into student accounts.

CSV columns, in this exact order:

```text
StudentNumber,CourseKey,WindowEndUtc,ObservedDays,FeatureSchemaVersion,ActiveDayRate,ActivitySpanDays,DaysSinceLastAccess,ForumInteractionCount,CourseInteractionCount,LateOrMissingAssignmentCount
```

Example:

```csv
StudentNumber,CourseKey,WindowEndUtc,ObservedDays,FeatureSchemaVersion,ActiveDayRate,ActivitySpanDays,DaysSinceLastAccess,ForumInteractionCount,CourseInteractionCount,LateOrMissingAssignmentCount
STU-0001,COURSE-01,2026-08-24T00:00:00Z,28,withdrawal-28d-v1,0.5,26,2,8,120,1
```

Imports accept only verified student-number matches with active consent. They validate the schema,
28-day window, UTC/future timestamps, feature ranges, source filename/hash, duplicates, and maximum
snapshot age. Snapshots older than eight days are not eligible for scoring.

Scoring is idempotent per snapshot/model. A run records candidates, scored/skipped/failed counts,
queue creations/escalations, status, and a bounded failure summary. A late consent withdrawal is
rechecked during persistence so it cannot create a score or queue case after consent is removed.

---

## Counselor queue

The validation-selected model threshold controls queue admission. Probability bands map to display
levels (`Low < 0.25`, `Moderate < 0.50`, `High < 0.75`, otherwise `Critical`), but those display
bands do not replace the learned queue threshold.

There can be only one open queue entry per student. A later qualifying score refreshes its current
probability/features and preserves the peak severity reached by the open case. The queue sorts by
severity descending and age ascending. Counselor/Admin resolution is audited and protected by a
row-version token; stale or duplicate actions do not silently overwrite another counselor's work.

The authorized route is `GET /Counselor/Queue`, with the anti-forgery-protected resolve action at
`POST /Counselor/Resolve`.

---

## Background jobs and real-time updates

`WeeklyRiskScoringJob` is implemented. By default it would run Mondays at 02:00 UTC (`0 2 * * 1`),
with non-overlap protection and bounded retries, but it is registered only when a validated runtime
artifact is available. If ML is disabled or unavailable, the recurring risk job is removed.

The following scheduled jobs are still scaffolds and should not be treated as working:

- `NudgeNotificationJob` delegates to an unimplemented nudge service.
- `BookingReminderJob` throws `NotImplementedException`.
- `ForumModerationJob` throws `NotImplementedException`.
- `CrisisResourceEscalationJob` throws `NotImplementedException`.

When scoring creates or escalates a case, the Web implementation broadcasts a payload-free
`QueueUpdated` SignalR event through `CounselorQueueHub`. Clients then reload the authorized queue;
student identity, probability, and features are never placed in the broadcast payload.

The Hangfire dashboard is mapped at `/hangfire` only when `Startup:UseHangfire=true` and requires the
Admin access policy.

---

## Database migrations

EF Core migrations are owned by `Lodestone.Infrastructure`:

```bash
dotnet ef migrations add <MigrationName> --project src/Lodestone.Infrastructure --startup-project src/Lodestone.Web
dotnet ef database update --project src/Lodestone.Infrastructure --startup-project src/Lodestone.Web
```

`20260829032603_ConsentGatedRiskMonitoring` adds the consent, consent-history, snapshot, scoring-run,
student-number claim, score provenance, and queue-concurrency schema. Its approved upgrade policy is
privacy-first and intentionally destructive for legacy monitoring data:

- Deletes all pre-consent `RiskQueueEntries`, `RiskScores`, and `ActivityLogs`.
- Converts valid existing profile student numbers into pending Admin-review claims.
- Clears every profile's verified `StudentNumber`; no legacy mapping is implicitly trusted.

The generated SQL was validated and the migration was applied successfully to the local
`DESKTOP-F5ATA0B\SQLEXPRESS` `Lodestone` database. The verified post-migration state is zero legacy
activity logs, scores, queue entries, verified profile numbers, and consented students, plus one
pending claim created from the valid legacy number. The new tables are present and
`20260829032603` is the latest migration-history entry. The migration's `Down` path cannot
reconstruct the purged legacy data.

---

## Tests

Run the complete suite:

```bash
dotnet test Lodestone.sln
```

Or run focused projects:

```bash
dotnet test tests/Lodestone.UnitTests
dotnet test tests/Lodestone.IntegrationTests
dotnet test tests/Lodestone.MLTests
```

The risk/ML tests cover OULAD parsing and leakage boundaries, grouped splitting, metric and threshold
selection, quality-gated publication, runtime artifact validation, consent/revocation, snapshot
eligibility and deduplication, idempotent scoring, queue behavior, verified student numbers,
concurrency outcomes, controller authorization, and health-endpoint contracts.

The latest full verification completed with 116 passing tests: 56 Unit, 38 Integration, and 22 ML.

---

## Privacy and security

- Monitoring is explicit opt-in, not opt-out.
- A claimed LMS identifier is not trusted until Admin verification.
- Withdrawal and Admin reset purge derived monitoring data.
- Imports are aggregated behavioral features, not journal/forum/counseling text.
- Risk is used only for human support routing, never diagnosis, grading, or discipline.
- Counselor queue broadcasts carry no confidential payload.
- Sensitive actions require authorization, anti-forgery protection, audit entries, and concurrency
  checks where stale writes matter.
- Secrets belong in User Secrets/environment variables; never commit SMTP/admin credentials.
- Use least-privilege database and Hangfire-dashboard access in production.

---

## Current status

- Consent-gated import/scoring, verified student-number workflow, risk persistence, counselor queue,
  Admin operations UI, trainer CLI, and fail-closed runtime integration are implemented in source.
- The consent/risk migration is SQL-validated and applied to the local SQL Express `Lodestone`
  database; the privacy-first purge and pending-claim conversion were verified directly.
- The official OULAD dataset is present locally, but both the SDCA baseline and current FastTree run
  failed the fixed quality gate, so no runtime artifact exists and ML remains disabled by default.
- The full build and all 116 tests pass. The ML health endpoint is healthy while intentionally
  disabled and fail-closed when enabled without an accepted artifact.
- Unrelated nudge, reminder/moderation/escalation jobs, PDF report generators, and the generic
  analytics Dashboard view remain incomplete.

---

## Troubleshooting

| Issue | Likely cause and action |
| --- | --- |
| Database connection fails | Check both SQL Server connection strings, instance availability, and certificate/encryption settings. |
| A future migration fails | Generate and inspect its SQL, back up affected data, and verify the configured SQL Server instance before retrying `database update`. |
| `/health/ml` says healthy while disabled | Expected: the subsystem is intentionally off. Inspect the JSON description. |
| `/health/ml` is unhealthy after enabling ML | Model/metadata is missing, corrupt, incompatible, or fails hash/schema checks. Do not bypass the check; retrain or restore a validated pair. |
| Weekly risk job is absent | It is registered only when the model status is available. Check `/health/ml` and startup logs. |
| Snapshot row is rejected | Verify Admin-approved student-number mapping, active consent, exact CSV header/schema, UTC window end, 28 observed days, feature ranges, and age <= 8 days. |
| Training exits with code 3 | The validation threshold or untouched test quality gate failed. Inspect the versioned failed report; no artifact should be enabled. |
| Hangfire emits failures from other jobs | Several unrelated recurring jobs remain explicit stubs; disable Hangfire for isolated ML health tests. |
| Browser still shows an old Razor view | Development uses Razor runtime compilation, but static CSS/JS may require a hard refresh. |

---

## License and academic use

Lodestone is an academic/capstone project licensed under the [MIT License](LICENSE). OULAD is a
separate dataset distributed by its authors/UCI under CC BY 4.0; follow its attribution and license
terms when redistributing or publishing derived work.
