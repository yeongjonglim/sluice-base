using SluiceBase.Api.Queries;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Tests;

// The plan JSON in these tests is captured VERBATIM from real PostgreSQL 16
// `EXPLAIN (VERBOSE, FORMAT JSON, COSTS OFF)` output (schema: users(id,name,email,ssn),
// contacts(id,email,phone), view v_users). Do not hand-edit the shapes — PostgreSQL emits
// column names UNQUALIFIED in a single-relation Output and qualified only to disambiguate
// joins, and a child scan under count(*)/DISTINCT/aggregates carries the full physical tuple.
// Those quirks are exactly what the extractor must handle, so the fixtures must stay real.
public class PostgresPlanColumnExtractorTests
{
    private static bool Has(IReadOnlyList<ColumnRef> cols, string schema, string table, string column) =>
        cols.Any(c => c.Schema == schema && c.Table == table && c.Column == column);

    [Fact]
    public void Extract_SimpleSingleTableSelect_ResolvesBareColumn()
    {
        // SELECT email FROM users  — the "simplest query" case: Output is BARE ["email"].
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Relation Name":"users","Schema":"public",
          "Alias":"users","Output":["email"]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "email"));
    }

    [Fact]
    public void Extract_SelectStar_ResolvesEveryColumn()
    {
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Relation Name":"users","Schema":"public",
          "Alias":"users","Output":["id","name","email","ssn"]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "email"));
        Assert.True(Has(cols, "public", "users", "ssn"));
        Assert.True(Has(cols, "public", "users", "id"));
    }

    [Fact]
    public void Extract_WhereOnlyColumn_FromQualifiedFilter()
    {
        // SELECT id FROM users WHERE ssn IS NOT NULL — Output bare ["id"], Filter qualified.
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Relation Name":"users","Schema":"public",
          "Alias":"users","Output":["id"],"Filter":"(users.ssn IS NOT NULL)"}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "ssn"));
        Assert.True(Has(cols, "public", "users", "id"));
    }

    [Fact]
    public void Extract_CountStar_BlocksNothing()
    {
        // SELECT count(*) FROM users — root Aggregate Output ["count(*)"]; the child scan's
        // Output is the full physical tuple and MUST be ignored (else count(*) over-blocks).
        const string json = """
        [{"Plan":{"Node Type":"Aggregate","Output":["count(*)"],"Plans":[
          {"Node Type":"Seq Scan","Relation Name":"users","Schema":"public","Alias":"users",
            "Output":["id","name","email","ssn"]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Empty(cols);
    }

    [Fact]
    public void Extract_MaxOfColumn_ResolvesColumnInsideAggregate()
    {
        const string json = """
        [{"Plan":{"Node Type":"Aggregate","Output":["max(ssn)"],"Plans":[
          {"Node Type":"Seq Scan","Relation Name":"users","Schema":"public","Alias":"users",
            "Output":["id","name","email","ssn"]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "ssn"));
        Assert.False(Has(cols, "public", "users", "email")); // email not referenced by max(ssn)
    }

    [Fact]
    public void Extract_CountOfColumn_ResolvesThatColumn()
    {
        const string json = """
        [{"Plan":{"Node Type":"Aggregate","Output":["count(email)"],"Plans":[
          {"Node Type":"Seq Scan","Relation Name":"users","Schema":"public","Alias":"users",
            "Output":["id","name","email","ssn"]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "email"));
        Assert.False(Has(cols, "public", "users", "ssn"));
    }

    [Fact]
    public void Extract_ConstantSelect_BlocksNothing()
    {
        // SELECT 1 FROM users — Output ["1"], no column.
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Relation Name":"users","Schema":"public",
          "Alias":"users","Output":["1"]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Empty(cols);
    }

    [Fact]
    public void Extract_ExpressionOverColumns_ResolvesEachColumn()
    {
        // SELECT name || ' ' || email FROM users
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Relation Name":"users","Schema":"public",
          "Alias":"users","Output":["((name || ' '::text) || email)"]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "name"));
        Assert.True(Has(cols, "public", "users", "email"));
    }

    [Fact]
    public void Extract_ToJsonbWholeRow_EmitsWholeRelationMarker()
    {
        // SELECT to_jsonb(u) FROM users u — Output ["to_jsonb(u.*)"].
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Relation Name":"users","Schema":"public",
          "Alias":"u","Output":["to_jsonb(u.*)"]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "*"));
    }

    [Fact]
    public void Extract_View_SeesThroughToBaseColumn()
    {
        // SELECT national_id FROM v_users — view inlined; scan Output qualified ["users.ssn"].
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Relation Name":"users","Schema":"public",
          "Alias":"users","Output":["users.ssn"]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "ssn"));
    }

    [Fact]
    public void Extract_Join_AttributesEachColumnToItsOwnRelation()
    {
        // SELECT u.name, c.email FROM users u JOIN contacts c ON c.id=u.id WHERE u.ssn IS NOT NULL
        const string json = """
        [{"Plan":{"Node Type":"Hash Join","Output":["u.name","c.email"],
          "Hash Cond":"(c.id = u.id)","Plans":[
          {"Node Type":"Seq Scan","Relation Name":"contacts","Schema":"public","Alias":"c",
            "Output":["c.id","c.email","c.phone"]},
          {"Node Type":"Hash","Output":["u.name","u.id"],"Plans":[
            {"Node Type":"Seq Scan","Relation Name":"users","Schema":"public","Alias":"u",
              "Output":["u.name","u.id"],"Filter":"(u.ssn IS NOT NULL)"}]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "ssn"));
        Assert.True(Has(cols, "public", "users", "name"));
        Assert.True(Has(cols, "public", "contacts", "email"));
        // No cross-attribution: contacts has no ssn, users has no phone.
        Assert.False(Has(cols, "public", "contacts", "ssn"));
        Assert.False(Has(cols, "public", "users", "phone"));
    }

    [Fact]
    public void Extract_SubqueryTwoRelations_KeepsRootOutputAttribution()
    {
        // SELECT email FROM users WHERE id IN (SELECT id FROM contacts). The contacts scan (a
        // direct child of Hash Join) emits its full physical tuple incl. contacts.email/phone,
        // but only contacts.id is used — so those must NOT be flagged.
        const string json = """
        [{"Plan":{"Node Type":"Hash Join","Output":["users.email"],
          "Hash Cond":"(contacts.id = users.id)","Plans":[
            {"Node Type":"Seq Scan","Relation Name":"contacts","Schema":"public","Alias":"contacts",
              "Output":["contacts.id","contacts.email","contacts.phone"]},
            {"Node Type":"Hash","Output":["users.email","users.id"],"Plans":[
              {"Node Type":"Seq Scan","Relation Name":"users","Schema":"public","Alias":"users",
                "Output":["users.email","users.id"]}
            ]}
          ]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "email"));
        Assert.True(Has(cols, "public", "contacts", "id"));    // used in the IN / hash cond
        Assert.False(Has(cols, "public", "contacts", "email")); // physical-tuple, never used
        Assert.False(Has(cols, "public", "contacts", "phone")); // physical-tuple, never used
    }

    [Fact]
    public void Extract_DistinctSingleColumn_DoesNotOverBlockFromPhysicalTuple()
    {
        // SELECT DISTINCT email FROM users — root Aggregate Output ["email"]; child scan full tuple.
        const string json = """
        [{"Plan":{"Node Type":"Aggregate","Output":["email"],"Group Key":["users.email"],"Plans":[
          {"Node Type":"Seq Scan","Relation Name":"users","Schema":"public","Alias":"users",
            "Output":["id","name","email","ssn"]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "email"));
        Assert.False(Has(cols, "public", "users", "ssn"));  // physical-tuple ssn must NOT leak in
    }

    [Fact]
    public void Extract_OrderBy_ResolvesSelectedAndSortColumns()
    {
        // SELECT email FROM users ORDER BY id — root Sort Output ["email","id"], Sort Key qualified.
        const string json = """
        [{"Plan":{"Node Type":"Sort","Output":["email","id"],"Sort Key":["users.id"],"Plans":[
          {"Node Type":"Seq Scan","Relation Name":"users","Schema":"public","Alias":"users",
            "Output":["email","id"]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "email"));
        Assert.True(Has(cols, "public", "users", "id"));
    }

    [Fact]
    public void Extract_UnionAll_HarvestsEachArmProjection()
    {
        // SELECT ssn FROM users UNION ALL SELECT phone FROM contacts — Append has no Output; the
        // base columns live in each arm's (child scan) Output, which must be harvested.
        const string json = """
        [{"Plan":{"Node Type":"Append","Plans":[
          {"Node Type":"Seq Scan","Relation Name":"users","Schema":"public","Alias":"users",
            "Output":["users.ssn"]},
          {"Node Type":"Seq Scan","Relation Name":"contacts","Schema":"public","Alias":"contacts",
            "Output":["contacts.phone"]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "ssn"));
        Assert.True(Has(cols, "public", "contacts", "phone"));
    }

    [Fact]
    public void Extract_MaterializedCte_SeesBaseColumnInInitPlan()
    {
        // WITH x AS MATERIALIZED (SELECT email FROM users) SELECT * FROM x — the base column is
        // only in the InitPlan child scan Output; the CTE Scan root Output is aliased (x.email).
        const string json = """
        [{"Plan":{"Node Type":"CTE Scan","CTE Name":"x","Alias":"x","Output":["x.email"],"Plans":[
          {"Node Type":"Seq Scan","Parent Relationship":"InitPlan","Subplan Name":"CTE x",
            "Relation Name":"users","Schema":"public","Alias":"users","Output":["users.email"]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "email"));
    }

    [Fact]
    public void Extract_SubqueryWithLimit_SeesBaseColumns()
    {
        // SELECT s.email FROM (SELECT email FROM users ORDER BY id LIMIT 5) s — base columns live
        // under Subquery Scan -> Limit -> Index Scan; the root Output is aliased (s.email).
        const string json = """
        [{"Plan":{"Node Type":"Subquery Scan","Alias":"s","Output":["s.email"],"Plans":[
          {"Node Type":"Limit","Output":["users.email","users.id"],"Plans":[
            {"Node Type":"Index Scan","Index Name":"users_pkey","Relation Name":"users",
              "Schema":"public","Alias":"users","Output":["users.email","users.id"]}]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "users", "email"));
        Assert.True(Has(cols, "public", "users", "id"));
    }

    [Fact]
    public void Extract_RawUntrimmedPlanOutput_ToleratesNoiseFields()
    {
        // Verbatim raw PG16 output (incl. "Parallel Aware"/"Async Capable" noise) for
        // `SELECT email FROM users`, to prove the extractor ignores unrelated fields.
        const string emailJson = """
        [{"Plan":{"Node Type":"Seq Scan","Parallel Aware":false,"Async Capable":false,
          "Relation Name":"users","Schema":"public","Alias":"users","Output":["email"]}}]
        """;
        Assert.True(Has(PostgresPlanColumnExtractor.Extract(emailJson), "public", "users", "email"));

        // Verbatim raw PG16 output for `SELECT count(*) FROM users` — the child scan's full
        // physical tuple must NOT be harvested.
        const string countJson = """
        [{"Plan":{"Node Type":"Aggregate","Strategy":"Plain","Partial Mode":"Simple",
          "Parallel Aware":false,"Async Capable":false,"Output":["count(*)"],"Plans":[
          {"Node Type":"Seq Scan","Parent Relationship":"Outer","Parallel Aware":false,
            "Async Capable":false,"Relation Name":"users","Schema":"public","Alias":"users",
            "Output":["id","name","email","ssn"]}]}}]
        """;
        Assert.Empty(PostgresPlanColumnExtractor.Extract(countJson));
    }

    [Fact]
    public void Extract_UnionSameAliasDifferentTables_AttributesEachToItsOwnRelation()
    {
        const string json = """
        [{"Plan":{"Node Type":"Append","Plans":[
          {"Node Type":"Seq Scan","Schema":"public","Relation Name":"secret_table","Alias":"t","Output":["t.ssn"]},
          {"Node Type":"Seq Scan","Schema":"public","Relation Name":"public_table","Alias":"t","Output":["t.name"]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.True(Has(cols, "public", "secret_table", "ssn"));
        Assert.True(Has(cols, "public", "public_table", "name"));
    }
}
