-- @category core
-- @description Country dialling codes keyed by country and code.
--              A child of countries via FK on country_code, with soft-delete
--              cascade from the parent table.

CREATE TABLE [dbo].[country_dialling_codes]
(
    [country_code]      CHAR(2)             NOT NULL,
    [dialling_code]     VARCHAR(10)         NOT NULL,
    [is_primary]        BIT                 NOT NULL
        CONSTRAINT [df_country_dialling_codes_primary] DEFAULT 1,
    [record_active]     BIT                 NOT NULL
        CONSTRAINT [df_country_dialling_codes_active] DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER    NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER    NOT NULL,
    [record_valid_from] DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until]   DATETIME2(7)     GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[country_dialling_codes_history]));
GO

ALTER TABLE [dbo].[country_dialling_codes]
    ADD CONSTRAINT [pk_country_dialling_codes]
        PRIMARY KEY CLUSTERED ([country_code] ASC, [dialling_code] ASC);
GO

ALTER TABLE [dbo].[country_dialling_codes]
    ADD CONSTRAINT [fk_country_dialling_codes_country]
        FOREIGN KEY ([country_code]) REFERENCES [dbo].[countries] ([iso_alpha2]);
GO
