CREATE TABLE type_showcase
(
    id           serial PRIMARY KEY,
    label        text        NOT NULL,
    -- DateTimeOffset (timestamptz)
    ts_col       timestamptz,
    -- DateOnly (date)
    date_col     date,
    -- TimeOnly (time)
    time_col     time,
    -- TimeSpan (interval)
    interval_col interval,
    -- string (json returned as raw text by default)
    json_col     json,
    -- string (jsonb returned as raw text by default)
    jsonb_col    jsonb,
    -- int[] (Array arm)
    int_arr      integer[],
    -- text[] (Array arm)
    text_arr     text[],
    -- BitArray (bit varying)
    bits_col     bit varying(8),
    -- byte[] (Array arm, serialised as base64)
    bytes_col    bytea
);

INSERT INTO type_showcase (label, ts_col, date_col, time_col, interval_col,
                           json_col, jsonb_col,
                           int_arr, text_arr,
                           bits_col, bytes_col)
VALUES ('full row',
        '2026-05-12 09:30:00+00',
        '2026-05-12',
        '09:30:00',
        interval '1 day 2 hours 30 minutes',
        '{"event":"login","count":3}',
        '{"tags":["admin","active"],"score":9.5}',
        '{1,2,3,42}',
        '{"foo","bar","baz"}',
        B'10110101',
        '\xDEADBEEF'),
       ('nested json',
        now(),
        current_date,
        current_time,
        interval '30 seconds',
        '[1,2,3]',
        '{"nested":{"deep":true},"arr":[1,2,3]}',
        '{100,200,300}',
        '{"hello","world"}',
        B'00000001',
        '\x00FF'),
       ('nulls',
        NULL, NULL, NULL, NULL,
        NULL, NULL,
        NULL, NULL,
        NULL, NULL);

GRANT SELECT ON type_showcase TO reader_blue;
GRANT SELECT, INSERT, UPDATE, DELETE ON type_showcase TO writer_blue;
GRANT USAGE, SELECT ON SEQUENCE type_showcase_id_seq TO writer_blue;
