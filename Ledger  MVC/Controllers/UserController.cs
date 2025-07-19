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
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                Console.WriteLine("ModelState Errors: " + string.Join(", ", errors));
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
                        Console.WriteLine($"Identity Error: {error.Description}");
                    }
                    PopulateSubscriptionTypes();
                    return View(model);
                }

                // Verify the user exists in _context
                var savedUser = await _context.ApplicationUsers
                    .FirstOrDefaultAsync(u => u.Email == model.Email);
                if (savedUser == null)
                {
                    Console.WriteLine("Failed to find user in _context. Email: " + model.Email);
                    TempData["Error"] = "فشل في العثور على المستخدم في قاعدة البيانات. الرجاء المحاولة مرة أخرى.";
                    PopulateSubscriptionTypes();
                    return View(model);
                }
                Console.WriteLine($"Found user with ID: {savedUser.Id}");

                // Add subscription history
                try
                {
                    var subscriptionHistory = new SubscriptionHistory
                    {
                        ApplicationUserId = savedUser.Id,
                        SubscriptionType = model.SubscriptionType,
                        StartDate = model.SubscriptionStartDate,
                        EndDate = user.SubscriptionEndDate,
                        Price = model.Price,
                        PaymentNote = "دفع يدوي تم تأكيده بواسطة الأدمن"
                    };
                    _context.SubscriptionHistories.Add(subscriptionHistory);
                    await _context.SaveChangesAsync();
                    Console.WriteLine("SubscriptionHistory saved successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save SubscriptionHistory: {ex.Message}, InnerException: {ex.InnerException?.Message}");
                    throw; // Rethrow to catch in outer try-catch
                }

                // Log audit
                //var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                //if (string.IsNullOrEmpty(adminId))
                //{
                //    Console.WriteLine("Admin user is not authenticated.");
                //    TempData["Error"] = "لا يمكن تسجيل العملية لأن المسؤول غير مسجل الدخول.";
                //    PopulateSubscriptionTypes();
                //    return View(model);
                //}

                //try
                //{
                //    var auditLog = new AuditLog
                //    {
                //        ApplicationUserId = adminId,
                //        Action = "إنشاء حساب مستخدم جديد",
                //        Details = $"تم إنشاء حساب للمستخدم {model.Email} مع اشتراك {GetEnumDisplayName(model.SubscriptionType)}",
                //        Timestamp = DateTime.Now
                //    };
                //    _context.AuditLogs.Add(auditLog);
                //    await _context.SaveChangesAsync();
                //    Console.WriteLine("AuditLog saved successfully.");
                //}
                //catch (Exception ex)
                //{
                //    Console.WriteLine($"Failed to save AuditLog: {ex.Message}, InnerException: {ex.InnerException?.Message}");
                //    throw; // Rethrow to catch in outer try-catch
                //}

                // Send email
                try
                {
                    string emailBody = $@"
    <!DOCTYPE html>
    <html lang='ar' dir='rtl'>
    <head>
        <meta charset='UTF-8'>
        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
        <title>بيانات الدخول إلى ASVS</title>
        <style>
            body {{ font-family: 'Arial', sans-serif; line-height: 1.6; color: #1e293b; margin: 0; padding: 0; background-color: #f8fafc; }}
            .container {{ max-width: 600px; margin: 20px auto; background-color: #ffffff; border-radius: 8px; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1); border: 1px solid #e2e8f0; }}
            .header {{ background-color: #6366f1; padding: 20px; text-align: center; color: #ffffff; }}
            .header h1 {{ margin: 0; font-size: 20px; }}
            .content {{ padding: 20px; }}
            .greeting {{ font-size: 16px; font-weight: bold; margin-bottom: 15px; color: #1e293b; }}
            .message {{ margin-bottom: 15px; font-size: 14px; }}
            .login-box {{ background-color: #f1f5f9; border-radius: 8px; padding: 15px; margin: 15px 0; border-right: 4px solid #6366f1; }}
            .login-row {{ margin-bottom: 10px; }}
            .login-label {{ font-weight: bold; color: #64748b; font-size: 14px; }}
            .login-value {{ color: #1e293b; font-size: 14px; }}
            .password-highlight {{ background-color: #a5b4fc; padding: 2px 8px; border-radius: 4px; border: 1px solid #4f46e5; font-weight: bold; }}
            .footer {{ background-color: #f1f5f9; padding: 15px; text-align: center; border-top: 1px solid #e2e8f0; font-size: 12px; color: #94a3b8; }}
            .footer-logo {{ font-weight: bold; color: #6366f1; margin-bottom: 5px; }}
        </style>
    </head>
    <body>
        <div class='container'>
            <div class='header'>
                <h1>مرحبًا بك في ASVS</h1>
            </div>
            <div class='content'>
                <div class='greeting'>مرحبًا {model.FullName}،</div>
                <div class='message'>
                    <p>تم إنشاء حسابك بنجاح في <span style='color: #6366f1; font-weight: bold;'>ASVS</span>.</p>
                    <p>بيانات الدخول الخاصة بك:</p>
                </div>
                <div class='login-box'>
                    <div class='login-row'>
                        <span class='login-label'>البريد الإلكتروني:</span>
                        <span class='login-value'>{model.Email}</span>
                    </div>
                    <div class='login-row'>
                        <span class='login-label'>كلمة السر المؤقتة:</span>
                        <span class='login-value password-highlight'>{temporaryPassword}</span>
                    </div>
                </div>
                <div class='message'>
                    <p>يرجى تغيير كلمة السر بعد تسجيل الدخول الأول.</p>
                </div>
            </div>
            <div class='footer'>
                <img src='~/IMG/ASVS Logo.png' alt='ASVS Logo' style='max-width: 120px; height: auto; margin-bottom: 5px;'>
                <div class='footer-logo'>ASVS</div>
                <p>© {DateTime.Now.Year} ASVS. جميع الحقوق محفوظة.</p>
            </div>
        </div>
    </body>
    </html>";

                    await _emailSender.SendEmailAsync(model.Email, "بيانات الدخول إلى ASVS", emailBody);
                    TempData["Success"] = "تم إنشاء حساب المستخدم بنجاح وإرسال بيانات الدخول إلى البريد الإلكتروني.";
                }
                catch (Exception emailEx)
                {
                    TempData["Success"] = $"تم إنشاء الح حساب بنجاح، لكن فشل إرسال البريد الإلكتروني إلى {model.Email}. كلمة السر المؤقتة: <strong>{temporaryPassword}</strong>. الرجاء إبلاغ المستخدم بكلمة السر يدويًا.";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during SaveChanges: {ex.Message}, InnerException: {ex.InnerException?.Message}");
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
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            try
            {
                var today = DateTime.Today;
                var startOfMonth = new DateTime(today.Year, today.Month, 1);

                var model = new DashboardViewModel
                {
                    ActiveUsers = await _context.ApplicationUsers
                        .CountAsync(u => u.Role == UserRole.User && u.IsActive && u.SubscriptionEndDate >= DateTime.Now),
                    MonthlyRevenue = await _context.SubscriptionHistories
                        .Where(s => s.StartDate >= startOfMonth && s.StartDate <= today)
                        .SumAsync(s => s.Price),
                    RecentUsers = await _context.ApplicationUsers
                        .Where(u => u.Role == UserRole.User)
                        .OrderByDescending(u => u.SubscriptionStartDate)
                        .Take(5)
                        .Select(u => new UserViewModel
                        {
                            Id = u.Id,
                            FullName = u.FullName,
                            SubscriptionType = u.SubscriptionType,
                            SubscriptionEndDate = u.SubscriptionEndDate,
                            IsActive = u.IsActive
                        })
                        .ToListAsync(),
                    MonthlyRevenues = await _context.SubscriptionHistories
                        .Where(s => s.StartDate.Year == today.Year)
                        .GroupBy(s => s.StartDate.Month)
                        .Select(g => new MonthlyRevenueViewModel
                        {
                            Month = g.Key,
                            Revenue = g.Sum(s => s.Price)
                        })
                        .ToListAsync()
                };

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ أثناء استرجاع بيانات الداشبورد: {ex.Message}";
                return View(new DashboardViewModel());
            }
        }

        public class DashboardViewModel
        {
            public int ActiveUsers { get; set; }
            public decimal MonthlyRevenue { get; set; }
            public List<UserViewModel> RecentUsers { get; set; }
            public List<MonthlyRevenueViewModel> MonthlyRevenues { get; set; }
        }

        public class MonthlyRevenueViewModel
        {
            public int Month { get; set; }
            public decimal Revenue { get; set; }
        }
      
        public class OverviewViewModel
        {
            public int TotalUsers { get; set; }
            public int ActiveUsers { get; set; }
            public int BlockedUsers { get; set; }
            public int ExpiredSubscriptions { get; set; }
            public int TotalClients { get; set; }
            public int TotalTransactions { get; set; }
            public List<SubscriptionDistributionViewModel> SubscriptionDistribution { get; set; }
            public List<UserViewModel> Users { get; set; }
        }

        public class SubscriptionDistributionViewModel
        {
            public string SubscriptionType { get; set; }
            public int Count { get; set; }
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
        public async Task<IActionResult> Overview(int page = 1)
        {
            try
            {
                var model = new OverviewViewModel
                {
                    TotalUsers = await _context.ApplicationUsers.CountAsync(u => u.Role == UserRole.User),
                    ActiveUsers = await _context.ApplicationUsers.CountAsync(u => u.Role == UserRole.User && u.IsActive && u.SubscriptionEndDate >= DateTime.Now),
                    BlockedUsers = await _context.ApplicationUsers.CountAsync(u => u.Role == UserRole.User && !u.IsActive),
                    ExpiredSubscriptions = await _context.ApplicationUsers.CountAsync(u => u.Role == UserRole.User && u.SubscriptionEndDate < DateTime.Now),
                    TotalClients = await _context.Clients.CountAsync(),
                    TotalTransactions = await _context.FinancialTransactions.CountAsync(),
                    SubscriptionDistribution = await _context.ApplicationUsers
                        .Where(u => u.Role == UserRole.User)
                        .GroupBy(u => u.SubscriptionType)
                        .Select(g => new SubscriptionDistributionViewModel
                        {
                            SubscriptionType = g.Key.GetDisplayName(),
                            Count = g.Count()
                        })
                        .ToListAsync(),
                    Users = await _context.ApplicationUsers
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
                            ClientsCount = u.Clients.Count,
                            TransactionsCount = u.FinancialTransactions.Count,
                            LastLogin = u.LastSeen ?? DateTime.MinValue
                        })
                        .OrderBy(u => u.FullName)
                        .ToListAsync()
                };

                ViewData["PageNumber"] = page;
                return View(model);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ أثناء استرجاع النظرة العامة: {ex.Message}";
                return View(new OverviewViewModel());
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