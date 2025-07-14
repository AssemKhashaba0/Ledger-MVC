using System.ComponentModel.DataAnnotations;

namespace Ledger__MVC.Models
{
    public class FinancialTransaction
    {
        public int Id { get; set; }

        [Required]
        public TransactionType Type { get; set; }

        [Required, Range(0.01, double.MaxValue)]
        public decimal Amount { get; set; }

        [Required]
        public DateTime Date { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        [Required]
        public int ClientId { get; set; }
        public Client Client { get; set; }

        [Required]
        public string ApplicationUserId { get; set; }
        public ApplicationUser ApplicationUser { get; set; }
    }
}
