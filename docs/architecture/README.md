# Architecture

## Overview

Lodestone uses Clean Architecture. Domain and Application hold core rules and interfaces; Infrastructure, ML, Jobs, and Web implement outer concerns.

```mermaid
flowchart LR
    Web[Lodestone.Web]
    Jobs[Lodestone.Jobs]
    ML[Lodestone.ML]
    Infra[Lodestone.Infrastructure]
    App[Lodestone.Application]
    Domain[Lodestone.Domain]

    Web --> App
    Jobs --> App
    ML --> App
    Infra --> App
    App --> Domain
    Infra --> Domain
    ML --> Domain
```

## Runtime ML Flow

```mermaid
sequenceDiagram
    participant Admin
    participant Web
    participant App as Application
    participant Infra as Infrastructure
    participant ML as Lodestone.ML
    participant Counselor

    Admin->>Web: Import weekly snapshot CSV
    Web->>App: IRiskSnapshotAdministrationService
    App->>Infra: Persist eligible consented + verified snapshots
    Admin->>Web: Run scoring or wait for weekly job
    Web->>App: IRiskScoringService
    App->>ML: IRiskModelPredictor
    ML-->>App: Probability only if artifact is valid
    App->>Infra: Persist score and queue mutation
    Infra-->>Web: QueueUpdated event
    Counselor->>Web: Reload authorized queue
```

`IRiskModelPredictor` is Application-owned, so ML.NET stays outside the inner layers. The Web app composes the implementation and exposes health/status without leaking artifact paths.

## Fail-Closed Behavior

When ML is disabled, the app reports an intentional disabled state and does not score. When ML is enabled but artifacts are missing, corrupt, hash-invalid, schema-invalid, version-invalid, gate-invalid, or unloadable, `/health/ml` and readiness become unhealthy and scoring is blocked.

## Privacy Architecture

Risk monitoring requires active consent and Admin-verified student number mapping at import time and again at scoring/persistence time. Consent withdrawal deletes derived monitoring data. Students can see their consent and verification state, but never risk probabilities or queue records.
