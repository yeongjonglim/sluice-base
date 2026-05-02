-- Create roles for read/write split
CREATE ROLE reader_green LOGIN PASSWORD 'reader_green';
CREATE ROLE writer_green LOGIN PASSWORD 'writer_green';

-- Different schema from blue to exercise multi-server UX
CREATE TABLE employees (
    id          serial PRIMARY KEY,
    first_name  text NOT NULL,
    last_name   text NOT NULL,
    department  text NOT NULL,
    salary      numeric(10,2) NOT NULL,
    hired_at    date NOT NULL DEFAULT CURRENT_DATE
);

CREATE TABLE departments (
    id          serial PRIMARY KEY,
    name        text NOT NULL UNIQUE,
    budget      numeric(12,2) NOT NULL
);

CREATE TABLE projects (
    id           serial PRIMARY KEY,
    title        text NOT NULL,
    department_id int NOT NULL REFERENCES departments(id),
    start_date   date NOT NULL,
    end_date     date
);

-- Seed sample data
INSERT INTO departments (name, budget) VALUES
    ('Engineering', 1500000),
    ('Marketing', 500000),
    ('Sales', 800000),
    ('HR', 300000),
    ('Finance', 600000);

INSERT INTO employees (first_name, last_name, department, salary, hired_at) SELECT
    (ARRAY['Alice','Bob','Carol','David','Eve','Frank','Grace','Hank','Iris','Jack'])[ceil(random()*10)::int],
    (ARRAY['Smith','Jones','Williams','Brown','Davis','Miller','Wilson','Moore','Taylor','Anderson'])[ceil(random()*10)::int],
    (ARRAY['Engineering','Marketing','Sales','HR','Finance'])[ceil(random()*5)::int],
    (50000 + random() * 100000)::numeric(10,2),
    CURRENT_DATE - (random() * 1000)::int
FROM generate_series(1, 80);

INSERT INTO projects (title, department_id, start_date, end_date) SELECT
    'Project ' || i,
    (random() * 4 + 1)::int,
    CURRENT_DATE - (random() * 365)::int,
    CASE WHEN random() > 0.5 THEN CURRENT_DATE + (random() * 180)::int ELSE NULL END
FROM generate_series(1, 30) AS i;

-- Grant permissions
GRANT USAGE ON SCHEMA public TO reader_green;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO reader_green;

GRANT USAGE ON SCHEMA public TO writer_green;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO writer_green;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO writer_green;
