-- Demonstrates how the schema sidebar handles very long table and column names:
-- the names should scroll horizontally while the database picker, accordion chevrons
-- and the "append SELECT" button stay pinned at the panel width.
CREATE TABLE customer_subscription_billing_cycle_adjustment_audit
(
    id                                                            serial PRIMARY KEY,
    customer_account_identifier_with_a_very_long_descriptive_name text           NOT NULL,
    subscription_billing_cycle_adjustment_reason_description       text           NOT NULL,
    previous_monthly_recurring_revenue_amount_in_account_currency  numeric(12, 2) NOT NULL,
    adjusted_monthly_recurring_revenue_amount_in_account_currency  numeric(12, 2) NOT NULL,
    adjustment_effective_date_for_the_next_billing_cycle_start     date           NOT NULL DEFAULT CURRENT_DATE,
    created_timestamp_with_time_zone_when_record_was_inserted      timestamptz    NOT NULL DEFAULT now()
);

INSERT INTO customer_subscription_billing_cycle_adjustment_audit
(customer_account_identifier_with_a_very_long_descriptive_name,
 subscription_billing_cycle_adjustment_reason_description,
 previous_monthly_recurring_revenue_amount_in_account_currency,
 adjusted_monthly_recurring_revenue_amount_in_account_currency)
SELECT 'ACCT-' || lpad(i::text, 8, '0'),
       'Adjustment ' || i || ' applied for plan upgrade and mid-cycle proration',
       (50 + random() * 500)::numeric(12, 2),
       (50 + random() * 500)::numeric(12, 2)
FROM generate_series(1, 20) AS i;

-- Grant permissions to match the roles created in 01-init.sql
GRANT SELECT ON customer_subscription_billing_cycle_adjustment_audit TO reader_green;
GRANT SELECT, INSERT, UPDATE, DELETE ON customer_subscription_billing_cycle_adjustment_audit TO writer_green;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO writer_green;
