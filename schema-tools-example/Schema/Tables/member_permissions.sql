-- @category core
-- @description Role permissions assigned to organisation members.
--              Uses a composite foreign key referencing the unique
--              constraint on organisation_members(organisation_id, user_id),
--              and a composite primary key.
--              Exercises composite FK/PK extraction and ON DELETE CASCADE.

CREATE TABLE [dbo].[member_permissions]
(
    [organisation_id]   UNIQUEIDENTIFIER    NOT NULL,
    [user_id]           UNIQUEIDENTIFIER    NOT NULL,
    [permission]        VARCHAR(100)        NOT NULL
        CONSTRAINT [ck_member_permissions_permission]
        CHECK (LEN([permission]) > 0),
    [granted_at]        DATETIMEOFFSET(7)   NOT NULL
        CONSTRAINT [df_member_permissions_granted_at] DEFAULT SYSUTCDATETIME(),
    [granted_by]        UNIQUEIDENTIFIER    NOT NULL,
    [record_created_by]        UNIQUEIDENTIFIER    NOT NULL,
    [record_updated_by]        UNIQUEIDENTIFIER    NOT NULL,

    CONSTRAINT [pk_member_permissions] PRIMARY KEY CLUSTERED
        ([organisation_id], [user_id], [permission]),
    CONSTRAINT [fk_member_permissions_member] FOREIGN KEY ([organisation_id], [user_id])
        REFERENCES [dbo].[organisation_members] ([organisation_id], [user_id])
        ON DELETE CASCADE
        ON UPDATE NO ACTION
);
GO
