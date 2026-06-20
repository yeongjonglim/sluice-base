// src/SluiceBase.Api/Data/AppDbContext.cs
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SluiceBase.Api.Data.Converters;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Mcp;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Updates;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<UserPermissionMap> UserPermissions => Set<UserPermissionMap>();
    public DbSet<UserDatabaseRole> UserDatabaseRoles => Set<UserDatabaseRole>();
    public DbSet<AccessGroup> AccessGroups => Set<AccessGroup>();
    public DbSet<AccessGroupMember> AccessGroupMembers => Set<AccessGroupMember>();
    public DbSet<AccessGroupPermission> AccessGroupPermissions => Set<AccessGroupPermission>();
    public DbSet<AccessGroupDatabaseRole> AccessGroupDatabaseRoles => Set<AccessGroupDatabaseRole>();
    public DbSet<SensitiveColumn> SensitiveColumns => Set<SensitiveColumn>();
    public DbSet<UserColumnBypass> UserColumnBypasses => Set<UserColumnBypass>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<Database> Databases => Set<Database>();
    public DbSet<QueryLog> QueryLogs => Set<QueryLog>();
    public DbSet<UpdateRequest> UpdateRequests => Set<UpdateRequest>();
    public DbSet<McpOAuthClient> McpOAuthClients => Set<McpOAuthClient>();
    public DbSet<McpAuthCode> McpAuthCodes => Set<McpAuthCode>();
    public DbSet<McpToken> McpTokens => Set<McpToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Conventions.Remove<TableNameFromDbSetConvention>();
        configurationBuilder.RegisterAllInVogenEfCoreConverters();
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }
}