<div align="center">

<img src="https://capsule-render.vercel.app/api?type=waving&color=0:020617,45:0f172a,75:1e293b,100:312e81&height=240&section=header&text=LODESTONE&fontSize=62&fontColor=ffffff&fontAlignY=38&desc=Consent-Gated%20Runtime%20ML%20%7C%20Student%20Wellbeing%20%7C%20Explainable%20AI&descAlignY=62&descSize=17&animation=fadeIn" width="100%"/>

<br>

<img src="https://readme-typing-svg.demolab.com?font=JetBrains+Mono&weight=600&size=20&duration=2800&pause=900&color=38BDF8&center=true&vCenter=true&width=950&lines=Privacy-first+student+support+platform;Consent-gated+behavioral+risk+monitoring;Explainable+machine+learning;Quality-gated+model+publication;Fail-closed+runtime+loading;Human-in-the-loop+counselor+support" alt="Lodestone animated introduction"/>

<br><br>

<img src="https://img.shields.io/badge/STATE-E%20%E2%80%94%20PUBLISHED%20%26%20AUDITED-22C55E?style=for-the-badge"/>
<img src="https://img.shields.io/badge/TESTS-360%20PASSING-38BDF8?style=for-the-badge"/>
<img src="https://img.shields.io/badge/.NET-8-512BD4?style=for-the-badge&logo=dotnet&logoColor=white"/>
<img src="https://img.shields.io/badge/ML.NET-LightGBM%20%7C%20FastTree-0078D4?style=for-the-badge"/>

<br><br>

**Lodestone is not a diagnostic or clinical system.**

**ML predictions are support-routing signals only.**

</div>

---

# ◈ Overview

Lodestone is an **ASP.NET Core MVC student-wellbeing application** that combines student-owned support tools with privacy-controlled learning analytics.

It provides a validated withdrawal-risk signal that can be routed to human counselors while keeping student consent, privacy, explainability, and human judgement at the center of the system.

<div align="center">

<img src="https://readme-typing-svg.demolab.com?font=JetBrains+Mono&size=17&duration=2300&pause=700&color=94A3B8&center=true&vCenter=true&width=900&lines=Student+Support+%E2%86%92+Consent+%E2%86%92+Behavioral+Signals+%E2%86%92+ML+%E2%86%92+Human+Review;Train+%E2%86%92+Validate+%E2%86%92+Audit+%E2%86%92+Publish+%E2%86%92+Monitor" alt="Lodestone workflow animation"/>

</div>

The system deliberately avoids turning ML predictions into automated decisions.

Risk predictions never:

* diagnose students
* change grades
* discipline students
* open crisis cases automatically
* contact emergency services
* send automatic risk-based nudges

---

# ◈ Table of Contents

