# Lodestone — Final Project Report (Full Context for Document Generation)

> Purpose of this file: complete, verified source material for generating the final project report (DOCX).
> Every figure below was checked against the codebase and running system on 2026‑09‑09.
> Suggested document structure follows the numbered chapters. The ML system (Chapters 6–9) is the emphasised core.

---

## 0. Front matter (fill in)

- **Project title:** Lodestone — A Consent‑Gated Student Wellbeing Platform with Human‑Reviewed Withdrawal‑Risk Analytics
- **Course / module:** SD Project 3.2
- **Author(s), supervisor, institution, date:** *(to fill)*
- **Repository branch:** `NewFeatures` (three feature commits on 2026‑09‑08/09 on top of the earlier work)

**Abstract (draft):**
Lodestone is an ASP.NET Core 8 web platform that gives university students private wellbeing tools (mood journal, peer forum, counsellor booking, crisis resources, peer‑volunteer chat) and gives counsellors a triage workspace. Its distinguishing component is a *consent‑gated* machine‑learning pipeline: students explicitly opt in to weekly monitoring, an administrator verifies their LMS student number, behavioural activity summaries are imported, and a LightGBM model trained on the public OULAD dataset produces a 28‑day withdrawal‑risk score that routes students to human counsellors — never to automated action. The model is published only after fixed quality gates on a validation set and a single locked test evaluation (test ROC AUC 0.753, recall 0.693, precision 0.053); the runtime loads it fail‑closed with SHA‑256‑bound metadata and manifest. Every prediction is explainable on demand by black‑box ablation, model‑wide feature importance is recorded at training time by permutation, and score drift between runs is monitored with a population stability index. The system has 360 automated tests (212 unit, 83 ML, 65 integration), 12 EF Core migrations, and runs in Docker.

---

## 1. Introduction

### 1.1 Problem statement
Students who disengage from their studies often do so silently: they stop logging in, miss deadlines, and withdraw before anyone notices. Self‑report channels (counselling appointments, help desks) reach only students who already ask. Learning‑analytics research shows that platform activity carries early signals of withdrawal, but naïve deployments risk surveillance, stigma, biased flags and automated decisions about vulnerable people.

### 1.2 Objectives (verbatim from proposal, verified)
1. Provide student support flows (journal, forum, crisis resources, booking, optional manual prompts).
2. Require explicit monitoring consent and Admin‑verified LMS/student‑number mapping.
3. Train withdrawal‑risk models from OULAD using leakage‑safe behavioural features only.
4. Publish runtime models only after fixed validation + locked‑test gates pass.
5. Keep every prediction behind human review in staff‑only operational views.

### 1.3 Scope boundaries (deliberately excluded)
No mental‑health diagnosis, no grade or disciplinary consequences, no automatic crisis escalation, no contacting emergency services, no automatic risk‑driven nudges. Excluded data: journal text, counselling notes, crisis search text, peer‑chat text, forum text, grades, demographics, final outcomes, and any activity after the observation anchor.

### 1.4 Users and roles
`Student`, `Volunteer` (trained peer supporter), `Counselor`, `Admin`. Roles are ASP.NET Core Identity roles; authorization policies: `CanViewRiskQueue` (Counselor/Admin), `CanAccessAdmin`, `CanManageVolunteers`, `CanRequestPeerSupport` (Student), `CanProvidePeerSupport` (Volunteer), `CanModerateForum` (Volunteer/Counselor/Admin).

---

## 2. System overview

### 2.1 Architecture (Clean Architecture)
| Project | Responsibility |
|---|---|
| `Lodestone.Domain` | Entities (28), enums (13), constants. Framework‑neutral. |
| `Lodestone.Shared` | Result types, helpers (`RiskLevelHelper`), exceptions. |
| `Lodestone.Application` | Use‑case services, DTOs, validators, repository/service interfaces. No EF, no ML.NET, no web. Contains the framework‑neutral `IRiskModelPredictor`, `IRiskModelExplainer`, `RiskModelDescriptor`, `RiskScoreDistribution`. |
| `Lodestone.Infrastructure` | EF Core 8 + SQL Server repositories, Identity, Data Protection, email, report data provider. |
| `Lodestone.ML` | OULAD loading, feature engineering, training, evaluation, fairness audit, artifact validation, `LoadedRiskModelPredictor`, `AblationRiskExplainer`. |
| `Lodestone.Jobs` | Hangfire jobs: weekly scoring, booking reminders, forum triage, crisis escalation, counsellor digest. |
| `Lodestone.Reporting` | QuestPDF report templates (risk summary, student engagement, counsellor session). |
| `Lodestone.Web` | ASP.NET Core MVC + Razor, SignalR hubs, health checks, security middleware, composition root. |
| `tools/Lodestone.ModelTrainer` | CLI: `download`, `train`, `experiment-v2`, `experiment-v3`, `analyze`, `audit-fairness`. |
| `tests/*` | UnitTests (212), MLTests (83), IntegrationTests (65). |

