-- Create roles for read/write split
CREATE ROLE reader_blue LOGIN PASSWORD 'reader_blue';
CREATE ROLE writer_blue LOGIN PASSWORD 'writer_blue';

-- Create sample schema
CREATE TABLE users
(
    id         serial PRIMARY KEY,
    username   text        NOT NULL UNIQUE,
    email      text        NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE orders
(
    id        serial PRIMARY KEY,
    user_id   int            NOT NULL REFERENCES users (id),
    total     numeric(10, 2) NOT NULL,
    status    text           NOT NULL DEFAULT 'pending',
    placed_at timestamptz    NOT NULL DEFAULT now()
);

CREATE TABLE products
(
    id    serial PRIMARY KEY,
    name  text           NOT NULL,
    price numeric(10, 2) NOT NULL,
    stock int            NOT NULL DEFAULT 0
);

-- Seed sample data
INSERT INTO users (username, email)
SELECT 'user_' || i,
       'user_' || i || '@example.com'
FROM generate_series(1, 50) AS i;

INSERT INTO products (name, price, stock)
VALUES ('Widget A', 9.99, 100),
       ('Widget B', 19.99, 50),
       ('Gadget X', 49.99, 25),
       ('Gadget Y', 99.99, 10),
       ('Premium Z', 199.99, 5);

INSERT INTO orders (user_id, total, status)
SELECT (random() * 49 + 1)::int,
       (random() * 200 + 5)::numeric(10, 2),
       (ARRAY ['pending','shipped','delivered','cancelled'])[ceil(random() * 4)::int]
FROM generate_series(1, 100);

-- Grant permissions
GRANT USAGE ON SCHEMA public TO reader_blue;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO reader_blue;

GRANT USAGE ON SCHEMA public TO writer_blue;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO writer_blue;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO writer_blue;
