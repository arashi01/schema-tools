-- @description Soft-delete table using custom column names
-- @category custom
CREATE TABLE [test].[custom_columns_table] (
    [id] UNIQUEIDENTIFIER NOT NULL,
    [name] VARCHAR(200) NOT NULL,
    [is_enabled] BIT NOT NULL CONSTRAINT [df_custom_columns_table_is_enabled] DEFAULT 1,
    [author] UNIQUEIDENTIFIER NOT NULL,
    [editor] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until]),
    CONSTRAINT [pk_custom_columns_table] PRIMARY KEY CLUSTERED ([id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [test].[custom_columns_table_history]));
