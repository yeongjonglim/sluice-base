-- Extension (citext ships with the postgres contrib image)
CREATE EXTENSION IF NOT EXISTS citext;

-- Enum + composite custom types
CREATE TYPE order_status AS ENUM ('pending', 'shipped', 'delivered', 'cancelled');
CREATE TYPE address AS (street text, city text, postcode text);

-- Standalone sequence (not owned by a column)
CREATE SEQUENCE ticket_seq START 1000 INCREMENT 5;

-- View over an existing table
CREATE VIEW active_orders AS
SELECT id, user_id, total, status
FROM orders
WHERE status <> 'cancelled';

-- Materialized view with its own index
CREATE MATERIALIZED VIEW order_totals AS
SELECT user_id, sum(total) AS total_spent
FROM orders
GROUP BY user_id;

CREATE INDEX idx_order_totals_user ON order_totals (user_id);

-- Secondary index on a table
CREATE INDEX idx_orders_status ON orders (status);

-- Function and procedure
CREATE FUNCTION order_count(uid integer) RETURNS bigint
LANGUAGE sql AS $$ SELECT count(*) FROM orders WHERE user_id = uid $$;

CREATE PROCEDURE touch_user(uid integer)
LANGUAGE sql AS $$ UPDATE users SET created_at = now() WHERE id = uid $$;

-- Let the read role see the new relations (catalog visibility is unaffected either way)
GRANT SELECT ON active_orders TO reader_blue;
GRANT SELECT ON order_totals TO reader_blue;
