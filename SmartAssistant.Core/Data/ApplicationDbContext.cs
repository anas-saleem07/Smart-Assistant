using Microsoft.EntityFrameworkCore;
using SmartAssistant.Core.Entities;

namespace SmartAssistant.Api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public DbSet<Reminder> Reminder { get; set; } = default!;
        public DbSet<ReminderAutomationSettings> ReminderAutomationSettings { get; set; } = default!;
        public DbSet<EmailProcessed> EmailProcessed { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EmailProcessed>()
                .HasIndex(x => new { x.Provider, x.MessageId })
                .IsUnique();

            // seed settings row (optional)
            modelBuilder.Entity<ReminderAutomationSettings>()
                .HasData(new ReminderAutomationSettings { Id = 1 });
        }
    }
}