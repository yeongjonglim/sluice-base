-- Wide transactions table: 22 columns, 500 rows — good for testing data grid width/scrolling
CREATE TABLE transactions (
    id             serial PRIMARY KEY,
    reference_id   text           NOT NULL UNIQUE,
    user_id        int            NOT NULL REFERENCES users (id),
    product_id     int            REFERENCES products (id),
    amount         numeric(12, 2) NOT NULL,
    currency       char(3)        NOT NULL DEFAULT 'USD',
    fee            numeric(10, 2) NOT NULL DEFAULT 0.00,
    net_amount     numeric(12, 2) NOT NULL,
    status         text           NOT NULL,
    payment_method text           NOT NULL,
    gateway        text           NOT NULL,
    card_last_four char(4),
    card_brand     text,
    ip_address     text,
    country_code   char(2),
    city           text,
    is_refunded    boolean        NOT NULL DEFAULT false,
    refund_amount  numeric(10, 2),
    notes          text,
    created_at     timestamptz    NOT NULL DEFAULT now(),
    updated_at     timestamptz    NOT NULL DEFAULT now(),
    settled_at     timestamptz
);

WITH params AS (
    SELECT
        i,
        round((random() * 495 + 5)::numeric, 2)                                                           AS amount,
        round((random() * 9.99)::numeric, 2)                                                               AS fee,
        (random() * 49 + 1)::int                                                                           AS user_id,
        CASE WHEN random() > 0.4 THEN (random() * 4 + 1)::int ELSE NULL END                               AS product_id,
        (ARRAY ['USD', 'EUR', 'GBP', 'CAD', 'AUD'])[ceil(random() * 5)::int]                              AS currency,
        (ARRAY ['pending', 'completed', 'failed', 'refunded', 'chargeback'])[ceil(random() * 5)::int]     AS status,
        (ARRAY ['credit_card', 'debit_card', 'bank_transfer', 'paypal', 'crypto'])[ceil(random() * 5)::int] AS payment_method,
        (ARRAY ['stripe', 'adyen', 'braintree', 'square', 'paypal'])[ceil(random() * 5)::int]             AS gateway,
        (ARRAY ['Visa', 'Mastercard', 'Amex', 'Discover', NULL::text])[ceil(random() * 5)::int]           AS card_brand,
        (ARRAY ['US', 'GB', 'DE', 'CA', 'AU', 'FR', 'JP', 'SG', 'BR', 'NL'])[ceil(random() * 10)::int]  AS country_code,
        (ARRAY ['New York', 'London', 'Berlin', 'Toronto', 'Sydney',
                'Paris', 'Tokyo', 'Singapore', 'São Paulo', 'Amsterdam'])[ceil(random() * 10)::int]        AS city,
        now() - (random() * interval '365 days')                                                           AS created_at
    FROM generate_series(1, 500) AS i
)
INSERT INTO transactions (reference_id,
                          user_id,
                          product_id,
                          amount,
                          currency,
                          fee,
                          net_amount,
                          status,
                          payment_method,
                          gateway,
                          card_last_four,
                          card_brand,
                          ip_address,
                          country_code,
                          city,
                          is_refunded,
                          refund_amount,
                          notes,
                          created_at,
                          updated_at,
                          settled_at)
SELECT 'TXN-' || upper(lpad(i::text, 8, '0')),
       user_id,
       product_id,
       amount,
       currency,
       fee,
       amount - fee,
       status,
       payment_method,
       gateway,
       lpad((random() * 9999)::int::text, 4, '0'),
       CASE WHEN payment_method IN ('credit_card', 'debit_card') THEN card_brand ELSE NULL END,
       (10 + (random() * 245)::int)::text || '.' ||
       (random() * 255)::int::text || '.' ||
       (random() * 255)::int::text || '.' ||
       (random() * 255)::int::text,
       country_code,
       city,
       status = 'refunded',
       CASE WHEN status = 'refunded' THEN round(amount * (random() * 0.8 + 0.2)::numeric, 2) ELSE NULL END,
       CASE WHEN random() > 0.9 THEN 'Manual review required' ELSE NULL END,
       created_at,
       created_at + (random() * interval '2 hours'),
       CASE WHEN status = 'completed' THEN created_at + (random() * interval '24 hours') ELSE NULL END
FROM params;

-- Grant permissions for the new table
GRANT SELECT ON transactions TO reader_blue;
GRANT SELECT, INSERT, UPDATE, DELETE ON transactions TO writer_blue;
GRANT USAGE, SELECT ON SEQUENCE transactions_id_seq TO writer_blue;