Dependency rule: Domain ← Application ← {Infrastructure, ML, Jobs, Reporting} ← Web. The Application layer defines every interface; outer layers implement them. ML.NET never leaks into Application.

### 2.2 Technology stack
.NET 8.0 · ASP.NET Core MVC 8.0.30 · Razor · SignalR · EF Core 8.0.30 (SQL Server, `.\SQLEXPRESS` locally) · ASP.NET Core Identity · ML.NET 3.0.1 with FastTree and LightGBM trainers · Hangfire 1.8.25 (SQL Server storage) · QuestPDF 2024.3 · FluentValidation 11.9 · xUnit / Moq / FluentAssertions / EF InMemory / WebApplicationFactory · Docker + docker‑compose · hand‑written CSS and vanilla JS (jQuery only for unobtrusive validation, from jsDelivr).

### 2.3 Security & privacy engineering
- HTTPS + HSTS, CSP, COOP/CORP, X‑Content‑Type‑Options, Permissions‑Policy headers.
- Rate limiting on authentication endpoints (10 requests / 10 min).
- Journal notes encrypted at rest with ASP.NET Core Data Protection; `JournalNoteProtectionMigrator` upgrades legacy rows; keys persisted to `keys/`.
- Anti‑forgery tokens on every POST; role + policy authorization on every controller.
- Audit log (`AuditLog` entity) for consent changes, imports, scoring runs, exports, case resolutions, prompts, escalations.
- Health endpoints: `/health/live`, `/health/ready`, `/health/ml` (`RiskModelHealthCheck`).
- ML disabled by default (`MachineLearning:Enabled=false`); enabling requires a valid artifact or the readiness probe fails.

### 2.4 Data model (entities)
`ApplicationUser` → `StudentProfile` | `CounselorProfile` | `VolunteerProfile`.
Student‑owned: `MoodJournalEntry`, `StudentNumberClaim`, `RiskMonitoringConsent` (+ `RiskMonitoringConsentHistory`), `StudentNudgePreference`, `Nudge`, `ActivityLog`.
Risk: `RiskFeatureSnapshot` → `RiskScore` (→ `RiskScoringRun`) → `RiskQueueEntry`.
Support: `CounselorAvailabilitySlot`, `CounselorBooking`, `CounselorSessionReport`, `CrisisResource`.
Community: `ForumCategory`, `ForumPost`, `ForumComment`, `ForumFlag`.
Peer support: `VolunteerAssignment`, `SupportRequest`, `SupportInteraction`.
System: `Notification`, `AuditLog`.

Enums: `BookingStatus`, `ForumPostStatus`, `NotificationType`, `NudgeStatus`, `ReportStatus`, `RiskCaseResolution`, `RiskLevel` (Low/Moderate/High/Critical), `RiskScoringRunStatus`, `StudentNumberClaimStatus`, `SupportInteractionType`, `SupportRequestCategory`, `SupportRequestStatus`, `UserRole`.

### 2.5 Database migrations (12, in order)
InitialCreate → EnforceDailyJournalEntries → CompleteStudentExperience → ConsentGatedRiskMonitoring → RuntimeMlV2AndManualNudges → BookingReminderTracking → VolunteerPeerSupport → RuntimeMlV3Snapshots → ForumTriageReviewMarker → PeerChatReadMarkersVolunteerAwayNudgeBooking → RiskCaseResolutionReasons → PeerEscalationHandoff. Applied automatically at startup by `DbInitializer`.

---

## 3. Functional features — Student

