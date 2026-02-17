-- @category commerce
-- @description Hierarchical category tree with self-referencing foreign key.
--              Categories can be nested to arbitrary depth via parent_id.

CREATE TABLE [dbo].[categories]
(
    [id]            UNIQUEIDENTIFIER    NOT NULL
        CONSTRAINT [df_categories_id] DEFAULT NEWSEQUENTIALID(),
    [name]          VARCHAR(200)        NOT NULL,
    [slug]          VARCHAR(200)        NOT NULL,  -- @description URL-friendly identifier
    [parent_id]     UNIQUEIDENTIFIER    NULL,      -- @description Parent category for hierarchy; NULL for root categories
    [sort_order]    INT                 NOT NULL
        CONSTRAINT [df_categories_sort_order] DEFAULT 0,
    [record_active]        BIT                 NOT NULL
        CONSTRAINT [df_categories_active] DEFAULT 1,
    [record_created_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_updated_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_valid_from]    DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until]   DATETIME2(7)        GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until]),

    CONSTRAINT [pk_categories] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [uq_categories_slug] UNIQUE ([slug]),
    CONSTRAINT [fk_categories_parent] FOREIGN KEY ([parent_id])
        REFERENCES [dbo].[categories] ([id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[categories_history]));
GO
