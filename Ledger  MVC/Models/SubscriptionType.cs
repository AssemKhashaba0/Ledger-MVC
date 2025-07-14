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
        [Display(Name = "عليّا (دين)")]
        Debt = 0,     // أنت مديون للعميل

        [Display(Name = "ليّا (قبض)")]
        Payment = 1   // قبضت من العميل
    }

    public enum UserRole
    {
        Admin,
        User
    }
}
