# Viva Notes

## Core Explanation

Lodestone is a consent-gated student support platform. It combines direct support tools with runtime ML infrastructure for withdrawal-risk routing, but every risk signal remains staff-only and human-reviewed.

## What Is Complete

- Student, Counselor, and Admin role journeys are implemented.
- Consent and Admin-verified student numbers are mandatory for monitoring.
- Runtime ML integration is complete and fail-closed.
- Manual counselor nudges exist, but they are separate from ML and require student opt-in.
- Privacy withdrawal deletes derived monitoring data.

## ML Acceptance Answer

The correct final state is State B: runtime integration complete but no acceptable model. The v2 real-data OULAD experiment failed validation precision. The locked test set was not evaluated and no artifact was published.

This is a successful safety outcome for the software: it proves the app will not use a weak, synthetic, corrupt, stale, or unvalidated model just to make ML appear operational.

## Ethics Points

- No journal, counseling, crisis, peer-chat, forum text, grades, scores, demographics, final outcomes, or future activity are ML features.
- No prediction creates a clinical conclusion.
- No prediction contacts emergency services or opens a crisis case.
- No prediction changes grades or discipline.
- No automatic risk-based nudge is enabled.

## Verification Points

- 158 tests pass.
- Release build has 0 warnings and 0 errors.
- EF has no pending model changes.
- Docker Compose config validates for local/demo use.
- `App_Data/ml` has no accepted model artifacts.
