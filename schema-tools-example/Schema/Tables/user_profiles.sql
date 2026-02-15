-- @category core
-- @description Extended user profile data. Child of users via FK.
--              Soft-delete cascades from parent user.

CREATE TABLE [dbo].[user_profiles]
(
    [id]            UNIQUEIDENTIFIER    NOT NULL
        CONSTRAINT [df_user_profiles_id] DEFAULT NEWSEQUENTIALID(),
    [user_id]       UNIQUEIDENTIFIER    NOT NULL,
    [bio]           VARCHAR(2000)       NULL,
    [avatar_url]    VARCHAR(500)        NULL,
    [active]        BIT                 NOT NULL
        CONSTRAINT [df_user_profiles_active] DEFAULT 1,
    [created_by]    UNIQUEIDENTIFIER    NOT NULL,
    [updated_by]    UNIQUEIDENTIFIER    NOT NULL,
    [valid_from]    DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to]      DATETIME2(7)        GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to]),

    CONSTRAINT [pk_user_profiles] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [uq_user_profiles_user_id] UNIQUE ([user_id]),
    CONSTRAINT [fk_user_profiles_user] FOREIGN KEY ([user_id])
        REFERENCES [dbo].[users] ([id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[user_profiles_history]));
GO
