# ER Diagram

This is a simplified view of the current EF Core model around the implemented wellbeing and risk workflows.

```mermaid
erDiagram
    ApplicationUser ||--o| StudentProfile : has
    ApplicationUser ||--o| CounselorProfile : has
    ApplicationUser ||--o| VolunteerProfile : has

    StudentProfile ||--o{ MoodJournalEntry : writes
    StudentProfile ||--o{ CounselorBooking : books
    CounselorProfile ||--o{ CounselorAvailabilitySlot : publishes
    CounselorProfile ||--o{ CounselorBooking : attends

    StudentProfile ||--o{ StudentNumberClaim : submits
    StudentProfile ||--o| RiskMonitoringConsent : chooses
    StudentProfile ||--o{ RiskMonitoringConsentHistory : audits
    StudentProfile ||--o{ RiskFeatureSnapshot : imports
    RiskFeatureSnapshot ||--o{ RiskScore : scores
    RiskScoringRun ||--o{ RiskScore : groups
    StudentProfile ||--o{ RiskQueueEntry : routes
    RiskScore ||--o{ RiskQueueEntry : triggers

    StudentProfile ||--o| StudentNudgePreference : chooses
    StudentProfile ||--o{ Nudge : receives

    ForumCategory ||--o{ ForumPost : contains
    ForumPost ||--o{ ForumComment : contains
    ForumPost ||--o{ ForumFlag : reported_by
```

Key constraints:

- Student-number claims are reviewed before `StudentProfile.StudentNumber` becomes trusted.
- Risk snapshots are unique by student, course, window, and schema.
- Scores are unique by snapshot and model version.
- Open queue cases are constrained to one per student.
- Manual nudges require explicit in-app preference and are independent from ML risk monitoring.
