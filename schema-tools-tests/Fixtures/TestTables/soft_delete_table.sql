-- @category test
-- @description Soft delete table (temporal + active column)

CREATE TABLE [test].[soft_delete_table]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [name] VARCHAR(200) NOT NULL,
    [active] BIT NOT NULL DEFAULT 1,
    [created_by] UNIQUEIDENTIFIER NOT NULL,
    [updated_by] UNIQUEIDENTIFIER NOT NULL,
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [test].[soft_delete_table_history]));
GO
