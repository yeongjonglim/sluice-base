using SluiceBase.Core.Permissions;

namespace SluiceBase.Core.Users;

public sealed class User
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private User()
    {
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    private User(UserId id, string? email, string? name, DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        Name = name;
        CreatedAt = createdAt;
    }

    public UserId Id { get; private set; }
    public string? Email { get; private set; }
    public string? Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IList<UserPermissionMap> Permissions { get; init; } = [];

    public static User Create(string? email, string? name, DateTimeOffset at) =>
        new(UserId.FromNewVersion7Guid(), email, name, at);

    public bool HasPermission(string permission) =>
        Permissions.Any(p => p.Permission == permission);
}

public sealed class ExternalLogin
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private ExternalLogin()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    {
    }

    public ExternalLoginId Id { get; private set; }
    public UserId UserId { get; private set; }
    public string Issuer { get; private set; }
    public string Subject { get; private set; }
    public string? Email { get; private set; }
    public string? Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public List<ClaimRecord>? Claims { get; private set; }

    // Loaded by EF
    public User User { get; private set; } = null!;

    public static ExternalLogin Create(
        UserId userId,
        string issuer,
        string subject,
        string? email,
        string? name,
        DateTimeOffset at,
        IEnumerable<ClaimRecord>? claims)
    {
        return new ExternalLogin
        {
            Id = ExternalLoginId.FromNewVersion7Guid(),
            UserId = userId,
            Issuer = issuer,
            Subject = subject,
            Email = email,
            Name = name,
            CreatedAt = at,
            LastLoginAt = at,
            Claims = claims?.ToList(),
        };
    }

    public void RecordLogin(DateTimeOffset at, string? email, string? name, IEnumerable<ClaimRecord>? claims)
    {
        Email = email;
        Name = name;
        Claims = claims?.ToList();
        LastLoginAt = at;
    }
}