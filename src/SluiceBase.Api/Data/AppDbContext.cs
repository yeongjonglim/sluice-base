using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SluiceBase.Api.Data.Converters;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPermissionMap> UserPermissions => Set<UserPermissionMap>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<QueryLog> QueryLogs => Set<QueryLog>();

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
    }
}