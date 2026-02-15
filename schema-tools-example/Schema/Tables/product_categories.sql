-- @category commerce
-- @description Product-to-category mapping with composite primary key
--              (nonclustered). Exercises composite PK, nonclustered PK,
--              computed columns (persisted and non-persisted), and
--              index extraction in SchemaTools.

CREATE TABLE [dbo].[product_categories]
(
    [product_id]    UNIQUEIDENTIFIER    NOT NULL,
    [category_id]   UNIQUEIDENTIFIER    NOT NULL,
    [sort_order]    INT                 NOT NULL
        CONSTRAINT [df_product_categories_sort_order] DEFAULT 0,
    [weight]        DECIMAL(8, 2)       NOT NULL
        CONSTRAINT [df_product_categories_weight] DEFAULT 1.0,
    [weighted_sort] AS ([sort_order] * [weight]),
    [sort_key]      AS (CONCAT(RIGHT('0000' + CAST([sort_order] AS VARCHAR(4)), 4), '-', CAST([category_id] AS VARCHAR(36)))) PERSISTED,
    [record_created_at]    DATETIMEOFFSET(7)   NOT NULL
        CONSTRAINT [df_product_categories_created_at] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [pk_product_categories] PRIMARY KEY NONCLUSTERED ([product_id], [category_id])
);
GO

-- Covering index for category lookups
CREATE NONCLUSTERED INDEX [ix_product_categories_category]
    ON [dbo].[product_categories] ([category_id])
    INCLUDE ([product_id], [sort_order]);
GO
