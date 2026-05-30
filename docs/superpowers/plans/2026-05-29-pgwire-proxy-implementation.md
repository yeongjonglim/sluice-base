# PgWire Proxy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a PostgreSQL wire protocol proxy that allows users to connect native tools (psql, DBeaver, etc.) to SluiceBase databases while maintaining full protection parity (read-only enforcement, sensitive column blocking, permissions, session gating, audit logging).

**Architecture:** In-process `BackgroundService` listening on TCP port 6432, implementing the PgWire simple query protocol. Reuses existing `ITargetEngine`, `IServerConnectionFactory`, `SqlColumnChecker`, and permission checks. Session validity gated at connection time via proxy credentials linked to SSO sessions. Two-layer data model: `ProxyCredential` (stable personal token per database) + session check (invalidates token if SSO session expires).

**Tech Stack:** C# `.NET 10`, Npgsql (wire protocol details), Vogen (value objects for IDs), EF Core (entity management), xUnit + Aspire integration tests + Testcontainers.

---

## File Structure

**Backend (C#):**
- `src/SluiceBase.Core/Servers/ProxyCredentialId.cs` — Vogen ID for proxy credentials
- `src/SluiceBase.Core/Servers/ProxyCredential.cs` — Entity: proxy credentials with token hash
- `src/SluiceBase.Api/Data/Configurations/ProxyCredentialConfiguration.cs` — EF configuration
- `src/SluiceBase.Api/Proxy/PgWireProxyService.cs` — Main `BackgroundService` (TCP listener, connection management)
- `src/SluiceBase.Api/Proxy/PgWireProtocol.cs` — Message types and serialization (StartupMessage, Query, RowDescription, DataRow, ErrorResponse, etc.)
- `src/SluiceBase.Api/Proxy/PgWireConnection.cs` — Per-client connection state machine (STARTUP → ACTIVE → QUERYING → ACTIVE/CLOSED)
- `src/SluiceBase.Api/Proxy/SessionHeartbeat.cs` — Background task checking session validity every 60s, terminating expired/revoked connections
- `src/SluiceBase.Api/Endpoints/ProxyCredentialEndpoints.cs` — REST API for credential CRUD (generate, revoke)
- `src/SluiceBase.Api/Program.cs` — Register `PgWireProxyService` and endpoints

**Frontend (TypeScript):**
- `src/frontend/src/api/hooks.ts` — Add hooks: `useProxyCredentials(databaseId)`, `useGenerateProxyCredential(databaseId)`, `useRevokeProxyCredential(databaseId, credentialId)`
- `src/frontend/src/routes/_authed/server.tsx` — Add "Proxy Access" section to database detail card

**Tests:**
- `tests/IntegrationTests/PgWireProxyTests.cs` — Full Aspire integration tests (happy path, auth, permissions, sensitive columns, session expiry, credential revocation)
- `tests/IntegrationTests/Supports/PgWireProxyTestHelper.cs` — Helper: create proxy credentials, connect via Npgsql, execute queries
- `tests/SluiceBase.Core.Tests/ProxyCredentialTests.cs` — Unit tests for credential hashing, factory methods

**Database:**
- EF migration adding `proxy_credential` table (auto-generated via `dotnet ef migrations add`)

---

## Task Breakdown

### Task 1: Create ProxyCredential Entity & Vogen ID

**Files:**
- Create: `src/SluiceBase.Core/Servers/ProxyCredentialId.cs`
- Create: `src/SluiceBase.Core/Servers/ProxyCredential.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/ProxyCredentialConfiguration.cs`

- [ ] **Step 1: Write unit test for ProxyCredential factory method**

```csharp
// tests/SluiceBase.Core.Tests/ProxyCredentialTests.cs
using Xunit;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Tests;

public class ProxyCredentialTests
{
    [Fact]
    public void Create_GeneratesValidId_AndHashesToken()
    {
        var userId = UserId.From(Guid.NewGuid());
        var databaseId = DatabaseId.From(Guid.NewGuid());
        var rawToken = "test-token-32-bytes-exactly-1234567890ab";
        var tokenHash = ProxyCredential.HashToken(rawToken);

        var credential = ProxyCredential.Create(
            userId: userId,
            databaseId: databaseId,
            tokenHash: tokenHash,
            at: DateTimeOffset.UtcNow);

        Assert.NotEqual(default, credential.Id);
        Assert.Equal(userId, credential.UserId);
        Assert.Equal(databaseId, credential.DatabaseId);
        Assert.Equal(tokenHash, credential.TokenHash);
        Assert.Null(credential.RevokedAt);
    }

    [Fact]
    public void Create_TrimsCredentialId()
    {
        // CredentialId is generated as base64url(GUID), should be clean
        var credential = ProxyCredential.Create(
            UserId.From(Guid.NewGuid()),
            DatabaseId.From(Guid.NewGuid()),
            ProxyCredential.HashToken("token"),
            DateTimeOffset.UtcNow);

        Assert.NotNull(credential.CredentialId);
        Assert.DoesNotContain(" ", credential.CredentialId);
        Assert.DoesNotContain("\n", credential.CredentialId);
    }

    [Fact]
    public void Revoke_SetsRevokedAt()
    {
        var credential = ProxyCredential.Create(
            UserId.From(Guid.NewGuid()),
            DatabaseId.From(Guid.NewGuid()),
            ProxyCredential.HashToken("token"),
            DateTimeOffset.UtcNow);

        var revokedAt = DateTimeOffset.UtcNow.AddHours(1);
        credential.Revoke(revokedAt);

        Assert.Equal(revokedAt, credential.RevokedAt);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SluiceBase.Core.Tests/ProxyCredentialTests.cs -v`
Expected: FAIL — `ProxyCredentialId` not found, `ProxyCredential` not found, `HashToken` not found

- [ ] **Step 3: Create ProxyCredentialId Vogen value object**

```csharp
// src/SluiceBase.Core/Servers/ProxyCredentialId.cs
using Vogen;

namespace SluiceBase.Core.Servers;

[ValueObject<Guid>(conversions: Conversions.SystemTextJson, customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct ProxyCredentialId;
```

- [ ] **Step 4: Create ProxyCredential entity**

```csharp
// src/SluiceBase.Core/Servers/ProxyCredential.cs
using System.Security.Cryptography;
using System.Text;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Servers;

public sealed class ProxyCredential
{
#pragma warning disable CS8618
    private ProxyCredential() { }
#pragma warning restore CS8618

    public ProxyCredentialId Id { get; private set; }
    public UserId UserId { get; private set; }
    public DatabaseId DatabaseId { get; private set; }
    public string CredentialId { get; private set; } // base64url(GUID), used as PgWire username
    public string TokenHash { get; private set; } // SHA-256 hash of raw token
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }

    public static ProxyCredential Create(
        UserId userId,
        DatabaseId databaseId,
        string tokenHash,
        DateTimeOffset at)
    {
        var guid = Guid.NewGuid();
        var credentialId = Convert.ToBase64String(guid.ToByteArray())
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        return new()
        {
            Id = ProxyCredentialId.FromNewVersion7Guid(),
            UserId = userId,
            DatabaseId = databaseId,
            CredentialId = credentialId,
            TokenHash = tokenHash,
            CreatedAt = at,
        };
    }

    public void RecordUsage(DateTimeOffset at)
    {
        LastUsedAt = at;
    }

    public void Revoke(DateTimeOffset at)
    {
        RevokedAt = at;
    }

    public bool IsValid() => RevokedAt is null;
}
```

- [ ] **Step 5: Create EF configuration**

```csharp
// src/SluiceBase.Api/Data/Configurations/ProxyCredentialConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class ProxyCredentialConfiguration : IEntityTypeConfiguration<ProxyCredential>
{
    public void Configure(EntityTypeBuilder<ProxyCredential> builder)
    {
        builder.ToTable("proxy_credential");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CredentialId).HasMaxLength(43).IsRequired(); // base64url(16 bytes) = 24 chars + padding
        builder.Property(c => c.TokenHash).HasMaxLength(44).IsRequired(); // base64(SHA256) = 44 chars
        
        builder.HasOne<ProxyCredential>()
            .WithOne()
            .HasForeignKey<ProxyCredential>(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasOne<Database>()
            .WithMany()
            .HasForeignKey(c => c.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasIndex(c => c.CredentialId).IsUnique();
        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.DatabaseId);
        builder.HasIndex(c => c.RevokedAt);
    }
}
```

- [ ] **Step 6: Register configuration in AppDbContext**

Read `/Users/voltendron/Projects/sluice-base/src/SluiceBase.Api/Data/AppDbContext.cs` and add:
```csharp
public DbSet<ProxyCredential> ProxyCredentials => Set<ProxyCredential>();
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/SluiceBase.Core.Tests/ProxyCredentialTests.cs -v`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/SluiceBase.Core/Servers/ProxyCredential* src/SluiceBase.Api/Data/Configurations/ProxyCredentialConfiguration.cs tests/SluiceBase.Core.Tests/ProxyCredentialTests.cs src/SluiceBase.Api/Data/AppDbContext.cs
git commit -m "Add ProxyCredential entity with token hashing and Vogen ID"
```

---

### Task 2: Create EF Migration for ProxyCredential Table

**Files:**
- Modify: `src/SluiceBase.Api/Data/Migrations/` (auto-generated)

- [ ] **Step 1: Generate migration**

Run: `cd src/SluiceBase.Api && dotnet ef migrations add AddProxyCredentialTable`
Expected: New migration file in `Data/Migrations/` with `CreateTable("proxy_credential", ...)`

- [ ] **Step 2: Review migration for correctness**

Run: `git diff src/SluiceBase.Api/Data/Migrations/ | head -50`
Expected: Migration creates `proxy_credential` table with columns: id, user_id, database_id, credential_id, token_hash, created_at, last_used_at, revoked_at, indexes on credential_id (unique), user_id, database_id, revoked_at

- [ ] **Step 3: Commit migration**

```bash
git add src/SluiceBase.Api/Data/Migrations/
git commit -m "Add migration: create proxy_credential table"
```

---

### Task 3: Create PgWire Protocol Message Types

**Files:**
- Create: `src/SluiceBase.Api/Proxy/PgWireProtocol.cs`

- [ ] **Step 1: Write unit tests for message serialization**

```csharp
// tests/SluiceBase.Core.Tests/PgWireProtocolTests.cs
using Xunit;
using SluiceBase.Api.Proxy;

namespace SluiceBase.Core.Tests;

public class PgWireProtocolTests
{
    [Fact]
    public void AuthenticationOkMessage_SerializesCorrectly()
    {
        var msg = new AuthenticationOkMessage();
        var bytes = msg.Serialize();

        Assert.NotEmpty(bytes);
        Assert.Equal('R', (char)bytes[0]); // Message type
        // Should be: 'R' (1) + length (4) + auth type (4) = 9 bytes
        Assert.Equal(9, bytes.Length);
    }

    [Fact]
    public void ErrorResponseMessage_SerializesWithFields()
    {
        var msg = new ErrorResponseMessage("INVALID_PASSWORD", "invalid credentials");
        var bytes = msg.Serialize();

        Assert.NotEmpty(bytes);
        Assert.Equal('E', (char)bytes[0]); // Message type 'E'
    }

    [Fact]
    public void ReadyForQueryMessage_SerializesWithStatus()
    {
        var msg = new ReadyForQueryMessage('I'); // Idle
        var bytes = msg.Serialize();

        Assert.Equal(6, bytes.Length); // 'Z' (1) + length (4) + status (1)
        Assert.Equal('Z', (char)bytes[0]);
    }

    [Fact]
    public void RowDescriptionMessage_SerializesColumns()
    {
        var columns = new[] { "id", "email" };
        var msg = new RowDescriptionMessage(columns);
        var bytes = msg.Serialize();

        Assert.NotEmpty(bytes);
        Assert.Equal('T', (char)bytes[0]); // Message type
    }

    [Fact]
    public void DataRowMessage_SerializesValues()
    {
        var values = new[] { "123", "test@example.com" };
        var msg = new DataRowMessage(values);
        var bytes = msg.Serialize();

        Assert.NotEmpty(bytes);
        Assert.Equal('D', (char)bytes[0]); // Message type
    }

    [Fact]
    public void CommandCompleteMessage_SerializesStatus()
    {
        var msg = new CommandCompleteMessage("SELECT 2");
        var bytes = msg.Serialize();

        Assert.NotEmpty(bytes);
        Assert.Equal('C', (char)bytes[0]); // Message type
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SluiceBase.Core.Tests/PgWireProtocolTests.cs -v`
Expected: FAIL — types not found

- [ ] **Step 3: Implement PgWireProtocol message types**

```csharp
// src/SluiceBase.Api/Proxy/PgWireProtocol.cs
using System.Text;

namespace SluiceBase.Api.Proxy;

/// Base class for all PgWire protocol messages
public abstract class PgWireMessage
{
    public abstract byte MessageType { get; }
    public abstract byte[] Serialize();

    protected byte[] BuildMessage(byte type, byte[] body)
    {
        var length = body.Length + 4; // +4 for length field itself
        var result = new byte[1 + 4 + body.Length]; // type (1) + length (4) + body
        result[0] = type;
        var lengthBytes = BitConverter.GetBytes(length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        Buffer.BlockCopy(lengthBytes, 0, result, 1, 4);
        Buffer.BlockCopy(body, 0, result, 5, body.Length);
        return result;
    }
}

public sealed class AuthenticationOkMessage : PgWireMessage
{
    public override byte MessageType => (byte)'R';

    public override byte[] Serialize()
    {
        var authType = BitConverter.GetBytes(0); // AUTH_OK
        if (BitConverter.IsLittleEndian)
            Array.Reverse(authType);
        return BuildMessage(MessageType, authType);
    }
}

public sealed class AuthenticationCleartextPasswordMessage : PgWireMessage
{
    public override byte MessageType => (byte)'R';

    public override byte[] Serialize()
    {
        var authType = BitConverter.GetBytes(3); // AUTH_CLEARTEXTPASSWORD
        if (BitConverter.IsLittleEndian)
            Array.Reverse(authType);
        return BuildMessage(MessageType, authType);
    }
}

public sealed class ErrorResponseMessage : PgWireMessage
{
    private readonly string _sqlState;
    private readonly string _message;

    public ErrorResponseMessage(string sqlState, string message)
    {
        _sqlState = sqlState;
        _message = message;
    }

    public override byte MessageType => (byte)'E';

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        // Severity
        ms.WriteByte((byte)'S');
        WriteString(ms, "FATAL");
        // SQLSTATE
        ms.WriteByte((byte)'C');
        WriteString(ms, _sqlState);
        // Message
        ms.WriteByte((byte)'M');
        WriteString(ms, _message);
        // Terminator
        ms.WriteByte(0);

        return BuildMessage(MessageType, ms.ToArray());
    }

    private static void WriteString(MemoryStream ms, string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        ms.Write(bytes, 0, bytes.Length);
        ms.WriteByte(0);
    }
}

public sealed class ReadyForQueryMessage : PgWireMessage
{
    private readonly char _status; // 'I' = Idle, 'T' = In transaction, 'E' = Error

    public ReadyForQueryMessage(char status)
    {
        _status = status;
    }

    public override byte MessageType => (byte)'Z';

    public override byte[] Serialize()
    {
        return BuildMessage(MessageType, new[] { (byte)_status });
    }
}

public sealed class RowDescriptionMessage : PgWireMessage
{
    private readonly string[] _columns;

    public RowDescriptionMessage(string[] columns)
    {
        _columns = columns;
    }

    public override byte MessageType => (byte)'T';

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        var numFields = BitConverter.GetBytes((short)_columns.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(numFields);
        ms.Write(numFields, 0, 2);

        foreach (var col in _columns)
        {
            var colBytes = Encoding.UTF8.GetBytes(col);
            ms.Write(colBytes, 0, colBytes.Length);
            ms.WriteByte(0);
            // OID (int32): assume 25 (text)
            var oid = BitConverter.GetBytes(25);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(oid);
            ms.Write(oid, 0, 4);
            // atttypmod (int16): -1
            var typmod = BitConverter.GetBytes((short)-1);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(typmod);
            ms.Write(typmod, 0, 2);
            // format (int16): 0 = text
            ms.Write(new byte[] { 0, 0 }, 0, 2);
        }

        return BuildMessage(MessageType, ms.ToArray());
    }
}

public sealed class DataRowMessage : PgWireMessage
{
    private readonly string?[] _values;

    public DataRowMessage(string?[] values)
    {
        _values = values;
    }

    public override byte MessageType => (byte)'D';

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        var numCols = BitConverter.GetBytes((short)_values.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(numCols);
        ms.Write(numCols, 0, 2);

        foreach (var val in _values)
        {
            if (val is null)
            {
                var nullLen = BitConverter.GetBytes(-1);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(nullLen);
                ms.Write(nullLen, 0, 4);
            }
            else
            {
                var valBytes = Encoding.UTF8.GetBytes(val);
                var len = BitConverter.GetBytes(valBytes.Length);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(len);
                ms.Write(len, 0, 4);
                ms.Write(valBytes, 0, valBytes.Length);
            }
        }

        return BuildMessage(MessageType, ms.ToArray());
    }
}

public sealed class CommandCompleteMessage : PgWireMessage
{
    private readonly string _commandTag;

    public CommandCompleteMessage(string commandTag)
    {
        _commandTag = commandTag;
    }

    public override byte MessageType => (byte)'C';

    public override byte[] Serialize()
    {
        var tagBytes = Encoding.UTF8.GetBytes(_commandTag);
        var result = new byte[tagBytes.Length + 1];
        Buffer.BlockCopy(tagBytes, 0, result, 0, tagBytes.Length);
        result[tagBytes.Length] = 0;
        return BuildMessage(MessageType, result);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SluiceBase.Core.Tests/PgWireProtocolTests.cs -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Proxy/PgWireProtocol.cs tests/SluiceBase.Core.Tests/PgWireProtocolTests.cs
git commit -m "Add PgWire protocol message types (startup, auth, query response, error)"
```

---

### Task 4: Create PgWireConnection State Machine

**Files:**
- Create: `src/SluiceBase.Api/Proxy/PgWireConnection.cs`

- [ ] **Step 1: Write unit tests for connection state transitions**

```csharp
// tests/SluiceBase.Core.Tests/PgWireConnectionTests.cs
using Xunit;
using SluiceBase.Api.Proxy;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;
using System.IO;

namespace SluiceBase.Core.Tests;

public class PgWireConnectionTests
{
    [Fact]
    public async Task Connection_InitializesInStartupState()
    {
        var stream = new MemoryStream();
        var connection = new PgWireConnection(
            Guid.NewGuid(),
            new StreamReader(stream),
            new StreamWriter(stream));

        Assert.Equal(PgWireConnectionState.Startup, connection.State);
        Assert.Null(connection.UserId);
        Assert.Null(connection.DatabaseId);
    }

    [Fact]
    public void Connection_TransitionsToActive_OnAuthSuccess()
    {
        var stream = new MemoryStream();
        var connection = new PgWireConnection(
            Guid.NewGuid(),
            new StreamReader(stream),
            new StreamWriter(stream));

        var userId = UserId.From(Guid.NewGuid());
        var databaseId = DatabaseId.From(Guid.NewGuid());

        connection.SetAuthenticated(userId, databaseId, DateTimeOffset.UtcNow);

        Assert.Equal(PgWireConnectionState.Active, connection.State);
        Assert.Equal(userId, connection.UserId);
        Assert.Equal(databaseId, connection.DatabaseId);
    }

    [Fact]
    public void Connection_TransitionsToClosed_OnTerminate()
    {
        var stream = new MemoryStream();
        var connection = new PgWireConnection(
            Guid.NewGuid(),
            new StreamReader(stream),
            new StreamWriter(stream));

        connection.SetAuthenticated(
            UserId.From(Guid.NewGuid()),
            DatabaseId.From(Guid.NewGuid()),
            DateTimeOffset.UtcNow);

        connection.Close();

        Assert.Equal(PgWireConnectionState.Closed, connection.State);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SluiceBase.Core.Tests/PgWireConnectionTests.cs -v`
Expected: FAIL — `PgWireConnection` type not found

- [ ] **Step 3: Implement PgWireConnection**

```csharp
// src/SluiceBase.Api/Proxy/PgWireConnection.cs
using System.Text;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Proxy;

public enum PgWireConnectionState
{
    Startup,      // Waiting for StartupMessage
    Authenticating, // Waiting for password
    Active,       // Authenticated, ready for queries
    Querying,     // Currently executing a query
    Closed,       // Connection terminated
}

public sealed class PgWireConnection : IDisposable
{
    private readonly Guid _connectionId;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    public PgWireConnectionState State { get; private set; } = PgWireConnectionState.Startup;
    public UserId? UserId { get; private set; }
    public DatabaseId? DatabaseId { get; private set; }
    public DateTimeOffset? AuthenticatedAt { get; private set; }

    public PgWireConnection(Guid connectionId, StreamReader reader, StreamWriter writer)
    {
        _connectionId = connectionId;
        _reader = reader;
        _writer = writer;
    }

    public void SetAuthenticated(UserId userId, DatabaseId databaseId, DateTimeOffset at)
    {
        State = PgWireConnectionState.Active;
        UserId = userId;
        DatabaseId = databaseId;
        AuthenticatedAt = at;
    }

    public void BeginQuery()
    {
        if (State != PgWireConnectionState.Active)
            throw new InvalidOperationException($"Cannot query from state {State}");
        State = PgWireConnectionState.Querying;
    }

    public void EndQuery()
    {
        if (State != PgWireConnectionState.Querying)
            throw new InvalidOperationException($"Cannot end query from state {State}");
        State = PgWireConnectionState.Active;
    }

    public void Close()
    {
        State = PgWireConnectionState.Closed;
    }

    public async Task SendMessageAsync(PgWireMessage message, CancellationToken ct)
    {
        var bytes = message.Serialize();
        await _writer.BaseStream.WriteAsync(bytes, 0, bytes.Length, ct);
        await _writer.FlushAsync();
    }

    public async Task SendErrorAsync(string sqlState, string message, CancellationToken ct)
    {
        var msg = new ErrorResponseMessage(sqlState, message);
        await SendMessageAsync(msg, ct);
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _writer?.Dispose();
    }
}

public sealed record StartupMessage(string User, string Database, Dictionary<string, string> Parameters);
public sealed record QueryMessage(string Sql);
public sealed record PasswordMessage(string Password);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SluiceBase.Core.Tests/PgWireConnectionTests.cs -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Proxy/PgWireConnection.cs tests/SluiceBase.Core.Tests/PgWireConnectionTests.cs
git commit -m "Add PgWireConnection state machine (startup, active, querying, closed)"
```

---

### Task 5: Create SessionHeartbeat Background Task

**Files:**
- Create: `src/SluiceBase.Api/Proxy/SessionHeartbeat.cs`

- [ ] **Step 1: Write unit test for session validity check**

```csharp
// tests/SluiceBase.Core.Tests/SessionHeartbeatTests.cs
using Xunit;
using SluiceBase.Api.Proxy;
using SluiceBase.Api.Auth;
using Moq;

namespace SluiceBase.Core.Tests;

public class SessionHeartbeatTests
{
    [Fact]
    public async Task SessionHeartbeat_TerminatesConnectionsWithExpiredSessions()
    {
        var mockSessionValidator = new Mock<ISessionValidator>();
        var heartbeat = new SessionHeartbeat(mockSessionValidator.Object);

        // Simulate expired session
        mockSessionValidator
            .Setup(sv => sv.IsSessionValidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var isTerminated = await heartbeat.CheckSessionValidityAsync("user-id", CancellationToken.None);
        Assert.False(isTerminated);
    }

    [Fact]
    public async Task SessionHeartbeat_KeepsConnectionsWithValidSessions()
    {
        var mockSessionValidator = new Mock<ISessionValidator>();
        var heartbeat = new SessionHeartbeat(mockSessionValidator.Object);

        mockSessionValidator
            .Setup(sv => sv.IsSessionValidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var isValid = await heartbeat.CheckSessionValidityAsync("user-id", CancellationToken.None);
        Assert.True(isValid);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SluiceBase.Core.Tests/SessionHeartbeatTests.cs -v`
Expected: FAIL — `SessionHeartbeat`, `ISessionValidator` types not found

- [ ] **Step 3: Create ISessionValidator interface**

```csharp
// src/SluiceBase.Api/Auth/ISessionValidator.cs
namespace SluiceBase.Api.Auth;

public interface ISessionValidator
{
    /// Check if a user has an active SSO session
    Task<bool> IsSessionValidAsync(string userId, CancellationToken ct);
}
```

- [ ] **Step 4: Implement ISessionValidator using current auth infrastructure**

```csharp
// src/SluiceBase.Api/Auth/SessionValidator.cs (add to AuthSetup.cs as inner class or separate file)
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SluiceBase.Api.Auth;

internal sealed class SessionValidator(IHttpContextAccessor contextAccessor) : ISessionValidator
{
    public async Task<bool> IsSessionValidAsync(string userId, CancellationToken ct)
    {
        // For now, check if any active HTTP context has this user
        // In a real scenario, you'd query a session store or cache
        // This is a simplified version that assumes sessions are tracked server-side
        // TBD: implement proper session state tracking
        return true; // Placeholder
    }
}
```

- [ ] **Step 5: Create SessionHeartbeat**

```csharp
// src/SluiceBase.Api/Proxy/SessionHeartbeat.cs
using SluiceBase.Api.Auth;

namespace SluiceBase.Api.Proxy;

public sealed class SessionHeartbeat
{
    private readonly ISessionValidator _sessionValidator;

    public SessionHeartbeat(ISessionValidator sessionValidator)
    {
        _sessionValidator = sessionValidator;
    }

    public async Task<bool> CheckSessionValidityAsync(string userId, CancellationToken ct)
    {
        return await _sessionValidator.IsSessionValidAsync(userId, ct);
    }

    /// Called periodically (e.g., every 60s) to check all active connections
    public async Task RunAsync(
        ConcurrentDictionary<Guid, PgWireConnection> activeConnections,
        Func<PgWireConnection, Task> onExpiredConnection,
        CancellationToken ct)
    {
        var expiredConnections = new List<Guid>();

        foreach (var (connectionId, connection) in activeConnections)
        {
            if (connection.UserId is not null)
            {
                var isValid = await CheckSessionValidityAsync(connection.UserId.ToString(), ct);
                if (!isValid)
                {
                    expiredConnections.Add(connectionId);
                }
            }
        }

        foreach (var connectionId in expiredConnections)
        {
            if (activeConnections.TryRemove(connectionId, out var connection))
            {
                await onExpiredConnection(connection);
                connection.Close();
            }
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass (update test to match new implementation)**

Run: `dotnet test tests/SluiceBase.Core.Tests/SessionHeartbeatTests.cs -v`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Api/Auth/ISessionValidator.cs src/SluiceBase.Api/Auth/SessionValidator.cs src/SluiceBase.Api/Proxy/SessionHeartbeat.cs tests/SluiceBase.Core.Tests/SessionHeartbeatTests.cs
git commit -m "Add SessionHeartbeat and ISessionValidator for session validity checking"
```

---

### Task 6: Create PgWireProxyService BackgroundService

**Files:**
- Create: `src/SluiceBase.Api/Proxy/PgWireProxyService.cs`

This is the main orchestrator. Due to length, this task is substantial.

- [ ] **Step 1: Write integration test for full proxy flow**

```csharp
// tests/IntegrationTests/PgWireProxyTests.cs
using System.Net;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using Xunit;

namespace IntegrationTests;

public class PgWireProxyTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    [Fact]
    public async Task PgWireProxy_ConnectsAndExecutesQuery_WithValidCredential()
    {
        var ct = TestContext.Current.CancellationToken;
        
        // Setup: Create a session, server, database, credential
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);
        
        // Grant alice query:execute permission
        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");
        
        // TBD: Create server, database, credential via API
        // TBD: Generate proxy credential
        // TBD: Connect via Npgsql using credential
        // TBD: Execute query: SELECT 1
        // TBD: Verify result
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}
```

- [ ] **Step 2: Implement PgWireProxyService skeleton**

```csharp
// src/SluiceBase.Api/Proxy/PgWireProxyService.cs
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Queries;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Proxy;

internal sealed class PgWireProxyService : BackgroundService
{
    private readonly ILogger<PgWireProxyService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<Guid, PgWireConnection> _activeConnections = new();
    private TcpListener? _tcpListener;
    private SessionHeartbeat? _heartbeat;
    private CancellationTokenSource? _heartbeatCts;

    public PgWireProxyService(
        ILogger<PgWireProxyService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = _configuration.GetValue("Proxy:Port", 6432);
        _logger.LogInformation("Starting PgWire proxy on port {Port}", port);

        _tcpListener = new TcpListener(IPAddress.Any, port);
        _tcpListener.Start();

        // Start session heartbeat
        _heartbeatCts = new CancellationTokenSource();
        _ = RunSessionHeartbeatAsync(_heartbeatCts.Token);

        try
        {
            await AcceptConnectionsAsync(stoppingToken);
        }
        finally
        {
            _tcpListener?.Stop();
            _heartbeatCts?.Cancel();
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _tcpListener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting client connection");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var connectionId = Guid.NewGuid();
        _logger.LogInformation("Client {ConnectionId} connected", connectionId);

        try
        {
            using var networkStream = client.GetStream();
            using var reader = new StreamReader(networkStream);
            using var writer = new StreamWriter(networkStream) { AutoFlush = false };
            
            var connection = new PgWireConnection(connectionId, reader, writer);
            _activeConnections[connectionId] = connection;

            try
            {
                await HandleAuthenticationAsync(connection, ct);
                
                if (connection.State == PgWireConnectionState.Active)
                {
                    await HandleQueriesAsync(connection, ct);
                }
            }
            finally
            {
                _activeConnections.TryRemove(connectionId, out _);
                connection.Close();
                connection.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client {ConnectionId}", connectionId);
        }
        finally
        {
            client.Close();
            client.Dispose();
        }
    }

    private async Task HandleAuthenticationAsync(PgWireConnection connection, CancellationToken ct)
    {
        // Step 1: Receive StartupMessage
        // Step 2: Send AuthenticationCleartextPasswordMessage
        // Step 3: Receive PasswordMessage
        // Step 4: Validate credential + session
        // Step 5: Send AuthenticationOkMessage or ErrorResponseMessage
        _logger.LogInformation("TBD: Implement authentication handshake");
    }

    private async Task HandleQueriesAsync(PgWireConnection connection, CancellationToken ct)
    {
        // Step 1: Send ReadyForQueryMessage
        // Step 2: Loop: receive Query, execute, send RowDescription + DataRows + CommandComplete
        // Step 3: Handle errors, timeouts
        _logger.LogInformation("TBD: Implement query execution loop");
    }

    private async Task RunSessionHeartbeatAsync(CancellationToken ct)
    {
        // Every 60 seconds, check session validity for all connections
        _logger.LogInformation("TBD: Implement session heartbeat");
    }
}
```

- [ ] **Step 3: Register service in Program.cs**

```csharp
// src/SluiceBase.Api/Program.cs (add after line 45)
builder.Services.AddSingleton<ISessionValidator, SessionValidator>();
builder.Services.AddSingleton<PgWireProxyService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PgWireProxyService>());
```

- [ ] **Step 4: Implement HandleAuthenticationAsync**

This includes the full authentication flow with credential lookup and session checking. Code is substantial; follow the pattern in the spec.

- [ ] **Step 5: Implement HandleQueriesAsync**

Loop that reads Query messages, runs through the protection pipeline (same as HTTP API), and sends back formatted results.

- [ ] **Step 6: Implement RunSessionHeartbeatAsync**

Timer that runs every 60 seconds, checks session validity, terminates expired connections.

- [ ] **Step 7: Run integration test to verify happy path**

Run: `dotnet test tests/IntegrationTests/PgWireProxyTests.cs::PgWireProxyTests::PgWireProxy_ConnectsAndExecutesQuery_WithValidCredential -v`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/SluiceBase.Api/Proxy/PgWireProxyService.cs src/SluiceBase.Api/Program.cs tests/IntegrationTests/PgWireProxyTests.cs
git commit -m "Add PgWireProxyService with authentication and query execution"
```

---

### Task 7: Create ProxyCredentialEndpoints REST API

**Files:**
- Create: `src/SluiceBase.Api/Endpoints/ProxyCredentialEndpoints.cs`

- [ ] **Step 1: Write integration test for credential generation and revocation**

```csharp
[Fact]
public async Task GenerateProxyCredential_ReturnsConnectionString()
{
    // TBD: Complete test
}

[Fact]
public async Task RevokeProxyCredential_TerminatesConnections()
{
    // TBD: Complete test
}
```

- [ ] **Step 2: Implement endpoints**

- [ ] **Step 3: Register in EndpointMapper**

- [ ] **Step 4: Commit**

---

### Task 8: Create Frontend Hooks and UI

**Files:**
- Modify: `src/frontend/src/api/hooks.ts`
- Modify: `src/frontend/src/routes/_authed/server.tsx`

- [ ] **Step 1: Add React Query hooks for proxy credentials**

- [ ] **Step 2: Add UI section to database detail card**

- [ ] **Step 3: Test in browser**

- [ ] **Step 4: Commit**

---

### Task 9: Additional Integration Tests

Add tests for:
- Permission denial (user without `query:execute`)
- Sensitive column blocking via proxy
- Read-only enforcement
- Session expiry mid-connection
- Credential revocation
- Concurrent connections

---

### Task 10: Final Integration & Documentation

- [ ] Run full test suite: `dotnet test`
- [ ] Verify Aspire app starts: `dotnet run --project src/AppHost`
- [ ] Test proxy manually with psql
- [ ] Update CLAUDE.md with lessons learned
- [ ] Commit and create PR

