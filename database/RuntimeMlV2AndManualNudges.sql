BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] DROP CONSTRAINT [CK_RiskFeatureSnapshots_Features];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    DROP INDEX [IX_Nudges_StudentProfileId] ON [Nudges];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [ActiveDayRateTrend] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [AssessmentDueRate] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [AssessmentLateOrMissingRate] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [AssessmentOnTimeRate] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [CohortActivityPercentile] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [CourseClickRateTrend] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [CourseProgressRatio] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [InactivityStreakDays] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [PriorActiveDayRate] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [PriorCourseClickRate] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [RecentActiveDayRate] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [RiskFeatureSnapshots] ADD [RecentCourseClickRate] real NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [Nudges] ADD [AcknowledgedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [Nudges] ADD [AvailableAtUtc] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME());
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [Nudges] ADD [DismissedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [Nudges] ADD [ExpiresAtUtc] datetime2 NOT NULL DEFAULT (DATEADD(day, 14, SYSUTCDATETIME()));
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [Nudges] ADD [IsManualCounselorNudge] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [Nudges] ADD [SnoozedUntilUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    ALTER TABLE [MoodJournalEntries] ADD [NoteProtectionVersion] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    CREATE TABLE [StudentNudgePreferences] (
        [Id] int NOT NULL IDENTITY,
        [StudentProfileId] int NOT NULL,
        [IsInAppNudgesEnabled] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] nvarchar(450) NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] nvarchar(450) NULL,
        CONSTRAINT [PK_StudentNudgePreferences] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StudentNudgePreferences_StudentProfiles_StudentProfileId] FOREIGN KEY ([StudentProfileId]) REFERENCES [StudentProfiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    EXEC(N'ALTER TABLE [RiskFeatureSnapshots] ADD CONSTRAINT [CK_RiskFeatureSnapshots_Features] CHECK (([FeatureSchemaVersion] = ''withdrawal-28d-v1'' AND [ActiveDayRate] >= 0 AND [ActiveDayRate] <= 1 AND [ActivitySpanDays] >= 0 AND [ActivitySpanDays] <= [ObservedDays] AND [DaysSinceLastAccess] >= 0 AND [DaysSinceLastAccess] <= [ObservedDays] AND [ForumInteractionCount] >= 0 AND [CourseInteractionCount] >= 0 AND [LateOrMissingAssignmentCount] >= 0) OR ([FeatureSchemaVersion] = ''withdrawal-28d-v2'' AND [RecentActiveDayRate] IS NOT NULL AND [PriorActiveDayRate] IS NOT NULL AND [ActiveDayRateTrend] IS NOT NULL AND [RecentCourseClickRate] IS NOT NULL AND [PriorCourseClickRate] IS NOT NULL AND [CourseClickRateTrend] IS NOT NULL AND [InactivityStreakDays] IS NOT NULL AND [AssessmentDueRate] IS NOT NULL AND [AssessmentOnTimeRate] IS NOT NULL AND [AssessmentLateOrMissingRate] IS NOT NULL AND [CourseProgressRatio] IS NOT NULL AND [CohortActivityPercentile] IS NOT NULL AND [RecentActiveDayRate] BETWEEN 0 AND 1 AND [PriorActiveDayRate] BETWEEN 0 AND 1 AND [ActiveDayRateTrend] BETWEEN -1 AND 1 AND [RecentCourseClickRate] >= 0 AND [PriorCourseClickRate] >= 0 AND [InactivityStreakDays] BETWEEN 0 AND [ObservedDays] AND [AssessmentDueRate] >= 0 AND [AssessmentOnTimeRate] BETWEEN 0 AND 1 AND [AssessmentLateOrMissingRate] BETWEEN 0 AND 1 AND [CourseProgressRatio] BETWEEN 0 AND 1 AND [CohortActivityPercentile] BETWEEN 0 AND 1))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    CREATE INDEX [IX_Nudges_StudentProfileId_Status_AvailableAtUtc] ON [Nudges] ([StudentProfileId], [Status], [AvailableAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    EXEC(N'ALTER TABLE [Nudges] ADD CONSTRAINT [CK_Nudges_VisibilityWindow] CHECK ([ExpiresAtUtc] > [AvailableAtUtc])');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    EXEC(N'ALTER TABLE [MoodJournalEntries] ADD CONSTRAINT [CK_MoodJournalEntries_NoteProtectionVersion] CHECK ([NoteProtectionVersion] IN (0, 1))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StudentNudgePreferences_StudentProfileId] ON [StudentNudgePreferences] ([StudentProfileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831094114_RuntimeMlV2AndManualNudges'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831094114_RuntimeMlV2AndManualNudges', N'8.0.30');
END;
GO

COMMIT;
GO

