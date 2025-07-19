using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Ledger__MVC.Models;
using Ledger__MVC.Data;

namespace Ledger__MVC.Controllers
{
    public class DemoController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<DemoController> _logger;

        public DemoController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILogger<DemoController> logger)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> LoginDemo()
        {
            try
            {
                const string demoEmail = "demo@daftar.com";
                const string demoPassword = "Demo123!";

                _logger.LogInformation("بدء عملية تسجيل دخول الديمو");

                // البحث عن المستخدم الديمو أو إنشاؤه
                var demoUser = await _userManager.FindByEmailAsync(demoEmail);
                if (demoUser == null)
                {
                    _logger.LogInformation("إنشاء مستخدم ديمو جديد");
                    await CreateDemoUser(demoEmail, demoPassword);
                    demoUser = await _userManager.FindByEmailAsync(demoEmail);
                }

                if (demoUser == null)
                {
                    _logger.LogError("فشل في إنشاء أو العثور على مستخدم الديمو");
                    TempData["Error"] = "فشل في إنشاء حساب الديمو";
                    return RedirectToAction("Index", "Home");
                }

                _logger.LogInformation("إعادة تعيين بيانات الديمو");
                // إعادة تعيين بيانات الديمو
                await ResetDemoData(demoUser.Id);

                _logger.LogInformation("تسجيل خروج المستخدم الحالي");
                // تسجيل الدخول
                await _signInManager.SignOutAsync();
                
                _logger.LogInformation("محاولة تسجيل دخول الديمو");
                var result = await _signInManager.PasswordSignInAsync(demoEmail, demoPassword, false, false);

                if (result.Succeeded)
                {
                    _logger.LogInformation("نجح تسجيل دخول الديمو");
                    TempData["Success"] = "مرحباً بك في النسخة التجريبية! البيانات سيتم إعادة تعيينها كل 10 دقائق";
                    return RedirectToAction("Summary", "Transaction");
                }

                _logger.LogWarning("فشل تسجيل دخول الديمو: {Result}", result);
                TempData["Error"] = "فشل في تسجيل الدخول للنسخة التجريبية";
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ في تسجيل دخول الديمو: {Message}", ex.Message);
                TempData["Error"] = $"حدث خطأ: {ex.Message}";
                return RedirectToAction("Index", "Home");
            }
        }

        private async Task CreateDemoUser(string email, string password)
        {
            try
            {
                _logger.LogInformation("إنشاء مستخدم ديمو: {Email}", email);
                
                var demoUser = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = "مستخدم تجريبي",
                    PhoneNumber = "01000000000",
                    SubscriptionType = SubscriptionType.Monthly,
                    SubscriptionStartDate = DateTime.Now,
                    SubscriptionEndDate = DateTime.Now.AddYears(10),
                    Role = UserRole.User,
                    IsActive = true,
                    EmailConfirmed = true,
                    LastDemoReset = DateTime.Now
                };

                var result = await _userManager.CreateAsync(demoUser, password);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogError("فشل في إنشاء حساب الديمو: {Errors}", errors);
                    throw new Exception($"فشل في إنشاء حساب الديمو: {errors}");
                }
                
                _logger.LogInformation("تم إنشاء مستخدم الديمو بنجاح");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ في CreateDemoUser");
                throw;
            }
        }

        private async Task ResetDemoData(string userId)
        {
            // حذف البيانات القديمة
            var existingClients = await _context.Clients
                .Where(c => c.ApplicationUserId == userId)
                .ToListAsync();
            _context.Clients.RemoveRange(existingClients);

            var existingTransactions = await _context.FinancialTransactions
                .Where(t => t.ApplicationUserId == userId)
                .ToListAsync();
            _context.FinancialTransactions.RemoveRange(existingTransactions);

            // إضافة بيانات تجريبية
            var demoClients = new List<Client>
            {
                new Client { Name = "أحمد محمد", PhoneNumber = "01012345678", Email = "ahmed@example.com", ApplicationUserId = userId },
                new Client { Name = "فاطمة علي", PhoneNumber = "01098765432", Email = "fatma@example.com", ApplicationUserId = userId },
                new Client { Name = "محمود حسن", PhoneNumber = "01055555555", Email = "mahmoud@example.com", ApplicationUserId = userId }
            };

            _context.Clients.AddRange(demoClients);
            await _context.SaveChangesAsync();

            // إضافة معاملات تجريبية
            var demoTransactions = new List<FinancialTransaction>
            {
                new FinancialTransaction
                {
                    ClientId = demoClients[0].Id,
                    Amount = 1500,
                    Type = TransactionType.Debt,
                    Notes = "بيع بضاعة",
                    Date = DateTime.Now.AddDays(-5),
                    ApplicationUserId = userId
                },
                new FinancialTransaction
                {
                    ClientId = demoClients[0].Id,
                    Amount = 500,
                    Type = TransactionType.Payment,
                    Notes = "دفعة جزئية",
                    Date = DateTime.Now.AddDays(-2),
                    ApplicationUserId = userId
                },
                new FinancialTransaction
                {
                    ClientId = demoClients[1].Id,
                    Amount = 2000,
                    Type = TransactionType.Debt,
                    Notes = "خدمات استشارية",
                    Date = DateTime.Now.AddDays(-3),
                    ApplicationUserId = userId
                }
            };

            _context.FinancialTransactions.AddRange(demoTransactions);

            // تحديث وقت آخر إعادة تعيين
            var user = await _context.ApplicationUsers.FindAsync(userId);
            if (user != null)
            {
                user.LastDemoReset = DateTime.Now;
            }

            await _context.SaveChangesAsync();
        }
    }
}




