using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Ledger__MVC.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required, StringLength(100)]
        public string FullName { get; set; }



        [Required, Phone]
        public string PhoneNumber { get; set; }



        [Required]
        public SubscriptionType SubscriptionType { get; set; }

        [Required]
        public DateTime SubscriptionStartDate { get; set; }

        [Required]
        public DateTime SubscriptionEndDate { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? LastSeen { get; set; }
        public UserRole Role { get; set; } = UserRole.User;
        public ICollection<Client> Clients { get; set; } = new List<Client>();
        public ICollection<FinancialTransaction> FinancialTransactions { get; set; } = new List<FinancialTransaction>();
        public ICollection<SubscriptionHistory> SubscriptionHistories { get; set; } = new List<SubscriptionHistory>();
        public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    }

}
