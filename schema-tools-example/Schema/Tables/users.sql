-- @category core
-- @description Core user accounts with soft-delete, temporal versioning,
--              and reactivation cascade to dependent tables.

CREATE TABLE [dbo].[users]
(
    [id]            UNIQUEIDENTIFIER    NOT NULL
        CONSTRAINT [df_users_id] DEFAULT NEWSEQUENTIALID(),
    [email]         VARCHAR(320)        NOT NULL,
    [display_name]  VARCHAR(200)        NOT NULL,
    [active]        BIT                 NOT NULL
        CONSTRAINT [df_users_active] DEFAULT 1,
    [created_by]    UNIQUEIDENTIFIER    NOT NULL,
    [updated_by]    UNIQUEIDENTIFIER    NOT NULL,
    [valid_from]    DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to]      DATETIME2(7)        GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to]),

    CONSTRAINT [pk_users] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [uq_users_email] UNIQUE ([email])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[users_history]));
GO
