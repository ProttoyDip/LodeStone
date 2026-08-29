BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    DELETE FROM [RiskQueueEntries];
    DELETE FROM [RiskScores];
    DELETE FROM [ActivityLogs];
    UPDATE [StudentProfiles]
    SET [StudentNumber] = UPPER(LTRIM(RTRIM([StudentNumber])))
    WHERE [StudentNumber] IS NOT NULL;
    UPDATE [StudentProfiles]
    SET [StudentNumber] = NULL
    WHERE [StudentNumber] IS NOT NULL
      AND (
          LEN([StudentNumber]) NOT BETWEEN 1 AND 64
          OR LEFT([StudentNumber], 1) COLLATE Latin1_General_100_BIN2 NOT LIKE '[A-Z0-9]'
          OR [StudentNumber] COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Z0-9._/-]%'
      );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    DROP INDEX [IX_RiskScores_StudentProfileId] ON [RiskScores];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    DROP INDEX [IX_RiskQueueEntries_StudentProfileId] ON [RiskQueueEntries];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StudentProfiles]') AND [c].[name] = N'StudentNumber');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [StudentProfiles] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [StudentProfiles] ALTER COLUMN [StudentNumber] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StudentProfiles]') AND [c].[name] = N'Program');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [StudentProfiles] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [StudentProfiles] ALTER COLUMN [Program] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RiskScores]') AND [c].[name] = N'ModelVersion');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [RiskScores] DROP CONSTRAINT [' + @var2 + '];');
    EXEC(N'UPDATE [RiskScores] SET [ModelVersion] = N'''' WHERE [ModelVersion] IS NULL');
    ALTER TABLE [RiskScores] ALTER COLUMN [ModelVersion] nvarchar(128) NOT NULL;
    ALTER TABLE [RiskScores] ADD DEFAULT N'' FOR [ModelVersion];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    ALTER TABLE [RiskScores] ADD [CourseKey] nvarchar(120) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    ALTER TABLE [RiskScores] ADD [FeatureSchemaVersion] nvarchar(64) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    ALTER TABLE [RiskScores] ADD [RiskFeatureSnapshotId] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    ALTER TABLE [RiskScores] ADD [RiskScoringRunId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    ALTER TABLE [RiskScores] ADD [WindowEndUtc] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RiskQueueEntries]') AND [c].[name] = N'ResolvedByUserId');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [RiskQueueEntries] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [RiskQueueEntries] ALTER COLUMN [ResolvedByUserId] nvarchar(450) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    ALTER TABLE [RiskQueueEntries] ADD [LastSignaledAtUtc] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    ALTER TABLE [RiskQueueEntries] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    ALTER TABLE [RiskQueueEntries] ADD [TriggerRiskScoreId] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE TABLE [RiskFeatureSnapshots] (
        [Id] int NOT NULL IDENTITY,
        [StudentProfileId] int NOT NULL,
        [CourseKey] nvarchar(120) NOT NULL,
        [WindowEndUtc] datetime2 NOT NULL,
        [ObservedDays] int NOT NULL,
        [FeatureSchemaVersion] nvarchar(64) NOT NULL,
        [SourceFileName] nvarchar(260) NOT NULL,
        [SourceFileSha256] varchar(64) NOT NULL,
        [ActiveDayRate] real NOT NULL,
        [ActivitySpanDays] real NOT NULL,
        [DaysSinceLastAccess] real NOT NULL,
        [ForumInteractionCount] real NOT NULL,
        [CourseInteractionCount] real NOT NULL,
        [LateOrMissingAssignmentCount] real NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_RiskFeatureSnapshots] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RiskFeatureSnapshots_Features] CHECK ([ActiveDayRate] >= 0 AND [ActiveDayRate] <= 1 AND [ActivitySpanDays] >= 0 AND [ActivitySpanDays] <= [ObservedDays] AND [DaysSinceLastAccess] >= 0 AND [DaysSinceLastAccess] <= [ObservedDays] AND [ForumInteractionCount] >= 0 AND [CourseInteractionCount] >= 0 AND [LateOrMissingAssignmentCount] >= 0),
        CONSTRAINT [CK_RiskFeatureSnapshots_ObservedDays] CHECK ([ObservedDays] > 0 AND [ObservedDays] <= 365),
        CONSTRAINT [FK_RiskFeatureSnapshots_StudentProfiles_StudentProfileId] FOREIGN KEY ([StudentProfileId]) REFERENCES [StudentProfiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE TABLE [RiskMonitoringConsentHistory] (
        [Id] int NOT NULL IDENTITY,
        [StudentProfileId] int NOT NULL,
        [IsConsented] bit NOT NULL,
        [PolicyVersion] nvarchar(32) NOT NULL,
        [ChangedAtUtc] datetime2 NOT NULL,
        [ChangedByUserId] nvarchar(450) NULL,
        CONSTRAINT [PK_RiskMonitoringConsentHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RiskMonitoringConsentHistory_StudentProfiles_StudentProfileId] FOREIGN KEY ([StudentProfileId]) REFERENCES [StudentProfiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE TABLE [RiskMonitoringConsents] (
        [Id] int NOT NULL IDENTITY,
        [StudentProfileId] int NOT NULL,
        [IsConsented] bit NOT NULL,
        [PolicyVersion] nvarchar(32) NOT NULL,
        [ConsentedAtUtc] datetime2 NULL,
        [WithdrawnAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_RiskMonitoringConsents] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RiskMonitoringConsents_State] CHECK ([IsConsented] = 0 OR ([ConsentedAtUtc] IS NOT NULL AND [WithdrawnAtUtc] IS NULL)),
        CONSTRAINT [FK_RiskMonitoringConsents_StudentProfiles_StudentProfileId] FOREIGN KEY ([StudentProfileId]) REFERENCES [StudentProfiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE TABLE [RiskScoringRuns] (
        [Id] int NOT NULL IDENTITY,
        [RunKey] uniqueidentifier NOT NULL,
        [ModelVersion] nvarchar(128) NOT NULL,
        [FeatureSchemaVersion] nvarchar(64) NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        [Status] int NOT NULL,
        [CandidateCount] int NOT NULL,
        [ScoredCount] int NOT NULL,
        [SkippedCount] int NOT NULL,
        [FailedCount] int NOT NULL,
        [QueueCreatedCount] int NOT NULL,
        [QueueEscalatedCount] int NOT NULL,
        [FailureSummary] nvarchar(2000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_RiskScoringRuns] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RiskScoringRuns_Counts] CHECK ([CandidateCount] >= 0 AND [ScoredCount] >= 0 AND [SkippedCount] >= 0 AND [FailedCount] >= 0 AND [QueueCreatedCount] >= 0 AND [QueueEscalatedCount] >= 0),
        CONSTRAINT [CK_RiskScoringRuns_Status] CHECK ([Status] >= 0 AND [Status] <= 4)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE TABLE [StudentNumberClaims] (
        [Id] int NOT NULL IDENTITY,
        [StudentProfileId] int NOT NULL,
        [ClaimedStudentNumber] nvarchar(64) NOT NULL,
        [Status] int NOT NULL,
        [SubmittedAtUtc] datetime2 NOT NULL,
        [ReviewedAtUtc] datetime2 NULL,
        [ReviewedByUserId] nvarchar(450) NULL,
        [RowVersion] rowversion NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_StudentNumberClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_StudentNumberClaims_Review] CHECK (([Status] = 0 AND [ReviewedAtUtc] IS NULL AND [ReviewedByUserId] IS NULL) OR ([Status] IN (1, 2) AND [ReviewedAtUtc] IS NOT NULL AND [ReviewedByUserId] IS NOT NULL)),
        CONSTRAINT [CK_StudentNumberClaims_Status] CHECK ([Status] >= 0 AND [Status] <= 2),
        CONSTRAINT [FK_StudentNumberClaims_StudentProfiles_StudentProfileId] FOREIGN KEY ([StudentProfileId]) REFERENCES [StudentProfiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    INSERT INTO [StudentNumberClaims]
        ([StudentProfileId], [ClaimedStudentNumber], [Status], [SubmittedAtUtc],
         [ReviewedAtUtc], [ReviewedByUserId], [CreatedAtUtc], [CreatedBy],
         [ModifiedAtUtc], [ModifiedBy])
    SELECT [Id], [StudentNumber], 0, SYSUTCDATETIME(),
           NULL, NULL, SYSUTCDATETIME(), [UserId], NULL, NULL
    FROM [StudentProfiles]
    WHERE [StudentNumber] IS NOT NULL;
    UPDATE [StudentProfiles]
    SET [StudentNumber] = NULL
    WHERE [StudentNumber] IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_StudentProfiles_StudentNumber] ON [StudentProfiles] ([StudentNumber]) WHERE [StudentNumber] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE INDEX [IX_RiskScores_RiskScoringRunId] ON [RiskScores] ([RiskScoringRunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE INDEX [IX_RiskScores_StudentProfileId_ScoredAtUtc] ON [RiskScores] ([StudentProfileId], [ScoredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RiskScores_Snapshot_Model] ON [RiskScores] ([RiskFeatureSnapshotId], [ModelVersion]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    EXEC(N'ALTER TABLE [RiskScores] ADD CONSTRAINT [CK_RiskScores_Level] CHECK ([Level] >= 0 AND [Level] <= 3)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    EXEC(N'ALTER TABLE [RiskScores] ADD CONSTRAINT [CK_RiskScores_Probability] CHECK ([Probability] >= 0 AND [Probability] <= 1)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE INDEX [IX_RiskQueueEntries_IsResolved_Level_LastSignaledAtUtc] ON [RiskQueueEntries] ([IsResolved], [Level], [LastSignaledAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE INDEX [IX_RiskQueueEntries_TriggerRiskScoreId] ON [RiskQueueEntries] ([TriggerRiskScoreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RiskQueueEntries_OneOpenPerStudent] ON [RiskQueueEntries] ([StudentProfileId]) WHERE [IsResolved] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    EXEC(N'ALTER TABLE [RiskQueueEntries] ADD CONSTRAINT [CK_RiskQueueEntries_Level] CHECK ([Level] >= 0 AND [Level] <= 3)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    EXEC(N'ALTER TABLE [RiskQueueEntries] ADD CONSTRAINT [CK_RiskQueueEntries_Resolution] CHECK (([IsResolved] = 0 AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL) OR ([IsResolved] = 1 AND [ResolvedAtUtc] IS NOT NULL AND [ResolvedByUserId] IS NOT NULL))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    EXEC(N'ALTER TABLE [ActivityLogs] ADD CONSTRAINT [CK_ActivityLogs_NonNegativeCounts] CHECK ([LoginCount] >= 0 AND [ForumInteractions] >= 0 AND [CourseInteractions] >= 0 AND [DaysSinceLastAccess] >= 0 AND [AssignmentsLateCount] >= 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE INDEX [IX_RiskFeatureSnapshots_FeatureSchemaVersion_WindowEndUtc] ON [RiskFeatureSnapshots] ([FeatureSchemaVersion], [WindowEndUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RiskFeatureSnapshots_Student_Course_Window_Schema] ON [RiskFeatureSnapshots] ([StudentProfileId], [CourseKey], [WindowEndUtc], [FeatureSchemaVersion]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE INDEX [IX_RiskMonitoringConsentHistory_StudentProfileId_ChangedAtUtc] ON [RiskMonitoringConsentHistory] ([StudentProfileId], [ChangedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RiskMonitoringConsents_StudentProfileId] ON [RiskMonitoringConsents] ([StudentProfileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RiskScoringRuns_RunKey] ON [RiskScoringRuns] ([RunKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE INDEX [IX_RiskScoringRuns_StartedAtUtc] ON [RiskScoringRuns] ([StartedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE INDEX [IX_StudentNumberClaims_ClaimedStudentNumber_Status] ON [StudentNumberClaims] ([ClaimedStudentNumber], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    CREATE INDEX [IX_StudentNumberClaims_Status_SubmittedAtUtc] ON [StudentNumberClaims] ([Status], [SubmittedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_StudentNumberClaims_OnePendingPerStudent] ON [StudentNumberClaims] ([StudentProfileId]) WHERE [Status] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    ALTER TABLE [RiskQueueEntries] ADD CONSTRAINT [FK_RiskQueueEntries_RiskScores_TriggerRiskScoreId] FOREIGN KEY ([TriggerRiskScoreId]) REFERENCES [RiskScores] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    ALTER TABLE [RiskScores] ADD CONSTRAINT [FK_RiskScores_RiskFeatureSnapshots_RiskFeatureSnapshotId] FOREIGN KEY ([RiskFeatureSnapshotId]) REFERENCES [RiskFeatureSnapshots] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    ALTER TABLE [RiskScores] ADD CONSTRAINT [FK_RiskScores_RiskScoringRuns_RiskScoringRunId] FOREIGN KEY ([RiskScoringRunId]) REFERENCES [RiskScoringRuns] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260829032603_ConsentGatedRiskMonitoring'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260829032603_ConsentGatedRiskMonitoring', N'8.0.0');
END;
GO

COMMIT;
GO

