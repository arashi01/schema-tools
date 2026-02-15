-- @category test
-- @description Table with PK defined via ALTER TABLE (non-id PK pattern)

CREATE TABLE [test].[countries]
(
    [iso_alpha2] CHAR(2) NOT NULL,
    [name] NVARCHAR(200) NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [test].[hist_countries]));
GO

ALTER TABLE [test].[countries]
    ADD CONSTRAINT [pk_countries]
    PRIMARY KEY CLUSTERED ([iso_alpha2] ASC);
GO
