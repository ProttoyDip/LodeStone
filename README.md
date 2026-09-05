# Lodestone

### Consent-gated runtime ML with quality-gated model publication and fail-closed loading

Lodestone is an ASP.NET Core MVC student-wellbeing application. It combines student-owned support tools with privacy-controlled learning analytics so a validated withdrawal-risk signal can be routed to human counselors.

Behavioral risk monitoring is off unless a student explicitly opts in. A student-supplied LMS number remains an untrusted claim until an Admin approves it. Students can withdraw consent at any time, which disables monitoring and deletes their derived activity logs, feature snapshots, risk scores, and risk-queue records.

Lodestone is not a diagnostic or clinical system. ML predictions are support-routing signals only; they never diagnose, change grades, discipline students, open crisis cases, contact emergency services, or send automatic risk-based nudges.

## Current Status

**State B: Runtime integration complete but no acceptable model.**

- Runtime ML is implemented as a first-class application feature through the Application-owned `IRiskModelPredictor` boundary.
- `MachineLearning:Enabled=true` is supported for local/demo runs only after a validated model, metadata, and publication manifest are present.
- The real OULAD v2 experiment completed on 2026-08-31 and failed the fixed validation gate. No locked-test evaluation was performed and no runtime artifact was published.
- `src/Lodestone.Web/App_Data/ml` contains only `.gitkeep`; tracked config keeps `MachineLearning:Enabled=false`.
- Release build passes with zero warnings. Full tests pass: 84 Unit, 45 Integration, 29 ML, 158 total.
- EF reports no pending model changes after `20260831094114_RuntimeMlV2AndManualNudges`.

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

Run v2 experiment:

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- experiment-v2
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

## V2 Experiment Result

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

- Unit: 84
- Integration: 45
- ML: 29
- Total: 158

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
