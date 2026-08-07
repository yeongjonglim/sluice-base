-- Additional schemas to exercise the ERD schema filter: a multi-schema database
-- with foreign keys that cross schema boundaries in both directions, so the
-- diagram's "pull in referenced tables from hidden schemas" behaviour is visible.
-- Idempotent so it is safe both as a fresh-volume init script and when re-run by
-- hand against an already-initialised database.

CREATE SCHEMA IF NOT EXISTS sales;
CREATE SCHEMA IF NOT EXISTS audit;

-- sales: regions and reps, with an intra-schema foreign key (reps -> regions).
CREATE TABLE IF NOT EXISTS sales.regions
(
    id   serial PRIMARY KEY,
    name text NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS sales.reps
(
    id        serial PRIMARY KEY,
    full_name text NOT NULL,
    region_id int  NOT NULL REFERENCES sales.regions (id)
);

-- audit: an event log referencing tables in TWO other schemas
-- (audit -> public.orders and audit -> sales.reps).
CREATE TABLE IF NOT EXISTS audit.order_events
(
    id         serial PRIMARY KEY,
    order_id   int         NOT NULL REFERENCES public.orders (id),
    rep_id     int REFERENCES sales.reps (id),
    event_type text        NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

-- Cross-schema foreign key the other way: public.orders -> sales.regions.
-- Added as a nullable column so existing rows stay valid. Constraints have no
-- IF NOT EXISTS before PG 18-ish, so guard the ADD CONSTRAINT explicitly.
ALTER TABLE public.orders ADD COLUMN IF NOT EXISTS region_id int;

DO
$$
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'orders_region_id_fkey') THEN
            ALTER TABLE public.orders
                ADD CONSTRAINT orders_region_id_fkey FOREIGN KEY (region_id) REFERENCES sales.regions (id);
        END IF;
    END
$$;

-- Sample data.
INSERT INTO sales.regions (name)
VALUES ('North'), ('South'), ('East'), ('West')
ON CONFLICT (name) DO NOTHING;

INSERT INTO sales.reps (full_name, region_id)
SELECT 'Rep ' || i, ((i - 1) % 4) + 1
FROM generate_series(1, 8) AS i;

UPDATE public.orders
SET region_id = ((id - 1) % 4) + 1
WHERE region_id IS NULL;

INSERT INTO audit.order_events (order_id, rep_id, event_type)
SELECT o.id, ((o.id - 1) % 8) + 1, 'created'
FROM public.orders o;

-- Let the read/write roles see the new schemas. The ERD introspects as the read
-- role, so without USAGE + SELECT here the new schemas would not appear at all.
GRANT USAGE ON SCHEMA sales, audit TO reader_blue, writer_blue;

GRANT SELECT ON ALL TABLES IN SCHEMA sales, audit TO reader_blue;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA sales, audit TO writer_blue;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA sales, audit TO writer_blue;

-- Two more schemas so a database with more than three schemas exists, which exercises the
-- ERD picker's "+N" overflow pill (schemas beyond the third collapse into a counter).
CREATE SCHEMA IF NOT EXISTS reporting;
CREATE SCHEMA IF NOT EXISTS staging;

CREATE TABLE IF NOT EXISTS reporting.daily_totals
(
    id        serial PRIMARY KEY,
    region_id int REFERENCES sales.regions (id),
    total     numeric(12, 2) NOT NULL
);

CREATE TABLE IF NOT EXISTS staging.import_batch
(
    id         serial PRIMARY KEY,
    source     text        NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

GRANT USAGE ON SCHEMA reporting, staging TO reader_blue, writer_blue;

GRANT SELECT ON ALL TABLES IN SCHEMA reporting, staging TO reader_blue;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA reporting, staging TO writer_blue;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA reporting, staging TO writer_blue;