1. **Dashboard** (`/Student`): quick actions, planner tabs, snapshot of recent activity, support pathways, *Your peer volunteers*, *Optional support prompts*, privacy section.
2. **Mood journal**: one entry per day (DB‑enforced), 1–10 mood, optional note encrypted at rest. Never read by any ML or staff view.
3. **Peer forum**: categories, posts, comments, community flags. Moderation by humans; triage ranking only orders the queue.
4. **Counsellor booking**: browse published availability slots, book, cancel; 24‑hour reminder email; booking notes to the counsellor. Opening the form from an escalated peer request pre‑fills a note.
5. **Crisis resources** (public, no login): emergency and continued‑support cards plus a *Find a resource* search (BM25 over resource text, POST so the query never enters a URL or log; emergency resources always shown).
6. **Peer volunteers**: dashboard card per assigned volunteer (role, bio, department, skills, availability, *Away until…*), **Start a conversation** (opens a private chat without filing a request) or **Continue conversation** with unread badge; *My Requests* page lists pending/active/history with "N new" badges.
7. **Peer‑support requests**: category + message + availability → visible only to volunteers assigned to that student; live SignalR chat once accepted; escalation card with **Book a counselor session** and "A counselor has picked this up" once acknowledged.
8. **Optional support prompts (nudges)**: student toggle; counsellor‑authored neutral templates only; Acknowledge / Snooze 7 days / Dismiss.
9. **Privacy controls**: submit student number for admin verification; **Weekly support monitoring** consent toggle (withdrawal deletes activity logs, snapshots, scores and queue entries in one serializable transaction); **What monitoring holds about you** transparency panel (snapshots stored, courses, period, imports, summaries scored, last run, whether a counsellor check‑in was suggested — counts and dates only, never a score).

## 4. Functional features — Volunteer, Counsellor, Admin

**Volunteer**: complete profile (name, department, skills, availability, bio) → admin approval; dashboard with pending/active/history; accept / decline / complete / escalate; live chat; **I'm available / Mark me away (until date + note)**; unread badges; *Awaiting counselor* / *Counselor following up* badges on escalated history; forum moderation access.

