using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SluiceBase.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}