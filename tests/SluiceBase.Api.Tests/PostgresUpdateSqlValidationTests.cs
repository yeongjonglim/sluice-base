using SluiceBase.Api.Targets;

namespace SluiceBase.Api.Tests;

public class PostgresUpdateSqlValidationTests
{
    private static readonly PostgresTargetEngine Engine = new();

    [Theory]
    [InlineData("UPDATE t SET a = 1 WHERE id = 1")]
    [InlineData("SELECT id, a FROM t WHERE id = 1; UPDATE t SET a = 2 WHERE id = 1 RETURNING id, a")]
    [InlineData("WITH x AS (SELECT 1 AS id) UPDATE t SET a = 1 FROM x WHERE t.id = x.id")]
    [InlineData("DO $$ BEGIN PERFORM 1; END $$")]
    [InlineData("UPDATE t SET note = 'COMMIT; BEGIN;' WHERE id = 1")]
    [InlineData("-- COMMIT first\nUPDATE t SET a = 1")]
    [InlineData("/* ROLLBACK */ DELETE FROM t WHERE id = 1")]
    [InlineData("PREPARE p AS SELECT 1")]
    [InlineData("UPDATE \"commit\" SET a = 1")]
    public void ValidateUpdateSql_AllowsOrdinaryStatements(string sql)
    {
        Assert.Null(Engine.ValidateUpdateSql(sql));
    }

    [Theory]
    [InlineData("COMMIT")]
    [InlineData("commit")]
    [InlineData("UPDATE t SET a = 1; COMMIT; BEGIN;")]
    [InlineData("COMMIT AND CHAIN; DELETE FROM t; COMMIT AND CHAIN;")]
    [InlineData("COMMIT; DELETE FROM t")]
    [InlineData("END; DELETE FROM t; START TRANSACTION;")]
    [InlineData("BEGIN")]
    [InlineData("START TRANSACTION")]
    [InlineData("ROLLBACK")]
    [InlineData("ABORT")]
    [InlineData("SAVEPOINT s")]
    [InlineData("RELEASE SAVEPOINT s")]
    [InlineData("ROLLBACK TO SAVEPOINT s")]
    [InlineData("PREPARE TRANSACTION 'x'")]
    [InlineData("COMMIT PREPARED 'x'")]
    [InlineData("/* hide */ COMMIT")]
    [InlineData("/* outer /* nested */ still */ COMMIT")]
    [InlineData("-- hide\nCOMMIT")]
    public void ValidateUpdateSql_RejectsTransactionControl(string sql)
    {
        Assert.Contains("transaction", Engine.ValidateUpdateSql(sql));
    }
}
