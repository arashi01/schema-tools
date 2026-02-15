-- @category test
-- @description Temporal table with system versioning

CREATE TABLE [test].[temporal_table]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [data] VARCHAR(MAX) NULL,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [test].[temporal_table_history]));
GO
