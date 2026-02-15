-- @category core
-- @description Organisation membership linking users to organisations.
--              Child of both users and organisations via FKs.

CREATE TABLE [dbo].[organisation_members]
(
    [id]                UNIQUEIDENTIFIER    NOT NULL
        CONSTRAINT [df_organisation_members_id] DEFAULT NEWSEQUENTIALID(),
    [organisation_id]   UNIQUEIDENTIFIER    NOT NULL,
    [user_id]           UNIQUEIDENTIFIER    NOT NULL,
    [role]              VARCHAR(50)         NOT NULL
        CONSTRAINT [df_organisation_members_role] DEFAULT 'member',
    [record_active]            BIT                 NOT NULL
        CONSTRAINT [df_organisation_members_active] DEFAULT 1,
    [record_created_by]        UNIQUEIDENTIFIER    NOT NULL,
    [record_updated_by]        UNIQUEIDENTIFIER    NOT NULL,
    [record_valid_from]        DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until]          DATETIME2(7)        GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until]),

    CONSTRAINT [pk_organisation_members] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [uq_organisation_members] UNIQUE ([organisation_id], [user_id]),
    CONSTRAINT [fk_organisation_members_org] FOREIGN KEY ([organisation_id])
        REFERENCES [dbo].[organisations] ([id]),
    CONSTRAINT [fk_organisation_members_user] FOREIGN KEY ([user_id])
        REFERENCES [dbo].[users] ([id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[organisation_members_history]));
GO
