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
    [record_active]        BIT                 NOT NULL
        CONSTRAINT [df_orders_active] DEFAULT 1,
    [record_created_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_updated_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_valid_from]    DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until]      DATETIME2(7)        GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until]),

    CONSTRAINT [pk_orders] PRIMARY KEY CLUSTERED ([id]),
    CONSTRAINT [fk_orders_user] FOREIGN KEY ([user_id])
        REFERENCES [dbo].[users] ([id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[orders_history]));
GO

-- Nonclustered index with INCLUDE columns
CREATE NONCLUSTERED INDEX [ix_orders_user_id]
    ON [dbo].[orders] ([user_id])
    INCLUDE ([order_date], [total_amount]);
GO

-- Unique filtered index on active records
CREATE UNIQUE NONCLUSTERED INDEX [ix_orders_user_date_active]
    ON [dbo].[orders] ([user_id], [order_date])
    WHERE [record_active] = 1;
GO
