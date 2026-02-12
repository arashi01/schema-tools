-- @category test
-- @description Simple test table with basic columns

CREATE TABLE [test].[simple_table]
(
    [id] UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT [pk_simple_table] PRIMARY KEY,
    
    [name] VARCHAR(200) NOT NULL,
    [value] INT NULL,
    [created_at] DATETIMEOFFSET(7) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
