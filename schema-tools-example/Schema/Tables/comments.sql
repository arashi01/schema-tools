-- @category social
-- @description Polymorphic comments table. The owner_type/owner_id pattern
--              allows comments to belong to different entity types without
--              a direct FK. SchemaTools detects and documents this pattern.

CREATE TABLE [dbo].[comments]
(
    [id]            UNIQUEIDENTIFIER    NOT NULL
        CONSTRAINT [df_comments_id] DEFAULT NEWSEQUENTIALID(),
    [owner_type]    VARCHAR(50)         NOT NULL
        CONSTRAINT [ck_comments_owner_type]
        CHECK ([owner_type] IN ('user', 'organisation', 'order')),
    [owner_id]      UNIQUEIDENTIFIER    NOT NULL,
    [body]          VARCHAR(4000)       NOT NULL,
    [record_active]        BIT                 NOT NULL
        CONSTRAINT [df_comments_active] DEFAULT 1,
    [record_created_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_updated_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_valid_from]    DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until]      DATETIME2(7)        GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until]),

    CONSTRAINT [pk_comments] PRIMARY KEY CLUSTERED ([id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[comments_history]));
GO
