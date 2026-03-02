using Microsoft.EntityFrameworkCore;
using SmartAssistant.Core.Entities;

namespace SmartAssistant.Api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Reminder> Reminder { get; set; } = default!;
        public DbSet<ReminderAutomationSettings> ReminderAutomationSettings { get; set; } = default!;
        public DbSet<EmailProcessed> EmailProcessed { get; set; } = default!;
        public DbSet<EmailOAuthAccount> EmailOAuthAccounts { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EmailProcessed>()
                .HasIndex(x => new { x.Provider, x.MessageId })
                .IsUnique();

            // NEW: dedupe only for Email reminders (manual has null source)
            modelBuilder.Entity<Reminder>()
                .HasIndex(r => new { r.Type, r.SourceProvider, r.SourceId })
                .IsUnique();

            modelBuilder.Entity<ReminderAutomationSettings>()
                .HasData(new ReminderAutomationSettings { Id = 1 });
        }
    }
}