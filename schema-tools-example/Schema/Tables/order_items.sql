-- @category commerce
-- @description Individual line items within an order.
--              Soft-delete cascades from parent order.

CREATE TABLE [dbo].[order_items]
(
    [id]            UNIQUEIDENTIFIER    NOT NULL
        CONSTRAINT [df_order_items_id] DEFAULT NEWSEQUENTIALID(),
    [order_id]      UNIQUEIDENTIFIER    NOT NULL,
    [product_name]  VARCHAR(300)        NOT NULL,
    [quantity]       INT                 NOT NULL
        CONSTRAINT [ck_order_items_quantity] CHECK ([quantity] > 0),
    [unit_price]    DECIMAL(18, 2)      NOT NULL,
    [record_active]        BIT                 NOT NULL
        CONSTRAINT [df_order_items_active] DEFAULT 1,
    [record_created_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_updated_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_valid_from]    DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until]      DATETIME2(7)        GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until]),

    CONSTRAINT [pk_order_items] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [fk_order_items_order] FOREIGN KEY ([order_id])
        REFERENCES [dbo].[orders] ([id])
        ON DELETE CASCADE
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[order_items_history]));
GO
