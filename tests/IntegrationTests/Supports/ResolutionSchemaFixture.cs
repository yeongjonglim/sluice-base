using Npgsql;

namespace IntegrationTests.Supports;

// Seeds an isolated schema with the relations the resolution tests query, so runs do not
// collide with other tests sharing blue-appdb. Returns the schema name; callers qualify
// table names or SET search_path.
internal static class ResolutionSchemaFixture
{
    public static async Task<string> CreateAsync(string connectionString, CancellationToken ct)
    {
        var schema = $"res_{Guid.NewGuid():N}"[..12];
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($"""
            CREATE SCHEMA "{schema}";
            CREATE TABLE "{schema}".users (id serial PRIMARY KEY, name text, email text, ssn text);
            CREATE TABLE "{schema}".contacts (id serial PRIMARY KEY, email text, phone text);
            CREATE TABLE "{schema}".orders (id serial PRIMARY KEY, customer_id int, amount numeric);
            CREATE VIEW "{schema}".v_users AS
              SELECT id, name AS display_name, ssn AS national_id FROM "{schema}".users;
            INSERT INTO "{schema}".users(name,email,ssn) VALUES ('a','a@x.com','111');
            INSERT INTO "{schema}".contacts(email,phone) VALUES ('c@x.com','555');
            INSERT INTO "{schema}".orders(customer_id,amount) VALUES (1, 9.99);
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
        return schema;
    }
}
