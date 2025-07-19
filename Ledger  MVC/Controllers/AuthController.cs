using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Ledger__MVC.Data;
using Ledger__MVC.Models;
using System.ComponentModel.DataAnnotations;

namespace Ledger__MVC.Controllers
{
    public class AuthController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            IConfiguration configuration)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
            _configuration = configuration;
        }

        // نموذج إدخال بيانات تسجيل الدخول
        public class LoginViewModel
        {
            [Required(ErrorMessage = "يرجى إدخال البريد الإلكتروني")]
            [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صحيح")]
            public string Email { get; set; }

            [Required(ErrorMessage = "يرجى إدخال كلمة السر")]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            [Display(Name = "تذكرني")]
            public bool RememberMe { get; set; }

        }

        // GET: /Auth/Login
        [HttpGet]
        public IActionResult Login(string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null)
                {
                    await LogFailedAttempt(null);
                    ModelState.AddModelError(string.Empty, "بيانات الدخول غير صحيحة");
                    return View(model);
                }

                // التحقق من قفل الحساب
                if (await IsAccountLocked(user.Id))
                {
                    ModelState.AddModelError(string.Empty, "الحساب مغلق مؤقتًا بسبب محاولات دخول متكررة فاشلة، حاول مجددًا بعد 15 دقيقة");
                    return View(model);
                }

                // تسجيل الدخول باستخدام Identity
                var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    // تحديث LastSeen
                    user.LastSeen = DateTime.Now;
                    await _context.SaveChangesAsync();

                    // التحقق من حالة الاشتراك
                    if (user.SubscriptionEndDate < DateTime.Now || !user.IsActive)
                    {
                        await _signInManager.SignOutAsync();
                        
                        // تسجيل عملية تسجيل الخروج التلقائي
                        var logoutAuditLog = new AuditLog
                        {
                            ApplicationUserId = user.Id,
                            Action = "تسجيل خروج تلقائي",
                            Details = $"تم تسجيل خروج المستخدم {user.Email} تلقائ<lemma> بسبب انتهاء الاشتراك",
                            Timestamp = DateTime.Now
                        };
                        _context.AuditLogs.Add(logoutAuditLog);
                        await _context.SaveChangesAsync();
                        
                        TempData["Error"] = "انتهى اشتراكك، الرجاء التجديد";
                        return RedirectToAction("Renewal");
                    }

                    // تسجيل عملية الدخول الناجحة
                    var auditLog = new AuditLog
                    {
                        ApplicationUserId = user.Id,
                        Action = "تسجيل دخول",
                        Details = $"المستخدم {user.Email} قام بتسجيل الدخول بنجاح",
                        Timestamp = DateTime.Now
                    };
                    _context.AuditLogs.Add(auditLog);
                    await _context.SaveChangesAsync();

                    // التحقق مما إذا كان المستخدم أدمن
                    if (await _userManager.IsInRoleAsync(user, "Admin"))
                    {
                        return RedirectToAction("Dashboard", "User"); // إعادة توجيه الأدمن إلى /User/Dashboard
                    }else
                    {
                        return RedirectToAction("Summary", "Transaction"); // إعادة توجيه المستخدم العادي إلى /Transaction/Summary
                    }

                }

                await LogFailedAttempt(user.Id);
                ModelState.AddModelError(string.Empty, "بيانات الدخول غير صحيحة");
                return View(model);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"حدث خطأ غير متوقع: {ex.Message}");
                return View(model);
            }
        }
        // GET: /Auth/Renewal
        [HttpGet]
        public IActionResult Renewal()
        {
            ViewData["VodafoneCashNumber"] = "000000000000"; // يمكن تخزينه في الإعدادات
            ViewData["PaymentInstructionsImage"] = "https://example.com/payment-instructions.jpg";
            return View();
        }

        // GET: /Auth/ShowUserSubscription
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> ShowUserSubscription()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound(new { Message = "المستخدم غير موجود" });
            }

            var daysRemaining = (user.SubscriptionEndDate - DateTime.Now).Days;
            if (daysRemaining < 0) daysRemaining = 0;

            var model = new
            {
                SubscriptionType = user.SubscriptionType.ToString(),
                SubscriptionEndDate = user.SubscriptionEndDate.ToString("yyyy-MM-dd"),
                DaysRemaining = daysRemaining
            };

            return View(model);
        }

        // نموذج تحديث كلمة المرور
        public class UpdatePasswordViewModel
        {
            [Required(ErrorMessage = "يرجى إدخال كلمة السر الحالية")]
            [DataType(DataType.Password)]
            public string CurrentPassword { get; set; }

            [Required(ErrorMessage = "يرجى إدخال كلمة السر الجديدة")]
            [DataType(DataType.Password)]
            public string NewPassword { get; set; }

            [DataType(DataType.Password)]
            [Compare("NewPassword", ErrorMessage = "كلمة السر الجديدة وتأكيدها غير متطابقتين")]
            public string ConfirmPassword { get; set; }
        }

        // GET: /Auth/UpdatePassword
        [HttpGet]
        [Authorize]
        public IActionResult UpdatePassword()
        {
            return View();
        }

        // POST: /Auth/UpdatePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> UpdatePassword(UpdatePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound(new { Message = "المستخدم غير موجود" });
            }

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (result.Succeeded)
            {
                return RedirectToAction("ShowUserSubscription", new { Message = "تم تحديث كلمة السر بنجاح" });
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        // دالة لتسجيل محاولات الدخول الفاشلة
        private async Task LogFailedAttempt(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return;

            var auditLog = new AuditLog
            {
                ApplicationUserId = userId,
                Action = "محاولة دخول فاشلة",
                Details = $"محاولة تسجيل دخول فاشلة للمستخدم بمعرف {userId}",
                Timestamp = DateTime.Now
            };
            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();
        }

        // دالة للتحقق من قفل الحساب
        private async Task<bool> IsAccountLocked(string userId)
        {
            var recentFailedAttempts = await _context.AuditLogs
                .Where(log => log.ApplicationUserId == userId && log.Action == "محاولة دخول فاشلة")
                .Where(log => log.Timestamp >= DateTime.Now.AddMinutes(-15))
                .CountAsync();

            return recentFailedAttempts >= 5;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    // تسجيل عملية تسجيل الخروج
                    var auditLog = new AuditLog
                    {
                        ApplicationUserId = user.Id,
                        Action = "تسجيل خروج",
                        Details = $"المستخدم {user.Email} قام بتسجيل الخروج",
                        Timestamp = DateTime.Now
                    };
                    _context.AuditLogs.Add(auditLog);
                    await _context.SaveChangesAsync();
                }

                await _signInManager.SignOutAsync();
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ أثناء تسجيل الخروج: {ex.Message}";
                return RedirectToAction("Index", "Home");
            }
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
