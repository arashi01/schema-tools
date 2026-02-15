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
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [test].[polymorphic_table_history]));
GO
