using System.ComponentModel.DataAnnotations;

namespace Ledger__MVC.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        public string? ApplicationUserId { get; set; }
        public ApplicationUser? ApplicationUser { get; set; }

        [Required]
        public string Action { get; set; } // مثل: "تجديد اشتراك"

        [Required]
        public string Details { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

}
