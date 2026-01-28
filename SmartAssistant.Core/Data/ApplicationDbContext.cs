using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Models;
using SmartAssistant.Core.Entities;
using SmartAssistant.Core.Entities;
namespace SmartAssistant.Api.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }
    public DbSet<Reminder> Reminder => Set<Reminder>();

    protected override void OnModelCreating (ModelBuilder modelBuilder)
    {
        base.OnModelCreating (modelBuilder);
        //Future : Api Config for relationships
    }
}