* [Overview](#-overview)
* [Why Lodestone](#-why-lodestone)
* [Core Principles](#-core-principles)
* [Current Status](#-current-status)
* [What Changed](#-what-changed-in-the-final-iteration)
* [Implemented Product Areas](#-implemented-product-areas)
* [ML System](#-ml-system)
* [Model Pipeline](#-model-pipeline)
* [Model Quality Gates](#-model-quality-gates)
* [Model Performance](#-model-performance)
* [Explainable Predictions](#-explainable-predictions)
* [Drift Monitoring](#-drift-monitoring)
* [Fairness Audit](#-fairness-audit)
* [Forum Moderation](#-forum-moderation-triage)
* [Volunteer Matching](#-volunteer-matching)
* [Peer Support](#-peer-support-and-escalation-handoff)
* [Background Jobs](#-background-jobs-and-real-time-updates)
* [Health Endpoints](#-health-endpoints)
* [Architecture](#-architecture)
* [Technology](#-technology)
* [Getting Started](#-getting-started)
* [Runtime ML Configuration](#-runtime-ml-configuration)
* [Consent and Student Identity](#-consent-and-student-identity)
* [OULAD Training](#-oulad-training)
* [Runtime Snapshot Import](#-runtime-snapshot-import)
* [Database Migrations](#-database-migrations)
* [Deployment](#-deployment)
* [Testing](#-tests)
* [Privacy and Security](#-privacy-and-security)
* [Troubleshooting](#-troubleshooting)
* [Documentation](#-documentation)
* [License](#-license)

---

# ◈ Why Lodestone

Student-support systems often have to balance two competing needs:

```text
Useful Early Signals
        +
Student Privacy
        +
Human Judgement
```

Lodestone is designed around all three.

```text
                         ┌──────────────────────┐
                         │       STUDENT        │
                         │                      │
                         │ Support · Privacy    │
                         │ Consent · Community  │
                         └──────────┬───────────┘
                                    │
                             Explicit Opt-In
                                    │
                                    ▼
                         ┌──────────────────────┐
                         │ Behavioral Snapshot  │
                         │                      │
                         │ Aggregate Features   │
                         └──────────┬───────────┘
                                    │
                                    ▼
                         ┌──────────────────────┐
                         │   Validated ML Model │
                         │                      │
                         │ withdrawal-28d-v3   │
                         └──────────┬───────────┘
                                    │
                                    ▼
                         ┌──────────────────────┐
                         │   Counselor Queue    │
                         │                      │
                         │ Human Review         │
                         └──────────┬───────────┘
                                    │
                                    ▼
                         ┌──────────────────────┐
                         │       SUPPORT        │
                         │                      │
                         │ Counselor / Volunteer│
                         └──────────────────────┘
```

---

# ◈ Core Principles

| Principle                 | Implementation                                       |
| ------------------------- | ---------------------------------------------------- |
| Student consent           | Monitoring requires explicit opt-in                  |
| Identity verification     | LMS number requires Admin approval                   |
| Data minimization         | Runtime ML uses aggregate behavioral features        |
| Human oversight           | ML only routes attention                             |
| Explainability            | Predictions can be explained on demand               |
| Fail closed               | Invalid artifacts never execute                      |
| Auditability              | Publication and scoring are auditable                |
| Privacy                   | Derived monitoring data is deleted after withdrawal  |
| No automated intervention | ML cannot automatically contact students             |
| Offline fairness          | Protected attributes are used only in offline audits |

---

# ◈ Current Status

<div align="center">

<img src="https://img.shields.io/badge/STATE-E%3A%20PUBLISHED%20%2B%20EXPLAINED%20%2B%20AUDITED-22C55E?style=for-the-badge"/>

<br><br>

<img src="https://readme-typing-svg.demolab.com?font=JetBrains+Mono&weight=600&size=17&duration=2200&pause=700&color=38BDF8&center=true&vCenter=true&width=850&lines=Published+v3+model;Runtime+explainability;Permutation+feature+importance;Population+drift+monitoring;Offline+fairness+audit;Fail-closed+model+loading" alt="Current status animation"/>

</div>

Lodestone is currently at:

> **State E: A published, explained, audited and drift-monitored v3 model; peer support, counselor and admin workflows complete.**

The seventeen-feature `withdrawal-28d-v3` model passed its fixed gates and is published under:

```text
src/Lodestone.Web/App_Data/ml/
```

The artifact was retrained on **2026-09-08** with an identical deterministic seed so it now records:

* permutation feature importance
* validation score distribution
* runtime drift reference data

### Verification

| Area                 |        Result |
| -------------------- | ------------: |
| Unit Tests           |       **212** |
| Integration Tests    |        **65** |
| ML Tests             |        **83** |
| Total Tests          |       **360** |
| Release Build Errors |         **0** |
| Runtime Model        |        **v3** |
| Explainability       | **Available** |
| Drift Monitoring     | **Available** |
| Fairness Audit       | **Available** |
| Fail-Closed Loading  |  **Enforced** |

Tracked configuration intentionally keeps:

```text
MachineLearning:Enabled=false
```

because the model artifacts are git-ignored.

Each environment must explicitly enable runtime scoring.

---

# ◈ What Changed In The Final Iteration

**2026-09-08 → 2026-09-09**

| Area           | Change                                                                        |
| -------------- | ----------------------------------------------------------------------------- |
| Peer support   | Students see assigned volunteers and can start private conversations directly |
| Messaging      | Unread badges for students and volunteers                                     |
| Volunteers     | Away status with return date and optional note                                |
| Routing        | Away volunteers excluded from routing suggestions                             |
| Escalation     | Counselor peer-support handoff panel                                          |
| Counselor      | "I'll follow up" acknowledgement workflow                                     |
| Booking        | Escalated students receive prefilled counselor-session booking                |
| Nudges         | Manual nudges linked to originating appointments                              |
| Risk cases     | Resolution requires a recorded reason                                         |
| Reports        | Median time-to-resolve and resolution breakdown                               |
| Transparency   | Student monitoring transparency panel                                         |
| Admin ML       | CSV scoring-run export                                                        |
| Drift          | PSI-based drift monitoring with histograms                                    |
| Explainability | Model-wide feature ranking                                                    |
| Jobs           | New count-only weekly CounselorDigestJob                                      |
| Schema         | Three new privacy/support workflow migrations                                 |

---

# ◈ Implemented Product Areas

## Student

* Registration
* Login
* Role redirects
* Dashboard
* Private mood journal
* Encrypted journal notes
* One journal entry per day
* Crisis resources
* BM25 resource search
* Peer forum
* Counselor booking
* 24-hour reminders
* Privacy controls
* Monitoring transparency

## Peer Support

* Admin-assigned volunteers
* Student-initiated support requests
* Direct volunteer conversations
* Private SignalR chat
* Unread message markers
* Volunteer availability
* Away status
* Escalation to counselors
* Counselor handoff acknowledgement

## Counselor

* Live support queue
* Risk cases
* On-demand prediction explanations
* Feature importance
* Peer escalation handoffs
* Appointment management
* Session notes
* Manual neutral prompts
* Prompt outcome tracking
* Weekly digest email

## Admin

* LMS/student-number verification
* Snapshot imports
* Runtime scoring
* Scoring-run history
* CSV exports
* ML health status
* Drift analysis
* Fairness audit
* Operational monitoring
* Audit records

---

# ◈ ML System

Lodestone treats ML as a **first-class application capability**, while keeping ML.NET isolated from the rest of the system.

```text
┌───────────────────────────────────────────┐
│          Lodestone.Application             │
│                                           │
│          IRiskModelPredictor               │
└──────────────────────┬────────────────────┘
                       │
                       ▼
┌───────────────────────────────────────────┐
│              Lodestone.ML                 │
│                                           │
│ Feature Engineering                       │
│ Training                                  │
│ Evaluation                                │
│ Fairness Audit                            │
│ Artifact Validation                       │
│ Prediction                                │
│ Explainability                            │
│ Drift Reference                           │
└───────────────────────────────────────────┘
```

**ML.NET never leaves `Lodestone.ML`.**

The application depends only on the framework-neutral `IRiskModelPredictor` abstraction.

---

# ◈ Model Pipeline

<div align="center">

<img src="https://capsule-render.vercel.app/api?type=rect&color=0:020617,50:172554,100:312e81&height=75&section=header&text=TRAIN%20%E2%86%92%20VALIDATE%20%E2%86%92%20AUDIT%20%E2%86%92%20PUBLISH%20%E2%86%92%20MONITOR&fontSize=20&fontColor=38BDF8&animation=fadeIn" width="90%"/>

</div>

```text
                           OULAD
                             │
                             ▼
                    Data Validation
                             │
                             ▼
                   Feature Engineering
                             │
                             ▼
                    Training Candidates
                             │
                             ▼
                   Grouped Cross-Validation
                             │
                             ▼
                    Validation Selection
                             │
                     ┌───────┴───────┐
                     │               │
                    FAIL            PASS
                     │               │
                     ▼               ▼
                   Reject        Locked Test
                                     │
                                     ▼
                              Fairness Audit
                                     │
                                     ▼
                              Artifact Hashing
                                     │
                                     ▼
                              Manifest Check
                                     │
                                     ▼
                          Atomic Publication
                                     │
                                     ▼
                            Runtime Scoring
                                     │
                                     ▼
                            Drift Monitoring
```

---

# ◈ Model Quality Gates

A model is not considered valid merely because it trained successfully.

The fixed validation gates are:

```text
Validation ROC AUC   ≥ 0.70
Validation Recall    ≥ 0.65
Validation Precision ≥ 0.05
```

The training system:

* uses deterministic student-grouped 70/15/15 splitting
* uses seed `20260901`
* fits calibration on training data only
* fits feature normalization on training data only
* tunes eight bounded FastTree and LightGBM candidates
* uses grouped 3-fold cross-validation
* selects operating threshold on validation only
* requires validation gates to pass
* evaluates the locked test partition exactly once
* records permutation importance
* records validation score distribution
* publishes artifacts atomically
* binds artifacts with SHA-256

---

# ◈ Model Performance

## `withdrawal-28d-v3`

<div align="center">

<img src="https://img.shields.io/badge/MODEL-withdrawal--28d--v3-38BDF8?style=for-the-badge"/>
<img src="https://img.shields.io/badge/ALGORITHM-LightGBM-818CF8?style=for-the-badge"/>
<img src="https://img.shields.io/badge/FEATURES-17-A78BFA?style=for-the-badge"/>
<img src="https://img.shields.io/badge/SEED-20260901-F59E0B?style=for-the-badge"/>

</div>

```text
Model version:
withdrawal-28d-v3-20260908T202849801Z

Algorithm:
LightGbm

Iterations:
400

Leaves:
63

Minimum examples per leaf:
20

Learning rate:
0.05

Seed:
20260901
```

### Validation

| Metric      |     Result |
| ----------- | ---------: |
| ROC AUC     |  **0.732** |
| Recall      |  **0.680** |
| Precision   | **0.0505** |
| Brier Score |  **0.174** |

### Locked Test

| Metric             |        Result |
| ------------------ | ------------: |
| ROC AUC            |     **0.753** |
| PR AUC             |     **0.078** |
| Recall             |     **0.693** |
| Precision          |     **0.053** |
| Brier Score        |     **0.170** |
| Mean Lead Time     | **14.7 days** |
| Base Rate          |     **2.55%** |
| Artifact Threshold |    **0.4629** |

The measured signal is real but weak.

At approximately 5.3% precision, most flagged student-weeks are not withdrawals.

Therefore:

> **Lodestone routes attention. It does not make a determination about a student.**

---

# ◈ Operating Point

The configured queue threshold is:

```text
MachineLearning:QueueThreshold = 0.83
```

This operating point was selected for precision and produces a very small queue.

The fairness audit measured approximately:

```text
Recall = 4.5%
```

At this threshold, the queue is a **capacity-bounded triage aid** rather than a safety net.

At the model artifact's threshold:

```text
Recall = 69.3%
```

but approximately one-third of student-weeks would be flagged.

The threshold should be revisited when actual counselor capacity is known.

---

# ◈ Explainable Predictions

A counselor should be able to interrogate a model signal rather than simply receive a number.

Lodestone uses `AblationRiskExplainer`.

```text
                Original Prediction
                        │
                        ▼
                Select Feature
                        │
                        ▼
              Replace with Reference
                        │
                        ▼
                    Re-score
                        │
                        ▼
              Calculate Difference
                        │
                        ▼
               Feature Contribution
```

The contribution is measured using the model's own output.

### Constraints

* explanations are never persisted
* explanations are generated on demand
* feature language is centrally controlled
* no causal claims are made
* no feature is described as attendance, grades, marks, exams, or lectures
* older artifacts can use population-ablation fallback

---

# ◈ Model-Wide Feature Importance

The v3 artifact records permutation feature importance.

| Feature                     | AUC-loss share |
| --------------------------- | -------------: |
| CohortActivityPercentile    |      **30.7%** |
| CourseProgressRatio         |      **18.3%** |
| AssessmentLateOrMissingRate |      **12.0%** |
| RecentActiveDayRate         |      **11.6%** |
| ActiveDayRateTrend          |       **4.3%** |
| RecentCourseClickRate       |       **4.1%** |
| InactivityStreakDays        |       **3.3%** |
| ClickVolatility             |       **2.9%** |

The counselor interface shows:

```text
Individual Student
       +
Model-Wide Importance
```

This distinguishes:

> What affected this student's prediction?

from:

> What does the model generally rely on?

---

# ◈ Drift Monitoring

After every scoring run, Lodestone compares the score distribution with a reference distribution.

The system uses **Population Stability Index (PSI)**.

|           PSI | Status      |
| ------------: | ----------- |
|      `< 0.10` | Stable      |
| `0.10 – 0.25` | Moderate    |
|      `≥ 0.25` | Significant |

The Admin ML operations page displays:

* PSI severity
* current histogram
* reference histogram
* mean shift
* percentage above queue threshold
* reference source
* scoring-run status

Drift never changes cases automatically.

It creates an operational signal for human inspection.

---

# ◈ Fairness Audit

Protected attributes are intentionally excluded from runtime ML.

The offline audit evaluates:

* gender
* age band
* deprivation band
* disability
* prior education
* region

These attributes:

```text
OULAD
  │
  ▼
Offline Audit
  │
  ├── Never Runtime Features
  ├── Never Stored
  ├── Never Imported
  └── Never Published
```

The audit reconstructs the exact evaluation partition from the model's seed and verifies the student hash.

This prevents evaluating a lookalike population instead of the actual locked test partition.

The audit evaluates every operating point rather than only the published threshold.

At the configured threshold, the gender selection-rate ratio was measured at approximately:

```text
0.434
```

compared with approximately:

```text
0.870
```

at the artifact threshold.

The documented limitation is important:

> A production deployment that stores no protected attributes cannot directly measure its own subgroup performance.

---

# ◈ Forum Moderation Triage

Lodestone does **not** use a distress or self-harm classifier.

`ForumTriageRanker` orders posts according to observable operational signals:

* unresolved community reports
* no replies after 24 hours
* first-time or infrequent author
* unusual post length relative to the author's own history
* time since last review

Length is compared against the author's own median rather than the entire forum.

Reported posts form their own review tier ahead of inferred ranking signals.

The ranker:

* does not classify content
* does not assign emotional categories
* does not hide posts
* does not notify authors
* does not change post status

It only helps moderators decide what to read first.

---

# ◈ Volunteer Matching

`VolunteerMatcher` ranks approved active volunteers using:

```text
Declared Skills
       +
Department / Bio Overlap
       +
Availability
       +
Current Workload
```

Capacity has deliberately limited influence.

Everything is:

* local
* deterministic
* inspectable
* auditable

No:

* LLM
* embedding service
* external AI API
* semantic inference

The matcher suggests candidates.

**An administrator makes the assignment.**

---

# ◈ Peer Support And Escalation Handoff

```text
                         STUDENT
                            │
             ┌──────────────┴──────────────┐
             │                             │
       Support Request              Direct Chat
             │                             │
             └──────────────┬──────────────┘
                            │
                            ▼
                    ASSIGNED VOLUNTEER
                            │
                     ┌──────┴──────┐
                     │             │
                  Support       Escalate
                     │             │
                     │             ▼
                     │         COUNSELOR
                     │             │
                     │       "I'll follow up"
                     │             │
                     └─────────────┴──────►
                              STUDENT
```

Students can:

* request support
* directly message assigned volunteers
* see volunteer availability
* see away status

Volunteers can:

* provide support
* mark themselves away
* escalate to counselors

Counselors can:

* view escalation handoffs
* acknowledge handoffs
* add counselors-only notes
* notify students
* facilitate session booking

Private conversations are not copied into counselor escalations.

Only the volunteer's escalation note is passed forward.

---

# ◈ Background Jobs And Real-Time Updates

| Job                   | Default Schedule | Behaviour                     |
| --------------------- | ---------------- | ----------------------------- |
| `weekly-risk-scoring` | Monday 02:00 UTC | Weekly scoring                |
| `booking-reminders`   | Daily 07:00      | Upcoming appointment emails   |
| `forum-moderation`    | Daily 08:00      | Moderator triage count        |
| `crisis-escalation`   | Every 6 hours    | Staff stale-case notification |
| `counselor-digest`    | Monday 08:00     | Count-only counselor summary  |

The `nudge-dispatch` job is intentionally removed.

Risk scoring never:

* creates crisis cases automatically
* contacts external emergency services
* sends risk-based student nudges

### SignalR

```text
                    SERVER
                       │
        ┌──────────────┼───────────────┐
        │              │               │
        ▼              ▼               ▼
CounselorQueueHub PeerSupportHub PeerChatHub
        │              │               │
        ▼              ▼               ▼
 QueueUpdated      Support       Private Chat
```

Sensitive details are not broadcast unnecessarily.

Clients reload authorized information from the server.

---

# ◈ Health Endpoints

| Endpoint        | Meaning                 |
| --------------- | ----------------------- |
| `/health/live`  | Process liveness        |
| `/health/ml`    | ML status               |
| `/health/ready` | Database + ML readiness |

### ML Disabled

```text
/health/ml
     │
     ▼
Healthy
Disabled
```

### Invalid Model

```text
Model Invalid
     │
     ├── /health/ml → Unhealthy
     ├── /health/ready → Unhealthy
     ├── Scoring → Disabled
     └── Weekly Job → Removed
```

### Valid Model

```text
Validated Artifact
       │
       ▼
Model Loaded
       │
       ▼
Scoring Available
```

---

# ◈ Architecture

Lodestone follows **Clean Architecture**.

```text
                         ┌───────────────────────┐
                         │     Lodestone.Web     │
                         │ MVC · Razor · SignalR │
                         │ Health · Composition  │
                         └───────────┬───────────┘
                                     │
                                     ▼
                         ┌───────────────────────┐
                         │ Lodestone.Application │
                         │ Use Cases · DTOs      │
                         │ Validation · Interfaces│
                         └───────┬───────┬───────┘
                                 │       │
                    ┌────────────┘       └────────────┐
                    ▼                                 ▼
        ┌──────────────────────┐          ┌──────────────────────┐
        │ Infrastructure       │          │ Lodestone.ML         │
        │ EF Core              │          │ Training             │
        │ SQL Server            │          │ Prediction           │
        │ Identity              │          │ Explainability       │
        │ Security              │          │ Fairness             │
        └──────────────────────┘          └──────────────────────┘
                    │                                 │
                    ▼                                 ▼
              SQL Server                       ML Artifacts

                         ┌───────────────────────┐
                         │    Lodestone.Jobs     │
                         │      Hangfire         │
                         └───────────────────────┘

                         ┌───────────────────────┐
                         │ Lodestone.Reporting   │
                         │       QuestPDF        │
                         └───────────────────────┘
```

---

# ◈ Project Responsibilities

| Project                        | Responsibility                                            |
| ------------------------------ | --------------------------------------------------------- |
| `Lodestone.Domain`             | Entities, enums, constants, core state                    |
| `Lodestone.Application`        | Use cases, DTOs, validation, framework-neutral interfaces |
| `Lodestone.Infrastructure`     | EF Core, SQL Server, Identity, email, security            |
| `Lodestone.ML`                 | OULAD, training, prediction, fairness, explainability     |
| `Lodestone.Jobs`               | Hangfire jobs and scheduling                              |
| `Lodestone.Reporting`          | QuestPDF reports                                          |
| `Lodestone.Web`                | MVC, Razor, SignalR, health endpoints                     |
| `tools/Lodestone.ModelTrainer` | Download, experiments, threshold analysis, fairness audit |

---

# ◈ Technology

<div align="center">

<img src="https://skillicons.dev/icons?i=dotnet,cs,html,css,js,sql,docker,git,github&perline=9" />

<br><br>

<img src="https://readme-typing-svg.demolab.com?font=JetBrains+Mono&size=16&duration=2500&pause=700&color=38BDF8&center=true&vCenter=true&width=850&lines=ASP.NET+Core+MVC;Entity+Framework+Core+8;SQL+Server;ML.NET+%7C+LightGBM+%7C+FastTree;Hangfire+%7C+SignalR;QuestPDF+%7C+xUnit+%7C+Moq+%7C+FluentAssertions" alt="Technology stack animation"/>

</div>

| Concern          | Technology                           |
| ---------------- | ------------------------------------ |
| Web              | ASP.NET Core MVC, Razor              |
| Runtime          | .NET 8                               |
| Data             | Entity Framework Core 8, SQL Server  |
| Authentication   | ASP.NET Core Identity                |
| ML               | ML.NET                               |
| Algorithms       | FastTree, LightGBM                   |
| Jobs             | Hangfire                             |
| Real-Time        | SignalR                              |
| Reports          | QuestPDF                             |
| Frontend         | Hand-written CSS, Vanilla JavaScript |
| Testing          | xUnit                                |
| Mocking          | Moq                                  |
| Assertions       | FluentAssertions                     |
| Integration      | WebApplicationFactory                |
| Database Testing | EF Core InMemory                     |
| Deployment       | Developer machine + public tunnel    |
| Containers       | Docker                               |

---

# ◈ Getting Started

## Prerequisites

* .NET 8 SDK or later
* SQL Server or SQL Server Express
* Git
* Optional Docker

## Restore

```bash
dotnet restore Lodestone.sln
```

## Build

```bash
dotnet build Lodestone.sln
```

## Run

```bash
dotnet run --project src/Lodestone.Web
```

The local application binds to:

```text
http://localhost:5000
https://localhost:5001
```

Migrations are normally applied automatically by `DbInitializer`.

`dotnet ef database update` is only required when:

```text
Startup__InitializeDatabase=false
```

---

# ◈ Database-Independent Startup

For a startup smoke test:

```powershell
$env:Startup__InitializeDatabase = "false"
$env:Startup__UseHangfire = "false"

dotnet run --project src/Lodestone.Web
```

---

# ◈ Runtime ML Configuration

Tracked defaults intentionally keep ML disabled.

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
    "BookingReminders": {
      "Enabled": true,
      "Cron": "0 7 * * *"
    },

    "ForumModeration": {
      "Enabled": true,
      "Cron": "0 8 * * *"
    },

    "CrisisEscalation": {
      "Enabled": true,
      "Cron": "0 */6 * * *"
    },

    "CounselorDigest": {
      "Enabled": true,
      "Cron": "0 8 * * 1"
    }
  }
}
```

---

# ◈ Enable Runtime ML

For a local demo:

```powershell
$env:MachineLearning__Enabled = "true"

dotnet run --project src/Lodestone.Web
```

Use User Secrets or environment variables for credentials.

Never commit secrets.

---

# ◈ ML Artifact Layout

```text
src/Lodestone.Web/App_Data/ml/
│
├── risk-model.zip
├── risk-model.metadata.json
└── risk-model.publication.json
```

These files are git-ignored intentionally.

When ML is disabled, `/health/ml` reports a healthy disabled status.

When enabled with invalid artifacts:

```text
ML = Unhealthy
Scoring = Disabled
Weekly Job = Removed
```

---

# ◈ Consent And Student Identity

Monitoring eligibility requires:

```text
Explicit Student Opt-In
          +
Admin-Verified LMS Mapping
```

A student-supplied LMS number remains an untrusted claim until approved.

Students can manage consent from the privacy area.

### Withdrawal

Consent withdrawal removes:

```text
ActivityLogs
RiskFeatureSnapshots
RiskScores
RiskQueueEntries
```

Consent history and privacy audit records remain.

---

# ◈ Student Transparency

The transparency panel shows:

* snapshots stored
* courses
* covered period
* imports
* summaries scored
* last scoring run
* whether a counselor check-in was suggested

It does not show:

* risk probability
* risk band
* queue position
* model decision
* counselor queue details

---

# ◈ OULAD Training

Lodestone uses the **Open University Learning Analytics Dataset (OULAD)** from the UCI Machine Learning Repository.

## Download

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- download
```

## v2 Experiment

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- experiment-v2
```

## v3 Experiment

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- experiment-v3
```

## Fairness Audit

```bash
dotnet run --project tools/Lodestone.ModelTrainer -- audit-fairness \
  --queue-threshold 0.83 \
  --expect-student-hash <testStudentHash>
```

---

# ◈ v2 Experiment

The v2 experiment failed the fixed quality gate.

Dataset:

```text
Rows:
505,179 train
108,709 validation
108,728 locked test

Students:
17,393 train
3,726 validation
3,729 locked test
```

Best grouped-CV candidates reached ROC AUC around:

```text
0.748
```

However, precision remained around:

```text
0.05
```

which did not satisfy the original gate.

The locked test partition was therefore not evaluated.

No runtime artifact was published.

---

# ◈ v3 Experiment

The v3 model introduced:

* activity acceleration
* click volatility
* forum-engagement share
* weekly inactivity coverage
* assessment-miss streak

The seventeen-feature model remained focused on behavioral activity and assessment timing.

Excluded:

* demographics
* registration information
* grades
* final outcomes
* journal text
* peer-chat text
* forum text
* counseling text
* crisis-case text
* future activity

---

# ◈ Runtime Snapshot Import

Admins import **pre-aggregated weekly behavioral snapshots**.

Raw OULAD rows are not imported into student accounts.

The importer validates:

* active consent
* Admin-approved student number
* exact schema
* duplicate headers
* source provenance
* feature ranges
* UTC timestamps
* duplicate snapshots
* maximum snapshot age
* 28-day observation window

The loaded model schema controls the required snapshot header.

---

# ◈ Database Migrations

Migrations live under:

```text
src/Lodestone.Infrastructure/Data/Migrations
```

Create a migration:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/Lodestone.Infrastructure \
  --startup-project src/Lodestone.Web
```

Update:

```bash
dotnet ef database update \
  --project src/Lodestone.Infrastructure \
  --startup-project src/Lodestone.Web
```

Current migrations:

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

EF reports no pending model changes.

---

# ◈ Deployment

Lodestone is currently deployed from a developer machine through a free public tunnel.

There is no permanent cloud host.

```text
┌───────────────────────┐
│   Developer Machine   │
│                       │
│ ASP.NET Core          │
│ SQL Server            │
│ Hangfire              │
│ ML Artifacts          │
└───────────┬───────────┘
            │
            ▼
      Public Tunnel
            │
            ▼
         Internet
```

The public link exists only while the machine, application and tunnel are running.

---

# ◈ Deployment Prerequisites

* .NET 8 SDK
* SQL Server Express
* `.\SQLEXPRESS`
* Node.js
* `npx localtunnel`
* optional `ngrok`
* three ML artifacts

---

# ◈ Configure Deployment

```bash
dotnet user-secrets set "MachineLearning:Enabled" true \
  --project src/Lodestone.Web

dotnet user-secrets set "SeedData:AdminEmail" "<admin email>" \
  --project src/Lodestone.Web

dotnet user-secrets set "SeedData:AdminPassword" "<strong password>" \
  --project src/Lodestone.Web
```

---

# ◈ Start The Application

```bash
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
ASPNETCORE_ENVIRONMENT=Development \
dotnet run \
  --project src/Lodestone.Web \
  --no-launch-profile \
  --urls "https://localhost:5001;http://localhost:5000"
```

---

# ◈ Public Tunnel

Using LocalTunnel:

```bash
npx localtunnel --port 5000 --subdomain lodestone
```

The resulting URL is typically:

```text
https://lodestone.loca.lt
```

If the requested subdomain is unavailable, LocalTunnel assigns another address.

---

# ◈ Verify Deployment

Open:

```text
/health/ready
```

A healthy deployment should report:

```text
Healthy
```

and include the loaded:

```text
risk-model
```

---

# ◈ Optional Docker

From the repository root:

```bash
docker compose \
  --env-file deployment/docker/.env.example \
  -f deployment/docker/docker-compose.yml \
  up --build
```

Replace every placeholder secret before use.

The Docker setup persists:

* SQL Server
* ASP.NET Data Protection keys
* HTTPS certificates
* optional ML artifacts

Do not use:

```bash
docker compose down -v
```

with real encrypted journal data unless the database and key-ring volumes are backed up.

---

# ◈ Tests

<div align="center">

<img src="https://img.shields.io/badge/UNIT-212-22C55E?style=for-the-badge"/>
<img src="https://img.shields.io/badge/INTEGRATION-65-38BDF8?style=for-the-badge"/>
<img src="https://img.shields.io/badge/ML-83-A78BFA?style=for-the-badge"/>
<img src="https://img.shields.io/badge/TOTAL-360-0EA5E9?style=for-the-badge"/>

<br><br>

<img src="https://readme-typing-svg.demolab.com?font=JetBrains+Mono&weight=600&size=18&duration=2100&pause=700&color=22C55E&center=true&vCenter=true&width=800&lines=360+tests+verified;Release+build%3A+0+errors;EF+Core%3A+no+pending+model+changes;Docker+Compose%3A+validated" alt="Testing animation"/>

</div>

Run:

```bash
dotnet test Lodestone.sln
```

### Latest verified counts

```text
Unit Tests          212
Integration Tests    65
ML Tests             83
────────────────────────
Total               360
```

Additional verification:

* Release solution build: 0 errors
* Retraining reproduced published metrics
* ML artifact passed fail-closed loading
* Feature importance loaded
* Validation score distribution loaded
* Docker Compose configuration validated
* EF reported no pending model changes

---

# ◈ Privacy And Security

Lodestone uses defense-in-depth security.

## Privacy

* Explicit monitoring opt-in
* Consent withdrawal deletion
* Minimal behavioral features
* No protected attributes in runtime ML
* On-demand explanations
* No persisted prediction explanations
* Student-facing transparency panel

## Identity

* ASP.NET Core Identity
* Role-based authorization
* Admin LMS verification
* Row-version protection

## Application Security

* Anti-forgery protection
* Rate limiting
* Sanitized logs
* Protected account links
* Configured public base URL
* Persistent Data Protection keys

## ML Security

```text
Artifact
   │
   ├── Model Hash
   ├── Metadata Hash
   ├── Schema
   ├── Feature Order
   ├── Version
   ├── Window
   ├── Stride
   ├── Manifest
   └── Publication Eligibility
             │
             ▼
        Validation
             │
        ┌────┴────┐
       FAIL      PASS
        │          │
        ▼          ▼
      Reject      Load
```

---

# ◈ Deliberately Not Built

<div align="center">

<img src="https://readme-typing-svg.demolab.com?font=JetBrains+Mono&weight=600&size=17&duration=1800&pause=600&color=F87171&center=true&vCenter=true&width=900&lines=NO+AUTOMATIC+RISK-BASED+NUDGES;NO+SELF-HARM+CLASSIFIER;NO+DISTRESS+CLASSIFIER;NO+GENERATIVE+AI+CHAT;NO+THIRD-PARTY+TEXT+INFERENCE;NO+AUTOMATIC+CRISIS+CREATION;NO+EMERGENCY+SERVICE+CONTACT;NO+GRADE+OR+DISCIPLINARY+DECISIONS" alt="Safety animation"/>

</div>

These are deliberate architectural boundaries.

---

# ◈ Troubleshooting

| Problem                           | Likely Cause / Action                                                   |
| --------------------------------- | ----------------------------------------------------------------------- |
| `/health/ml` says disabled        | Expected when ML is intentionally off                                   |
| `/health/ml` unhealthy            | Artifact, metadata, manifest, schema, hash, gate or loadability failure |
| Weekly risk job absent            | Validated model is unavailable                                          |
| Snapshot import rejected          | Check consent, identity, schema, timestamps, ranges, duplicates and age |
| Training exits with code `3`      | Validation or locked-test gate failed                                   |
| Drift needs 30 rows               | Fewer than 30 scored snapshots or no reference distribution             |
| Build has locked DLLs             | Stop the running `Lodestone.Web` process                                |
| Encrypted journal data disappears | Data Protection key volume was changed or deleted                       |

---

# ◈ Documentation

### AI Governance

```text
docs/AI-GOVERNANCE.md
```

Governance principles enforced by the application.

### Final Project Report

```text
docs/final-report/PROJECT-REPORT-CONTEXT.md
```

Verified chapter-structured project report source with the complete ML walkthrough.

### ML Report

```text
docs/ml-report/README.md
```

Training, evaluation and ML system documentation.

### Architecture

```text
docs/architecture/README.md
```

System architecture documentation.

### ER Diagram

```text
docs/er-diagram/README.md
```

Database and entity relationship documentation.

### Proposal

```text
docs/proposal/README.md
```

Project proposal and objectives.

---

# ◈ Project Structure

```text
Lodestone/
│
├── src/
│   ├── Lodestone.Domain/
│   │
│   ├── Lodestone.Application/
│   │
│   ├── Lodestone.Infrastructure/
│   │
│   ├── Lodestone.ML/
│   │
│   ├── Lodestone.Jobs/
│   │
│   ├── Lodestone.Reporting/
│   │
│   └── Lodestone.Web/
│
├── tools/
│   └── Lodestone.ModelTrainer/
│
├── docs/
│   ├── AI-GOVERNANCE.md
│   ├── architecture/
│   ├── er-diagram/
│   ├── final-report/
│   ├── ml-report/
│   └── proposal/
│
├── deployment/
│   └── docker/
│
├── Lodestone.sln
└── README.md
```

---

# ◈ Limitations

Lodestone explicitly documents its limitations.

### Model limitation

The model has measurable predictive signal but weak precision.

```text
Test Precision ≈ 5.3%
Test Recall    ≈ 69.3%
```

Therefore, most flagged student-weeks are not withdrawals.

### Queue limitation

At the configured threshold:

```text
Queue Threshold = 0.83
Recall ≈ 4.5%
```

The queue is not a comprehensive detection mechanism.

### Fairness limitation

Protected attributes are not stored in production.

Therefore, subgroup performance cannot be continuously measured from production records.

### Deployment limitation

The current public deployment depends on:

```text
Developer Machine
+
Application
+
SQL Server
+
Public Tunnel
```

It is not a production cloud architecture.

---

# ◈ Academic Use

Lodestone is an academic/capstone project exploring:

* privacy-aware student wellbeing systems
* responsible machine learning
* explainable AI
* human-in-the-loop decision support
* model governance
* quality-gated publication
* fairness evaluation
* drift monitoring
* secure web application architecture
* consent-aware data processing

The project demonstrates how ML can provide useful support-routing signals without becoming the final decision-maker.

---

# ◈ License

Lodestone is licensed under the **MIT License**.

```text
LICENSE
```

OULAD is distributed by its authors/UCI under **CC BY 4.0**.

Follow its attribution and licensing requirements when using or redistributing derived work.

---

<div align="center">

<br>

<img src="https://readme-typing-svg.demolab.com?font=JetBrains+Mono&weight=600&size=19&duration=2600&pause=800&color=38BDF8&center=true&vCenter=true&width=900&lines=CONSENT+%E2%86%92+SIGNAL+%E2%86%92+EXPLANATION+%E2%86%92+HUMAN+REVIEW+%E2%86%92+SUPPORT;Privacy+first.+Human+judgement+always.;Built+for+responsible+student+support.;LODESTONE" alt="Lodestone final animation"/>

<br><br>

<img src="https://img.shields.io/badge/360%20TESTS-PASSING-22C55E?style=flat-square"/>
<img src="https://img.shields.io/badge/MODEL-v3-38BDF8?style=flat-square"/>
<img src="https://img.shields.io/badge/EXPLAINABLE-YES-A78BFA?style=flat-square"/>
<img src="https://img.shields.io/badge/DRIFT-MONITORED-F59E0B?style=flat-square"/>
<img src="https://img.shields.io/badge/FAIL--CLOSED-ENFORCED-EF4444?style=flat-square"/>

<br><br>

<img src="https://capsule-render.vercel.app/api?type=waving&color=0:312e81,45:1e293b,75:0f172a,100:020617&height=150&section=footer&animation=fadeIn" width="100%"/>

</div>
