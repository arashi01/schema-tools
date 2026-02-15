-- @category audit
-- @description High-volume event stream with clustered columnstore index.
--              Append-only table optimised for analytical queries.
--              Exercises clustered columnstore and second identity column.

CREATE TABLE [dbo].[event_stream]
(
    [event_id]      BIGINT IDENTITY(1, 1)   NOT NULL,
    [event_type]    VARCHAR(100)            NOT NULL,
    [source]        VARCHAR(200)            NOT NULL,
    [payload]       VARCHAR(MAX)            NULL,
    [record_created_at]    DATETIMEOFFSET(7)       NOT NULL
        CONSTRAINT [df_event_stream_created_at] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [pk_event_stream] PRIMARY KEY NONCLUSTERED ([event_id])
);
GO

CREATE CLUSTERED COLUMNSTORE INDEX [cci_event_stream]
    ON [dbo].[event_stream];
GO
