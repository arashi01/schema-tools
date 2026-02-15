-- @category test
-- @description Soft delete table (temporal + active column)

CREATE TABLE [test].[soft_delete_table]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [name] VARCHAR(200) NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [test].[soft_delete_table_history]));
GO
