using SluiceBase.Api.Queries;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Tests;

public class PostgresPlanColumnExtractorTests
{
    [Fact]
    public void Extract_SimpleSelect_ResolvesRelationFromScanNode()
    {
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Schema":"public","Relation Name":"contacts",
          "Alias":"contacts","Output":["contacts.id","contacts.email"]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Contains(new ColumnRef("public", "contacts", "email"), cols);
        Assert.DoesNotContain(cols, c => c.Table == "users");
    }

    [Fact]
    public void Extract_WhereOnlyColumn_FromFilterExpression()
    {
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Schema":"public","Relation Name":"users",
          "Alias":"users","Output":["users.id"],"Filter":"(users.ssn IS NOT NULL)"}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Contains(new ColumnRef("public", "users", "ssn"), cols);
    }

    [Fact]
    public void Extract_Join_AttributesEachColumnToItsOwnRelation()
    {
        const string json = """
        [{"Plan":{"Node Type":"Hash Join","Output":["c.name","o.amount"],
          "Hash Cond":"(o.customer_id = c.id)","Plans":[
          {"Node Type":"Seq Scan","Schema":"public","Relation Name":"orders","Alias":"o",
            "Output":["o.amount","o.customer_id"]},
          {"Node Type":"Hash","Output":["c.name","c.id"],"Plans":[
            {"Node Type":"Seq Scan","Schema":"public","Relation Name":"customers","Alias":"c",
              "Output":["c.id","c.name"],"Filter":"(c.ssn IS NOT NULL)"}]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Contains(new ColumnRef("public", "customers", "ssn"), cols);
        Assert.Contains(new ColumnRef("public", "orders", "customer_id"), cols);
        Assert.DoesNotContain(cols, c => c.Table == "orders" && c.Column == "ssn");
    }

    [Fact]
    public void Extract_QualifiedStar_EmitsWholeRelationMarker()
    {
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Schema":"public","Relation Name":"users",
          "Alias":"u","Output":["to_jsonb(u.*)"]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Contains(new ColumnRef("public", "users", "*"), cols);
    }

    [Fact]
    public void Extract_SortAndGroupKeys_AreArrays()
    {
        const string json = """
        [{"Plan":{"Node Type":"Sort","Sort Key":["u.ssn"],"Output":["u.id"],"Plans":[
          {"Node Type":"Seq Scan","Schema":"public","Relation Name":"users","Alias":"u",
            "Output":["u.id","u.ssn"]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Contains(new ColumnRef("public", "users", "ssn"), cols);
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
        Assert.Contains(new ColumnRef("public", "secret_table", "ssn"), cols);
        Assert.Contains(new ColumnRef("public", "public_table", "name"), cols);
    }
}
