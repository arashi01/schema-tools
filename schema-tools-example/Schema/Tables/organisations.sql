-- @category core
-- @description Organisation accounts with soft-delete in restrict mode.
--              Soft-delete is blocked if active members exist.

CREATE TABLE [dbo].[organisations]
(
    [id]            UNIQUEIDENTIFIER    NOT NULL
        CONSTRAINT [df_organisations_id] DEFAULT NEWSEQUENTIALID(),
    [name]          VARCHAR(300)        NOT NULL,
    [record_active]        BIT                 NOT NULL
        CONSTRAINT [df_organisations_active] DEFAULT 1,
    [record_created_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_updated_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_valid_from]    DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until]      DATETIME2(7)        GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until]),

    CONSTRAINT [pk_organisations] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [uq_organisations_name] UNIQUE ([name])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[organisations_history]));
GO
