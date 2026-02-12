-- @category test
-- @description Temporal table with system versioning

CREATE TABLE [test].[temporal_table]
(
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [data] VARCHAR(MAX) NULL,
    [created_by] UNIQUEIDENTIFIER NOT NULL,
    [updated_by] UNIQUEIDENTIFIER NOT NULL,
    [valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [valid_to] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([valid_from], [valid_to])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [test].[temporal_table_history]));
GO
