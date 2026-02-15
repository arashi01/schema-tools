-- @category test
-- @description Table with composite PK defined via ALTER TABLE and FK to alter_table_pk

CREATE TABLE [test].[country_dialling_codes]
(
    [country_code] CHAR(2) NOT NULL,
    [dialling_code] VARCHAR(10) NOT NULL,
    [record_active] BIT NOT NULL DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER NOT NULL,
    [record_valid_from] DATETIME2(7) GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until] DATETIME2(7) GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [test].[hist_country_dialling_codes]));
GO

ALTER TABLE [test].[country_dialling_codes]
    ADD CONSTRAINT [pk_country_dialling_codes]
    PRIMARY KEY CLUSTERED ([country_code] ASC, [dialling_code] ASC);
GO

ALTER TABLE [test].[country_dialling_codes]
    ADD CONSTRAINT [fk_country_dialling_codes_countries]
    FOREIGN KEY ([country_code])
    REFERENCES [test].[countries] ([iso_alpha2]);
GO
