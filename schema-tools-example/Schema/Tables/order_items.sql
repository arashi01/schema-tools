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
    [active]        BIT                 NOT NULL
        CONSTRAINT [df_order_items_active] DEFAULT 1,
    [created_by]    UNIQUEIDENTIFIER    NOT NULL,
    [updated_by]    UNIQUEIDENTIFIER    NOT NULL,
    [valid_from]    DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to]      DATETIME2(7)        GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to]),

    CONSTRAINT [pk_order_items] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [fk_order_items_order] FOREIGN KEY ([order_id])
        REFERENCES [dbo].[orders] ([id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[order_items_history]));
GO
