/*
 * @category commerce
 * @description ISO 4217 currency reference data with natural key.
 *              Provides standardised currency symbols and decimal precision.
 */

CREATE TABLE [dbo].[currencies]
(
    [code]          CHAR(3)             NOT NULL,  -- @description ISO 4217 three-letter currency code
    [name]          VARCHAR(100)        NOT NULL,  -- @description Official currency name
    [symbol]        VARCHAR(5)          NOT NULL,
    [decimal_places] INT               NOT NULL
        CONSTRAINT [df_currencies_decimal_places] DEFAULT 2,
    [record_active]        BIT                 NOT NULL
        CONSTRAINT [df_currencies_active] DEFAULT 1,
    [record_created_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_updated_by]    UNIQUEIDENTIFIER    NOT NULL,
    [record_valid_from]    DATETIME2(7)        GENERATED ALWAYS AS ROW START NOT NULL,
    [record_valid_until]   DATETIME2(7)        GENERATED ALWAYS AS ROW END NOT NULL,

    PERIOD FOR SYSTEM_TIME ([record_valid_from], [record_valid_until]),

    CONSTRAINT [pk_currencies] PRIMARY KEY CLUSTERED ([code])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[currencies_history]));
GO
