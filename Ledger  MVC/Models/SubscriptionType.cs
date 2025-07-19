using System.ComponentModel.DataAnnotations;

namespace Ledger__MVC.Models
{
    public enum SubscriptionType
    {
        [Display(Name = "شهري")]
        Monthly = 1,

        [Display(Name = "ربع سنوي")]
        Quarterly = 3,

        [Display(Name = "نصف سنوي")]
        SemiAnnual = 6,

        [Display(Name = "سنوي")]
        Annual = 12
    }

    public enum TransactionType
    {
        [Display(Name = "ليك (مديون)")]
        Debt = 0,     // العميل مديون ليك

        [Display(Name = "عليك (دفعة)")]
        Payment = 1   // دفعت للعميل
    }

    public enum UserRole
    {
        Admin,
        User
    }
}
