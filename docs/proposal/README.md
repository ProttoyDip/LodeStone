# Proposal

## Project Aim

Lodestone is a student-wellbeing platform for academic settings. It combines student-owned support tools with consent-gated behavioral early-warning operations so staff can notice possible withdrawal risk and route it to human counselors.

The ML component is a runtime application feature. It is designed as consent-gated runtime ML with quality-gated model publication and fail-closed loading.

## Problem

Students often disengage before they ask for help. Traditional self-report channels miss students who do not complete surveys, journal, or proactively contact support. Lodestone addresses that gap without turning private wellbeing tools into model features.

## Objectives

- Provide useful student support flows: journal, peer forum, crisis resources, booking, and optional manual prompts.
- Require explicit monitoring consent and Admin-verified LMS/student-number mapping before learning snapshots can attach to an account.
- Train and evaluate withdrawal-risk models from OULAD using leakage-safe behavioral features only.
- Publish a runtime model only after fixed validation and locked-test quality gates pass.
- Keep every prediction behind human review and staff-only operational views.

## Boundaries

Lodestone does not diagnose mental-health conditions, change grades, discipline students, automatically open crisis cases, contact emergency services, or send automatic risk-based nudges. Journal text, counseling notes, crisis-case text, peer-chat/forum text, grades, scores, demographics, final outcomes, and future activity are excluded from ML.

## Current Outcome

The application infrastructure is complete for runtime ML, but the real v2 OULAD experiment failed the fixed validation gate. This is State B: no acceptable model exists, no artifact is published, and runtime ML remains disabled by default.
