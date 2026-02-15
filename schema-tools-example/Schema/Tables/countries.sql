-- @category core
-- @description Reference table of countries keyed by ISO 3166-1 alpha-2 code.
--              Supports soft-delete with temporal versioning.

CREATE TABLE [dbo].[countries]
(
    [iso_alpha2]        CHAR(2)             NOT NULL,
    [name]              VARCHAR(200)        NOT NULL,
    [dialling_code]     VARCHAR(10)         NULL,
    [record_active]     BIT                 NOT NULL
        CONSTRAINT [df_countries_active] DEFAULT 1,
    [record_created_by] UNIQUEIDENTIFIER    NOT NULL,
    [record_updated_by] UNIQUEIDENTIFIER    NOT NULL,
    [record_valid_from] DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until]   DATETIME2(7)     GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[countries_history]));
GO

ALTER TABLE [dbo].[countries]
    ADD CONSTRAINT [pk_countries] PRIMARY KEY CLUSTERED ([iso_alpha2] ASC);
GO
