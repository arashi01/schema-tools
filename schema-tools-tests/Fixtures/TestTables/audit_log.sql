-- @category audit
-- @description Append-only audit log with foreign keys

CREATE TABLE [test].[audit_log]
(
    [id] BIGINT IDENTITY(1, 1) NOT NULL,
    [entity_type] VARCHAR(50) NOT NULL,
    [entity_id] UNIQUEIDENTIFIER NOT NULL,
    [action] VARCHAR(20) NOT NULL
        CONSTRAINT [ck_audit_log_action]
        CHECK ([action] IN ('create', 'update', 'delete')),
    [payload] VARCHAR(MAX) NULL,
    [record_created_at] DATETIMEOFFSET(7) NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [pk_audit_log] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [fk_audit_log_entity] FOREIGN KEY ([entity_id])
        REFERENCES [test].[simple_table] ([id])
);
GO
