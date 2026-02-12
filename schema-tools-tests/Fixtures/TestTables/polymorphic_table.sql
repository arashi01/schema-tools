-- @category test
-- @description Polymorphic table with owner_type/owner_id pattern

CREATE TABLE [test].[polymorphic_table]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [owner_type] VARCHAR(20) NOT NULL
        CONSTRAINT [ck_polymorphic_table_owner_type] 
        CHECK ([owner_type] IN ('individual', 'organisation')),
    [owner_id] UNIQUEIDENTIFIER NOT NULL,
    [data] VARCHAR(MAX) NULL,
    [active] BIT NOT NULL DEFAULT 1,
    [created_by] UNIQUEIDENTIFIER NOT NULL,
    [updated_by] UNIQUEIDENTIFIER NOT NULL,
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [test].[polymorphic_table_history]));
GO
