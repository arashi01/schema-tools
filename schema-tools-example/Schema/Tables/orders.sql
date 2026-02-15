-- @category commerce
-- @description Customer orders with soft-delete cascading from parent user.
--              Reactivation cascade enabled: reactivating a user also
--              reactivates orders that were soft-deleted at the same time.

CREATE TABLE [dbo].[orders]
(
    [id]            UNIQUEIDENTIFIER    NOT NULL
        CONSTRAINT [df_orders_id] DEFAULT NEWSEQUENTIALID(),
    [user_id]       UNIQUEIDENTIFIER    NOT NULL,
    [order_date]    DATETIMEOFFSET(7)   NOT NULL
        CONSTRAINT [df_orders_order_date] DEFAULT SYSUTCDATETIME(),
    [total_amount]  DECIMAL(18, 2)      NOT NULL,
    [currency]      CHAR(3)             NOT NULL
        CONSTRAINT [df_orders_currency] DEFAULT 'GBP',
    [active]        BIT                 NOT NULL
        CONSTRAINT [df_orders_active] DEFAULT 1,
    [created_by]    UNIQUEIDENTIFIER    NOT NULL,
    [updated_by]    UNIQUEIDENTIFIER    NOT NULL,
    [valid_from]    DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to]      DATETIME2(7)        GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to]),

    CONSTRAINT [pk_orders] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [fk_orders_user] FOREIGN KEY ([user_id])
        REFERENCES [dbo].[users] ([id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[orders_history]));
GO