**Counsellor**: **Support queue** (open risk cases with level, scores, measured behaviours, live SignalR updates), **Why this score** explanation page, **Resolve case** with outcome (`Contacted`, `NoConcern`, `Referred`, `StudentDeclined`, `Unreachable`) + note, **Peer‑support handoffs** panel (volunteer escalations with the volunteer's note; "I'll follow up" acknowledgement notifies the student), **Appointments** (awaiting outcome / upcoming / recent, record outcome + session notes with template drafter, send optional prompt with per‑prompt outcome status), **Availability** management, weekly **digest email** (count‑only).

**Admin**: dashboard shell with sections Dashboard, RiskMonitoring, CounselorBookings, ForumModeration, Students, Counselors, Volunteers, Users, Notifications, AuditLogs, Profile. Student‑number claim approval; volunteer invite/approve/activate/assign (with `VolunteerMatcher` top‑3 suggestions for unrouted requests); **Risk model operations** page (status cards, CSV import with template download, *Run scoring now*, PDF risk summary report, audit trail with latest run, **Export results (CSV)**, **Drift check**); counsellor provisioning; forum moderation.

## 5. Background jobs (Hangfire)
| Job id | Default cron | What it does |
|---|---|---|
| `weekly-risk-scoring` | `0 2 * * 1` (Mon 02:00 UTC) | `RiskScoringService.RunPendingSnapshotsAsync` — same idempotent batch as the admin button. Registered only when `MachineLearning:Enabled`. |
| `booking-reminders` | `0 7 * * *` | Emails students 24 h before a confirmed session; stamps booking first to avoid duplicates. |
| `forum-moderation` | `0 8 * * *` | Notifies moderators of the backlog count. |
| `crisis-escalation` | `0 */6 * * *` | Notifies staff when high/critical cases wait > 24 h (counts only). |
| `counselor-digest` | `0 8 * * 1` | Emails each counsellor: open cases (urgent/stale), waiting escalations, their prompt outcomes. Count‑only; skips quiet weeks. |
`nudge-dispatch` is deliberately removed (manual prompts are visible immediately).

---

# PART II — THE MACHINE‑LEARNING SYSTEM (emphasis)

## 6. Problem formulation and data

### 6.1 The prediction task
One signal only: **probability that a student withdraws within the next 28 days, given the previous 28 days of platform behaviour.** A ranking aid for counsellors, not a classifier of people.

### 6.2 Dataset — OULAD
Open University Learning Analytics Dataset (UCI, SHA‑256 `f2ed1902…d3e4`). Seven tables joined by `OuladDataLoader`: `courses`, `studentInfo`, `studentRegistration`, `studentAssessment`, `studentVle`, `assessments`, `vle`. Demographics in `studentInfo` are used **only** by the offline fairness audit, never as features.

### 6.3 Rolling observations
Constants: `ObservationWindowDays = 28`, `PredictionWindowDays = 28`, `ObservationStrideDays = 7`.
For each enrolment an *anchor* day slides every 7 days from `registration + 28` to `courseLength − 28`. Features use activity in `[anchor − 27, anchor]`; the label looks at `(anchor, anchor + 28]`.

**Label:** `IsAtRisk = unregistration_day > anchor AND unregistration_day ≤ anchor + 28`.

**Exclusions:** courses shorter than 56 days; enrolments missing a registration row; enrolments whose `final_result = Withdrawn` contradicts a missing unregistration date.

**Leakage prevention:** only clicks, due dates and submissions dated `≤ anchor` are used; unregistration dates feed the label only; cohort percentiles are fitted on training students only.

### 6.4 Feature schema `withdrawal-28d-v3` (17 runtime features)
| # | Feature | Definition |
|---|---|---|
| 1 | RecentActiveDayRate | active days in days 15–28 ÷ 14 |
| 2 | PriorActiveDayRate | active days in days 1–14 ÷ 14 |
| 3 | ActiveDayRateTrend | (1) − (2) |
| 4 | RecentCourseClickRate | course clicks days 15–28 ÷ 14 |
| 5 | PriorCourseClickRate | course clicks days 1–14 ÷ 14 |
| 6 | CourseClickRateTrend | (4) − (5) |
| 7 | InactivityStreakDays | trailing run of inactive days ending at anchor |
| 8 | AssessmentDueRate | assessments due in window ÷ 28 |
| 9 | AssessmentOnTimeRate | on‑time ÷ due (0 if none due) |
| 10 | AssessmentLateOrMissingRate | late or missing ÷ due |
| 11 | CourseProgressRatio | min((anchor+1) ÷ courseLength, 1) |
| 12 | CohortActivityPercentile | mid‑rank percentile of RecentActiveDayRate within (course, anchor) cohort, fitted on training students (`CohortFeatureCalibrator`), with course‑wide then global fallback |
| 13 | ActivityTrendAcceleration | second difference of active‑day rate across window thirds |
| 14 | ClickVolatility | population SD of daily total clicks over 28 days |
| 15 | ForumEngagementShare | forum clicks ÷ (forum + course clicks) |
| 16 | InactiveWeekRate | weeks with zero activity ÷ 4 |
| 17 | AssessmentMissStreak | consecutive most‑recent assessments late/missing |

Legacy v1 (6 features: ActiveDayRate, ActivitySpanDays, DaysSinceLastAccess, ForumInteractionCount, CourseInteractionCount, LateOrMissingAssignmentCount) and v4 experimental "Prior assessment" features (6, offline only) also exist; runtime accepts v1–v3 and the loaded model's schema decides which CSVs import.

### 6.5 Class balance
Base rate ≈ 2.5 %. `GroupDataSplitter.ApplyBalancedClassWeights`: `positiveWeight = N / (2·P)`, `negativeWeight = N / (2·Q)` so both classes carry equal total weight; passed to trainers via `ExampleWeightColumnName`.

## 7. Training methodology

### 7.1 Splitting (`GroupDataSplitter.Split`)
Grouped by **student** (a student's enrolments never straddle partitions), stratified by whether the student ever has a positive label, Fisher‑Yates shuffled with decorrelated seeds, 70 / 15 / 15 → **17,339 / 3,714 / 3,718 students; 504,023 / 107,501 / 108,238 rows**. Seed `20260901`. Student‑set SHA‑256 hashes recorded in the training report so the partition is reproducible and the fairness audit can assert it.

### 7.2 Feature pipeline (`FeatureEngineering.BuildPipeline`)
`Concatenate(RawFeatures)` → `NormalizeMeanVariance(Features)` fitted on training rows only. `CohortFeatureCalibrator` fitted on training rows, frozen, applied to validation/test.

### 7.3 Candidate grid (`ModelTrainingCandidate.V2Candidates`, 8 bounded candidates)
FastTree: (200,31,10,0.05) (300,31,20,0.1) (400,63,10,0.05) (400,63,20,0.1); LightGBM: (200,15,10,0.05) (300,31,20,0.05) (400,63,10,0.03) (400,63,20,0.05) as (iterations, leaves, minExamplesPerLeaf, learningRate). No free‑form hyper‑parameters from the CLI.

### 7.4 Grouped 3‑fold cross‑validation (`GroupedCrossValidator`)
Folds assigned per student; calibrator refit per fold; mean AUC / PR‑AUC / recall / precision / F1 recorded; ranked by mean AUC with a 0.001 tie tolerance, then precision.

### 7.5 Validation selection (`TrainingPipeline.SelectOnValidation`)
For ranked candidates: train, then `ModelEvaluator.SelectThreshold` = **maximise F1 subject to recall ≥ 0.65 + 0.03 margin and precision ≥ 0.05**; evaluate on validation; first candidate passing all gates wins.

### 7.6 Quality gates (`ModelQualityGates`, fixed in code)
`MinimumAreaUnderRocCurve = 0.70`, `MinimumRecall = 0.65`, `MinimumPrecision = 0.05`. Applied to validation, then **once** to the locked test partition. Failure at either stage writes a failure report and throws `ModelQualityGateException`; nothing is published. (This "State B" behaviour was exercised: the earlier v2 attempt failed the precision gate and was correctly not published.)

### 7.7 Post‑selection measurements (validation only)
- `PermutationImportanceCalculator.Compute` — for each of the 17 features, shuffle it across validation rows 3×, re‑score, average the ROC‑AUC drop; normalise positive drops to shares.
- `RiskScoreDistribution.From(validationScores)` — 10 fixed bins [0,0.1)…[0.9,1], mean; the drift baseline.
- `BuildThresholdCurve` (101 points) and per‑feature train→validation/test PSI (`FeatureDriftSummary`) into the training report.

### 7.8 Publication (`TrainingPipeline` → staged files → atomic move)
Writes `risk-model.zip`, `risk-model.metadata.json`, `risk-model.publication.json`, `risk-model.report.json` to temporary siblings, verifies reload parity on validation data, computes SHA‑256 of model and metadata, then moves all into `src/Lodestone.Web/App_Data/ml/`. Manifest binds `PublicationId`, `ModelSha256`, `MetadataSha256`, feature names, temporal constants, algorithm and the quality‑gate result.

### 7.9 Result — published artifact (retrained 2026‑09‑08, verified)
- **Model version:** `withdrawal-28d-v3-20260908T202849801Z`, algorithm **LightGBM** (400 iterations, 63 leaves, 20 min/leaf, LR 0.05, 1 thread), seed 20260901, publication id `10d2572f…3bb5b`.
- **Decision (publication) threshold:** 0.4629.
- **Validation:** ROC AUC **0.732**, PR‑AUC 0.070, recall **0.680**, precision **0.0505**, F1 0.094, Brier 0.174, false alerts/100 student‑weeks 32.3 (TP 1,848 · FP 34,724 · TN 70,061 · FN 868).
- **Locked test:** ROC AUC **0.753**, PR‑AUC 0.078, recall **0.693**, precision **0.0532**, F1 0.099, Brier 0.170, false alerts/100 31.5, **mean lead time 14.7 days** (TP 1,916 · FP 34,094 · TN 71,380 · FN 848).
- **Permutation importance (validation, share of AUC loss):** CohortActivityPercentile 30.7 %, CourseProgressRatio 18.3 %, AssessmentLateOrMissingRate 12.0 %, RecentActiveDayRate 11.6 %, ActiveDayRateTrend 4.3 %, RecentCourseClickRate 4.1 %, InactivityStreakDays 3.3 %, ClickVolatility 2.9 %, remaining nine < 3 % each.
- **Validation score distribution (bins 0.0→1.0):** 17.7 %, 16.4 %, 13.9 %, 11.5 %, 10.2 %, 10.9 %, 10.8 %, 6.6 %, 1.9 %, 0 %; mean 0.349; 107,501 rows.

**Honest reading:** ~2× lift over the 2.5 % base rate. At the publication threshold about a third of student‑weeks would be flagged, so the deployed **queue threshold is 0.83** (a capacity decision in `appsettings.json`, not a model property): ≈0.7 % of student‑weeks selected, recall 4.5 %, precision 15.2 % — roughly 23 students a week per 3,700, about 1 in 6.5 genuinely at risk, and 95 % of eventual withdrawals *not* flagged. The model is a ranking aid; the report and UI say so.

### 7.10 Fairness audit (`audit-fairness`, offline)
Reconstructs the exact locked‑test partition (asserting the student hash), scores it with the published model, joins OULAD demographics, and reports selection rate, recall, precision, FPR, AUC and disparate‑impact ratios per group for gender, age band, IMD deprivation band, disability, highest education, region; groups < 500 rows or < 20 positives are suppressed. Findings at the deployed 0.83 threshold showed large recall gaps (15–18×) between groups — documented as an accepted limitation because the production system deliberately stores **no** demographic data and therefore cannot audit itself; mitigation is human review of every flag.

### 7.11 Diagnostic tooling
`analyze` (`ThresholdAnalyzer`): per‑candidate 201‑point threshold curve on validation plus best‑precision‑at‑recall‑floor for floors 0.02–0.90; never touches test.

## 8. Runtime ML — from artifact to counsellor queue

### 8.1 Fail‑closed loading (`LoadedRiskModelPredictor.TryLoad`, once at startup, immutable singleton)
1. `MachineLearning:Enabled` false → predictor unavailable, `/health/ml` Healthy("disabled").
2. Model, metadata, manifest files must exist.
3. Metadata: schema `risk-model-metadata-v2`; non‑empty version/schema/features; publication id ≤ 80 chars; `EligibleForRuntimeIntegration`; validation **and** test metrics re‑checked against `ModelQualityGates`.
4. Manifest: schema `risk-model-publication-v1`; eligible; `QualityGate.Passed/ValidationPassed/TestPassed`; gate minima not below the code constants; publication time present.
5. Cross‑checks: publication id, model version, schema, ordered feature names, model SHA‑256, algorithm identical in both documents.
6. SHA‑256 of `risk-model.zip` equals both documents; SHA‑256 of metadata equals manifest.
7. Load ML.NET model, create prediction engine, probe with a zero vector (finite probability in [0,1]).
8. Resolve queue threshold (config override 0.83, else artifact threshold).
9. Build `RiskModelDescriptor` (version, schema, 28 days, threshold, feature names, publication id, **feature importance**, **training score distribution**).
Any failure → `RiskModelStatus.Unavailable(reason)`; `/health/ml` and `/health/ready` Unhealthy; scoring refuses to run. Verified 2026‑09‑09: `/health/ml` → `Healthy`, model `withdrawal-28d-v3-20260908T202849801Z`.

### 8.2 Snapshot intake (`RiskSnapshotAdministrationService.ImportCsvAsync`)
RFC 4180 CSV, ≤ 25 MB, ≤ 50,000 rows, ≤ 200 reported errors. Header must match exactly one registered schema equal to the loaded model's. Base columns `StudentNumber, CourseKey, WindowEndUtc, ObservedDays, FeatureSchemaVersion` + schema features. Any malformed row rejects the file. Per row: student number must resolve to an **active, admin‑verified, consented** student; exact duplicates (student, course, window) skipped. Import records file SHA‑256 and an audit entry.

### 8.3 Scoring batch (`RiskScoringService.RunPendingSnapshotsAsync`) — same code path for the weekly job and "Run scoring now"
1. Validate descriptor; fetch pending snapshot ids: same schema and `ObservedDays`, `WindowEndUtc` within `MaximumSnapshotAgeDays = 8`, student active + verified + consented, **no existing `RiskScore` for this model version** (idempotency).
2. Open a `RiskScoringRun` (run key, model/schema version, candidate count, actor).
3. Per snapshot: re‑check eligibility, map to `RiskModelInput`, validate finiteness/sign, predict, band with `RiskThresholdConstants` (Low < 0.25 ≤ Moderate < 0.50 ≤ High < 0.75 ≤ Critical), persist in a serializable transaction (`RiskScoreRepository.PersistAsync`): re‑check consent, insert `RiskScore`, and if probability ≥ **queue threshold 0.83** create the student's single open `RiskQueueEntry` or escalate its level. Failures are caught per snapshot.
4. Close the run: counts scored/skipped/failed/queue‑created/queue‑escalated, status Completed/PartiallyCompleted/Failed, failure summary; SignalR notifies counsellor queues.

### 8.4 Counsellor review
Queue shows level, scores, measured behaviours, timestamps; **Why this score** (below); **Resolve case** with reason + note (audited, concurrency‑safe via row version). Consent withdrawal purges the student's snapshots, scores and cases.

### 8.5 Explainability (`AblationRiskExplainer` + `RiskExplanationService`)
- Baseline: per‑feature **median** of the most recent snapshot of every consenting student in the same schema (≥ 5 students required).
- For each feature, replace the student's value with the baseline, re‑predict, contribution = actual − counterfactual; skip features already at baseline. Black‑box, so it survives any algorithm change; ~17 predictions per explanation; nothing persisted.
- `BoundaryNote` states whether removing the largest raising factor would put the score below the queue threshold — phrased as a property of the model, never as advice.
- **Model‑wide importance** (new): the descriptor's training‑time permutation importance is shown as a *Model‑wide rank* column and a ranked panel ("What the model relies on in general"); for artifacts without it, the service estimates importance as mean |ablation effect| across up to 150 population snapshots and labels the source.
- Vocabulary (`RiskFeatureVocabulary.Describe`) renders counsellor‑readable phrases; no causal language.

### 8.6 Drift monitoring (new)
`RiskSnapshotAdministrationService.ComputeDriftAsync` after each run (≥ 30 scored rows): current run histogram vs. baseline — the artifact's validation score distribution when model versions match, otherwise up to 5,000 prior scores from the same model. **PSI** = Σ (aᵢ − eᵢ)·ln(aᵢ/eᵢ) over 10 fixed bins; severity Stable < 0.10 ≤ Moderate < 0.25 ≤ Significant. Admin page shows a badge, side‑by‑side histogram, mean shift, share ≥ queue threshold, and advice text.

### 8.7 Audit & export
Every run is recorded (`RiskScoringRun`); **Export results (CSV)** streams per‑snapshot rows (run key, model, student number, course, window, probability, level, scored time, queued?) and writes a `RiskScoringRun.Exported` audit row. **Risk summary PDF** (QuestPDF): scoring volume, level distribution, case flow (opened/resolved/open, median time to resolve, resolution‑reason breakdown), top‑20 highest scores — student numbers only, no names.

### 8.8 Student transparency
Students see counts and dates only (snapshots, courses, period, imports, scored summaries, last run, whether a counsellor check‑in was suggested and whether it was reviewed). Scores and bands are never shown to students because a score is not a diagnosis and could be misread.

## 9. Other local, inspectable algorithms (no LLMs, nothing leaves the process)
| Component | Method | Key parameters |
|---|---|---|
| `CrisisResourceRetriever` | BM25 over title×2 + description + phone; query‑expansion lexicon (everyday words → resource vocabulary); stop‑words | k₁ = 1.2, b = 0.75; results verbatim; query never stored; emergency resources always shown |
| `VolunteerMatcher` | weighted score = 0.55·skill overlap + 0.25·availability overlap + 0.20·capacity | full workload = 5 assignments; recommends top‑3, admin decides; reasons listed; student text not quoted |
| `ForumTriageRanker` | 0.40 reported + 0.25 unanswered 24 h + 0.15 new author + 0.10 length anomaly + 0.10 age (saturates 7 d) | orders the moderation queue; humans decide |
| `SessionReportDrafter` | template from structured booking fields | no text generation; counsellor fills the blanks |
| `AblationRiskExplainer` | see 8.5 | — |

## 10. AI governance principles (docs/AI-GOVERNANCE.md, summarised)
1 one signal (28‑day withdrawal) · 2 behavioural features only · 3 human‑in‑the‑loop, no automated contact/moderation/escalation · 4 opt‑in consent, withdrawal deletes data, admin‑verified identity · 5 on‑demand ablation explanations, no causal claims, nothing persisted · 6 offline fairness audit on six attributes · 7 accepted limitation: no production demographics · 8 fixed gates, locked test, fail‑closed loading · 9 queue threshold is a capacity decision, off by default · 10 deliberately not built: self‑harm classifier, generative chat, local LLM, third‑party text inference · 11 structural forum triage · 12 recommending (not assigning) volunteer matcher · 13 BM25 crisis search with verbatim results · 14 template session notes.

---

## 11. Implementation timeline (from migration and commit history)
July 2026 initial schema and journal → late Aug: student experience, consent‑gated monitoring → 31 Aug: runtime ML v2 + manual nudges → 1 Sep: booking reminders → 4 Sep: volunteer peer support + SignalR chat → 5 Sep: v3 schema and published LightGBM model → 8 Sep: forum triage marker; peer‑chat read markers, volunteer availability, nudge outcomes; case resolution reasons; escalation handoff; run export; counsellor digest; model‑wide importance; drift monitor; retrained artifact.

## 12. Testing and verification (verified 2026‑09‑09)
- **Unit tests 212** (services, validators, jobs, scheduler, web controllers, distribution/PSI maths).
- **ML tests 83** (data loader, splitter, evaluator, pipeline gates, ablation explainer, fairness metrics, permutation importance, model availability).
- **Integration tests 65** (repositories incl. concurrency‑safe resolve with reasons, DB init, auth paths).
- Release build: 0 errors. `dotnet ef` model in sync with migrations. `/health/ml` Healthy with the retrained artifact. Docker compose validates.
- Manual verification in browser: crisis search, student dashboard sections, direct volunteer conversation, unread badges, resolve form, drift panel.

## 13. Results and discussion
- The pipeline reproduces bit‑for‑bit with a fixed seed (retraining reproduced AUC 0.753 / recall 0.693 / precision 0.053 exactly) — reproducibility is a first‑class property.
- Permutation importance confirms the model leans on relative cohort activity (30.7 %), course progress (18.3 %) and assessment lateness (12.0 %) — behaviours counsellors recognise, not proxies for identity.
- Precision is low by construction of the task (2.5 % base rate); the deployed operating point trades recall for a queue a counselling service can actually work (≈ 23 students/week per 3,700).
- Fail‑closed loading and fixed gates mean a bad model cannot be deployed by accident; an earlier v2 candidate was correctly rejected.
- Fairness gaps at the deployed threshold are real and documented; human review of every flag and the absence of demographic data in production are the mitigations, not a solution.

## 14. Limitations and future work
- OULAD is a UK distance‑learning dataset; transfer to the target institution's LMS needs local retraining and re‑gating.
- Fairness cannot be monitored in production without collecting protected attributes (a deliberate trade‑off).
- Drift monitor compares score distributions only; feature‑level PSI exists offline but not yet at runtime.
- Email delivery (reminders, digest) requires SMTP configuration; locally it logs.
- Further work: calibration (Brier 0.17 suggests over‑confident probabilities), counterfactual‑free explanations (e.g. SHAP) as a comparison, longer prediction windows, and an institution‑specific feature schema (v4 assessment‑history features showed promise offline).

## 15. Conclusion
Lodestone demonstrates that learning analytics can be built *around* consent, verification, explainability, quality gates and human judgement rather than bolted on afterwards. The ML component is modest by design — a 17‑feature LightGBM ranking model with AUC 0.75 — but every step from OULAD row to counsellor queue is reproducible, auditable, fail‑closed and explainable, and every action a student experiences is taken by a person.

---

## Appendix A — How to run
```
# database: SQL Server Express (.\SQLEXPRESS); migrations apply on start
dotnet run --project src/Lodestone.Web            # https://localhost:5001
# enable ML for a demo
$env:MachineLearning__Enabled="true"
# retrain (OULAD in src/Lodestone.ML/Data/OULAD)
dotnet run --project tools/Lodestone.ModelTrainer -c Release -- experiment-v3 --seed 20260901
# fairness audit
dotnet run --project tools/Lodestone.ModelTrainer -- audit-fairness --partition test --queue-threshold 0.83
# tests
dotnet test tests/Lodestone.UnitTests; dotnet test tests/Lodestone.MLTests; dotnet test tests/Lodestone.IntegrationTests
```

## Appendix B — Key configuration (`appsettings.json`)
`MachineLearning: { Enabled: false, ModelPath: App_Data/ml/risk-model.zip, QueueThreshold: 0.83 }` · `RiskScoring: { Cron: "0 2 * * 1" }` · `MaintenanceJobs: { BookingReminders 0 7 * * *, ForumModeration 0 8 * * *, CrisisEscalation 0 */6 * * *, CounselorDigest 0 8 * * 1 }`.

## Appendix C — Suggested figures for the DOCX
1. Clean‑architecture layer diagram (Section 2.1). 2. ER diagram (2.4). 3. Rolling‑window timeline: 28‑day observation → anchor → 28‑day prediction (6.3). 4. Training pipeline flowchart: load → split → calibrate → CV → select → gates → locked test → PFI/distribution → publish (7). 5. Fail‑closed load checklist (8.1). 6. Scoring sequence diagram: import → pending → run → score → band → queue → counsellor (8.3). 7. Bar chart of permutation importance (7.9). 8. Validation score histogram (7.9). 9. Screenshots: Risk model operations, Support queue, Why this score, Student transparency panel, Volunteer dashboard.
