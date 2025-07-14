using Ledger__MVC.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ledger__MVC.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        public DbSet<ApplicationUser> ApplicationUsers { get; set; }
        public DbSet<Client> Clients { get; set; }
        public DbSet<FinancialTransaction> FinancialTransactions { get; set; }
        public DbSet<SubscriptionHistory> SubscriptionHistories { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Notification> Notifications { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ApplicationUser - Client (One to Many)
            builder.Entity<Client>()
                .HasOne(c => c.ApplicationUser)
                .WithMany(u => u.Clients)
                .HasForeignKey(c => c.ApplicationUserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ApplicationUser - FinancialTransaction (One to Many)
            builder.Entity<FinancialTransaction>()
                .HasOne(t => t.ApplicationUser)
                .WithMany(u => u.FinancialTransactions)
                .HasForeignKey(t => t.ApplicationUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Client - FinancialTransaction (One to Many)
            builder.Entity<FinancialTransaction>()
                .HasOne(t => t.Client)
                .WithMany(c => c.Transactions)
                .HasForeignKey(t => t.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            // ApplicationUser - SubscriptionHistory (One to Many)
            builder.Entity<SubscriptionHistory>()
                .HasOne(h => h.ApplicationUser)
                .WithMany(u => u.SubscriptionHistories)
                .HasForeignKey(h => h.ApplicationUserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ApplicationUser - AuditLog (One to Many) —> Can be null
            builder.Entity<AuditLog>()
                .HasOne(a => a.ApplicationUser)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(a => a.ApplicationUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // ApplicationUser - Notification (One to Many) —> Can be null
            builder.Entity<Notification>()
                .HasOne(n => n.ApplicationUser)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.ApplicationUserId)
                .OnDelete(DeleteBehavior.SetNull);


        }
    }
}
