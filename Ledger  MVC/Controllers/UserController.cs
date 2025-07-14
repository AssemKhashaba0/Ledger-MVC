using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using System.Security.Claims;
using Ledger__MVC.Data;
using Ledger__MVC.Models;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Ledger__MVC.Controllers
{
    //[Authorize(Roles = nameof(UserRole.Admin))]
    public class UserController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserController(ApplicationDbContext context, IEmailSender emailSender, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _emailSender = emailSender;
            _userManager = userManager;
        }

        public class UserViewModel
        {
            public string Id { get; set; }
            public string FullName { get; set; }
            public string Email { get; set; }
            public string PhoneNumber { get; set; }
            public SubscriptionType SubscriptionType { get; set; }
            public string SubscriptionTypeDisplayName => GetEnumDisplayName(SubscriptionType);
            public DateTime SubscriptionEndDate { get; set; }
            public bool IsActive { get; set; }
            public int ClientsCount { get; set; }
            public int TransactionsCount { get; set; }
            public DateTime LastLogin { get; set; }
        }

        public class CreateUpdateUserViewModel
        {
            [Required(ErrorMessage = "الاسم الكامل مطلوب")]
            [Display(Name = "الاسم الكامل")]
            public string FullName { get; set; }

            [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
            [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صالح")]
            [Display(Name = "البريد الإلكتروني")]
            public string Email { get; set; }

            [Required(ErrorMessage = "رقم الهاتف مطلوب")]
            [Phone(ErrorMessage = "رقم الهاتف غير صالح")]
            [Display(Name = "رقم الهاتف")]
            public string PhoneNumber { get; set; }

            [Required(ErrorMessage = "نوع الاشتراك مطلوب")]
            [Display(Name = "نوع الاشتراك")]
            public SubscriptionType SubscriptionType { get; set; }

            [Required(ErrorMessage = "سعر الاشتراك مطلوب")]
            [Range(0.01, double.MaxValue, ErrorMessage = "السعر يجب أن يكون أكبر من 0")]
            [Display(Name = "السعر")]
            public decimal Price { get; set; }

            [Required(ErrorMessage = "تاريخ بدء الاشتراك مطلوب")]
            [DataType(DataType.Date)]
            [DisplayFormat(DataFormatString = "{0:yyyy-MM-dd}", ApplyFormatInEditMode = true)]
            [Display(Name = "تاريخ بدء الاشتراك")]
            public DateTime SubscriptionStartDate { get; set; }
        }

        private static string GetEnumDisplayName(SubscriptionType type)
        {
            var fieldInfo = type.GetType().GetField(type.ToString());
            var displayAttribute = fieldInfo?.GetCustomAttribute<DisplayAttribute>();
            return displayAttribute?.Name ?? type.ToString();
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                var users = await _context.ApplicationUsers
                    .Where(u => u.Role == UserRole.User)
                    .Select(u => new UserViewModel
                    {
                        Id = u.Id,
                        FullName = u.FullName,
                        Email = u.Email,
                        PhoneNumber = u.PhoneNumber,
                        SubscriptionType = u.SubscriptionType,
                        SubscriptionEndDate = u.SubscriptionEndDate,
                        IsActive = u.IsActive,
                        ClientsCount = _context.Clients.Count(c => c.ApplicationUserId == u.Id),
                        TransactionsCount = _context.FinancialTransactions.Count(t => t.ApplicationUserId == u.Id),
                        LastLogin = u.LastSeen ?? DateTime.MinValue
                    })
                    .OrderByDescending(u => u.LastLogin)
                    .ToListAsync();

                return View(users);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "حدث خطأ غير متوقع أثناء استرجاع قائمة المستخدمين. الرجاء المحاولة لاحقًا.";
                return View(new List<UserViewModel>());
            }
        }

        [HttpGet]
        public IActionResult Create()
        {
            PopulateSubscriptionTypes();
            return View(new CreateUpdateUserViewModel { SubscriptionStartDate = DateTime.Now });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateUpdateUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                PopulateSubscriptionTypes();
                return View(model);
            }

            try
            {
                // Check for duplicate email
                if (await _userManager.FindByEmailAsync(model.Email) != null)
                {
                    ModelState.AddModelError("Email", "البريد الإلكتروني مستخدم بالفعل، الرجاء استخدام بريد آخر.");
                    PopulateSubscriptionTypes();
                    return View(model);
                }

                // Validate subscription type
                if (model.SubscriptionType == 0)
                {
                    ModelState.AddModelError("SubscriptionType", "الرجاء اختيار خطة اشتراك أولاً.");
                    PopulateSubscriptionTypes();
                    return View(model);
                }

                // Create new user using UserManager
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FullName = model.FullName,
                    PhoneNumber = model.PhoneNumber,
                    SubscriptionType = model.SubscriptionType,
                    SubscriptionStartDate = model.SubscriptionStartDate,
                    SubscriptionEndDate = CalculateSubscriptionEndDate(model.SubscriptionType, model.SubscriptionStartDate),
                    Role = UserRole.User,
                    IsActive = true,
                    EmailConfirmed = true
                };

                // Generate temporary password
                var temporaryPassword = GenerateTemporaryPassword();
                var result = await _userManager.CreateAsync(user, temporaryPassword);
                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                    PopulateSubscriptionTypes();
                    return View(model);
                }

                // Add subscription history
                var subscriptionHistory = new SubscriptionHistory
                {
                    ApplicationUserId = user.Id,
                    SubscriptionType = model.SubscriptionType,
                    StartDate = model.SubscriptionStartDate,
                    EndDate = user.SubscriptionEndDate,
                    Price = model.Price,
                    PaymentNote = "دفع يدوي تم تأكيده بواسطة الأدمن"
                };
                _context.SubscriptionHistories.Add(subscriptionHistory);

                // Log audit
                var auditLog = new AuditLog
                {
                    ApplicationUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    Action = "إنشاء حساب مستخدم جديد",
                    Details = $"تم إنشاء حساب للمستخدم {model.Email} مع اشتراك {GetEnumDisplayName(model.SubscriptionType)}",
                    Timestamp = DateTime.Now
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync();

                // Send email
                try
                {
                    string emailBody = $@"<h3>مرحبًا {model.FullName},</h3>
                    <p>تم إنشاء حسابك بنجاح في نظام Smart Ledger.</p>
                    <p>بيانات الدخول الخاصة بك:</p>
                    <ul>
                        <li><strong>البريد الإلكتروني:</strong> {model.Email}</li>
                        <li><strong>كلمة السر المؤقتة:</strong> {temporaryPassword}</li>
                    </ul>
                    <p>يرجى تغيير كلمة السر بعد تسجيل الدخول الأول لأسباب أمنية.</p>
                    <p>شكرًا لك!</p>";
                    await _emailSender.SendEmailAsync(model.Email, "بيانات الدخول إلى Smart Ledger", emailBody);
                    TempData["Success"] = "تم إنشاء حساب المستخدم بنجاح وإرسال بيانات الدخول إلى البريد الإلكتروني.";
                }
                catch (Exception emailEx)
                {
                    TempData["Success"] = $"تم إنشاء الحساب بنجاح، لكن فشل إرسال البريد الإلكتروني إلى {model.Email}. كلمة السر المؤقتة: <strong>{temporaryPassword}</strong>. الرجاء إبلاغ المستخدم بكلمة السر يدويًا.";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ غير متوقع أثناء إنشاء المستخدم: {ex.InnerException?.Message ?? ex.Message}";
                PopulateSubscriptionTypes();
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Update(string id)
        {
            var user = await _context.ApplicationUsers.FindAsync(id);
            if (user == null)
            {
                TempData["Error"] = "المستخدم غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            var model = new CreateUpdateUserViewModel
            {
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                SubscriptionType = user.SubscriptionType,
                SubscriptionStartDate = user.SubscriptionStartDate,
                Price = _context.SubscriptionHistories
                    .Where(s => s.ApplicationUserId == user.Id && s.SubscriptionType == user.SubscriptionType)
                    .OrderByDescending(s => s.StartDate)
                    .Select(s => s.Price)
                    .FirstOrDefault()
            };

            PopulateSubscriptionTypes();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(string id, CreateUpdateUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                PopulateSubscriptionTypes();
                return View(model);
            }

            try
            {
                var user = await _context.ApplicationUsers.FindAsync(id);
                if (user == null)
                {
                    TempData["Error"] = "المستخدم غير موجود.";
                    return RedirectToAction(nameof(Index));
                }

                if (await _context.ApplicationUsers.AnyAsync(u => u.Email == model.Email && u.Id != id))
                {
                    ModelState.AddModelError("Email", "البريد الإلكتروني مستخدم بالفعل، الرجاء استخدام بريد آخر.");
                    PopulateSubscriptionTypes();
                    return View(model);
                }

                bool subscriptionChanged = user.SubscriptionType != model.SubscriptionType || user.SubscriptionStartDate != model.SubscriptionStartDate;

                user.FullName = model.FullName;
                user.PhoneNumber = model.PhoneNumber;
                user.Email = model.Email;
                user.UserName = model.Email;

                if (subscriptionChanged)
                {
                    user.SubscriptionType = model.SubscriptionType;
                    user.SubscriptionStartDate = model.SubscriptionStartDate;
                    user.SubscriptionEndDate = CalculateSubscriptionEndDate(model.SubscriptionType, model.SubscriptionStartDate);

                    var subscriptionHistory = new SubscriptionHistory
                    {
                        ApplicationUserId = user.Id,
                        SubscriptionType = model.SubscriptionType,
                        StartDate = model.SubscriptionStartDate,
                        EndDate = user.SubscriptionEndDate,
                        Price = model.Price,
                        PaymentNote = "تعديل يدوي تم تأكيده بواسطة الأدمن"
                    };
                    _context.SubscriptionHistories.Add(subscriptionHistory);
                }

                var auditLog = new AuditLog
                {
                    ApplicationUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    Action = "تعديل حساب مستخدم",
                    Details = $"تم تعديل بيانات المستخدم {model.Email}. تم تغيير الاشتراك: {subscriptionChanged}",
                    Timestamp = DateTime.Now
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync();
                TempData["Success"] = "تم تعديل بيانات المستخدم بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ غير متوقع أثناء تعديل المستخدم: {ex.InnerException?.Message ?? ex.Message}";
                PopulateSubscriptionTypes();
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Block(string id)
        {
            try
            {
                var user = await _context.ApplicationUsers.FindAsync(id);
                if (user == null)
                {
                    TempData["Error"] = "المستخدم غير موجود.";
                    return RedirectToAction(nameof(Index));
                }

                user.IsActive = !user.IsActive;

                var auditLog = new AuditLog
                {
                    ApplicationUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    Action = user.IsActive ? "تفعيل مستخدم" : "حظر مستخدم",
                    Details = $"تم {(user.IsActive ? "تفعيل" : "حظر")} المستخدم {user.Email}",
                    Timestamp = DateTime.Now
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync();
                TempData["Success"] = user.IsActive ? "تم تفعيل المستخدم بنجاح." : "تم حظر المستخدم بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ غير متوقع أثناء تغيير حالة المستخدم: {ex.InnerException?.Message ?? ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            try
            {
                var user = await _context.ApplicationUsers
                    .Include(u => u.Clients)
                    .Include(u => u.FinancialTransactions)
                    .Include(u => u.SubscriptionHistories)
                    .FirstOrDefaultAsync(u => u.Id == id);

                if (user == null)
                {
                    TempData["Error"] = "المستخدم غير موجود.";
                    return RedirectToAction(nameof(Index));
                }

                _context.Clients.RemoveRange(user.Clients);
                _context.FinancialTransactions.RemoveRange(user.FinancialTransactions);
                _context.SubscriptionHistories.RemoveRange(user.SubscriptionHistories);
                _context.ApplicationUsers.Remove(user);

                var auditLog = new AuditLog
                {
                    ApplicationUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    Action = "حذف مستخدم",
                    Details = $"تم حذف المستخدم {user.Email}",
                    Timestamp = DateTime.Now
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف المستخدم بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ غير متوقع أثناء حذف المستخدم: {ex.InnerException?.Message ?? ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            var user = await _context.ApplicationUsers
                .Where(u => u.Id == id && u.Role == UserRole.User)
                .Select(u => new UserViewModel
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    SubscriptionType = u.SubscriptionType,
                    SubscriptionEndDate = u.SubscriptionEndDate,
                    IsActive = u.IsActive,
                    ClientsCount = _context.Clients.Count(c => c.ApplicationUserId == u.Id),
                    TransactionsCount = _context.FinancialTransactions.Count(t => t.ApplicationUserId == u.Id),
                    LastLogin = u.LastSeen ?? DateTime.MinValue
                })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                TempData["Error"] = "المستخدم غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            return View(user);
        }

        private DateTime CalculateSubscriptionEndDate(SubscriptionType subscriptionType, DateTime startDate)
        {
            return subscriptionType switch
            {
                SubscriptionType.Monthly => startDate.AddMonths(1),
                SubscriptionType.Quarterly => startDate.AddMonths(3),
                SubscriptionType.SemiAnnual => startDate.AddMonths(6),
                SubscriptionType.Annual => startDate.AddYears(1),
                _ => startDate.AddMonths(1)
            };
        }

        private string GenerateTemporaryPassword()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_+-=[]{}|;:,.<>?";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 12)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private void PopulateSubscriptionTypes()
        {
            ViewData["SubscriptionTypes"] = Enum.GetValues(typeof(SubscriptionType))
                .Cast<SubscriptionType>()
                .Select(t => new { Id = (int)t, Name = GetEnumDisplayName(t) })
                .ToList();
        }
    }
}