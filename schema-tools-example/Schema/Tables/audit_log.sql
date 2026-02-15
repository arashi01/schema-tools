-- @category audit
-- @description Append-only audit log. SchemaTools detects the absence of an
--              active column and classifies this as an append-only table.
--              No soft-delete triggers or views are generated for this table.

CREATE TABLE [dbo].[audit_log]
(
    [id]            BIGINT IDENTITY(1, 1) NOT NULL,
    [entity_type]   VARCHAR(50)         NOT NULL,
    [entity_id]     UNIQUEIDENTIFIER    NOT NULL,
    [action]        VARCHAR(20)         NOT NULL
        CONSTRAINT [ck_audit_log_action]
        CHECK ([action] IN ('create', 'update', 'delete', 'restore')),
    [actor_id]      UNIQUEIDENTIFIER    NOT NULL,
    [payload]       VARCHAR(MAX)        NULL,
    [created_at]    DATETIMEOFFSET(7)   NOT NULL
        CONSTRAINT [df_audit_log_created_at] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [pk_audit_log] PRIMARY KEY CLUSTERED ([id])
);
GO
