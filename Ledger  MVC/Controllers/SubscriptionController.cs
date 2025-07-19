using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Identity.UI.Services;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Ledger__MVC.Data;
using Ledger__MVC.Models;

namespace Ledger__MVC.Controllers
{
    public class SubscriptionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<SubscriptionController> _logger;

        public SubscriptionController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            ILogger<SubscriptionController> logger)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        // ViewModel for Index
        public class SubscriptionIndexViewModel
        {
            public List<UserListViewModel> Users { get; set; }
            public List<SubscriptionTypeViewModel> SubscriptionTypes { get; set; }
            public string CurrentSearch { get; set; }
            public string CurrentIsActive { get; set; }
            public string CurrentSubscriptionType { get; set; }
        }

        // ViewModel for User List
        public class UserListViewModel
        {
            public string Id { get; set; }
            public string FullName { get; set; }
            public string PhoneNumber { get; set; }
            public SubscriptionType SubscriptionType { get; set; }
            public DateTime SubscriptionEndDate { get; set; }
            public string Status { get; set; }
            public int ClientCount { get; set; }
            public int TransactionCount { get; set; }
            public DateTime LastSeen { get; set; }
            public int RemainingDays { get; set; }
        }

        // ViewModel for Subscription Types
        public class SubscriptionTypeViewModel
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        // ViewModel for Update Subscription
        public class UpdateSubscriptionViewModel
        {
            [Required(ErrorMessage = "نوع الاشتراك مطلوب")]
            public SubscriptionType SubscriptionType { get; set; }

            [Required(ErrorMessage = "تاريخ البداية مطلوب")]
            [DataType(DataType.Date)]
            public DateTime SubscriptionStartDate { get; set; }

            [Required(ErrorMessage = "السعر مطلوب")]
            [Range(0, double.MaxValue, ErrorMessage = "السعر يجب أن يكون أكبر من أو يساوي 0")]
            public decimal Price { get; set; }

            public List<SubscriptionTypeViewModel> SubscriptionTypes { get; set; }
            public string UserId { get; set; }
            public string FullName { get; set; }
        }

        // ViewModel for Renew Subscription
        public class RenewSubscriptionViewModel
        {
            [Required(ErrorMessage = "نوع الاشتراك مطلوب")]
            public SubscriptionType SubscriptionType { get; set; }

            [Required(ErrorMessage = "تاريخ البداية مطلوب")]
            [DataType(DataType.Date)]
            public DateTime SubscriptionStartDate { get; set; }

            [Required(ErrorMessage = "السعر مطلوب")]
            [Range(0, double.MaxValue, ErrorMessage = "السعر يجب أن يكون أكبر من أو يساوي 0")]
            public decimal Price { get; set; }

            public string PaymentNote { get; set; }
            public List<SubscriptionTypeViewModel> SubscriptionTypes { get; set; }
            public string UserId { get; set; }
            public string FullName { get; set; }
        }

        // GET: /Subscription/Index
        [HttpGet]
        public async Task<IActionResult> Index(string search = null, bool? isActive = null, SubscriptionType? subscriptionType = null)
        {
            try
            {
                var query = _context.ApplicationUsers
                    .Where(u => u.Role == UserRole.User)
                    .Select(u => new UserListViewModel
                    {
                        Id = u.Id,
                        FullName = u.FullName,
                        PhoneNumber = u.PhoneNumber,
                        SubscriptionType = u.SubscriptionType,
                        SubscriptionEndDate = u.SubscriptionEndDate,
                        Status = !u.IsActive ? "محظور" :
                                 u.SubscriptionEndDate >= DateTime.Now ? "نشط" : "منتهي",
                        ClientCount = u.Clients.Count,
                        TransactionCount = u.FinancialTransactions.Count,
                        LastSeen = u.LastSeen ?? DateTime.MinValue,
                        RemainingDays = !u.IsActive ? 0 :
                                        u.SubscriptionEndDate >= DateTime.Now ?
                                        (u.SubscriptionEndDate - DateTime.Now).Days : 0
                    });

                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(u => u.FullName.Contains(search) || u.PhoneNumber.Contains(search));
                }

                if (isActive.HasValue)
                {
                    query = query.Where(u => isActive.Value ? u.Status == "نشط" : u.Status == "منتهي");
                }

                if (subscriptionType.HasValue)
                {
                    query = query.Where(u => u.SubscriptionType == subscriptionType.Value);
                }

                var model = new SubscriptionIndexViewModel
                {
                    Users = await query.ToListAsync(),
                    SubscriptionTypes = Enum.GetValues(typeof(SubscriptionType))
                        .Cast<SubscriptionType>()
                        .Select(e => new SubscriptionTypeViewModel
                        {
                            Id = (int)e,
                            Name = e.GetDisplayName()
                        })
                        .ToList(),
                    CurrentSearch = search,
                    CurrentIsActive = isActive?.ToString(),
                    CurrentSubscriptionType = subscriptionType?.ToString()
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Index action: {Message}", ex.Message);
                TempData["Error"] = $"حدث خطأ غير متوقع: {ex.Message}";
                return View(new SubscriptionIndexViewModel
                {
                    Users = new List<UserListViewModel>(),
                    SubscriptionTypes = Enum.GetValues(typeof(SubscriptionType))
                        .Cast<SubscriptionType>()
                        .Select(e => new SubscriptionTypeViewModel
                        {
                            Id = (int)e,
                            Name = e.GetDisplayName()
                        })
                        .ToList(),
                    CurrentSearch = search,
                    CurrentIsActive = isActive?.ToString(),
                    CurrentSubscriptionType = subscriptionType?.ToString()
                });
            }
        }

        // GET: /Subscription/UpdatePlan/{userId}
        [HttpGet]
        public async Task<IActionResult> UpdatePlan(string userId)
        {
            var user = await _context.ApplicationUsers
                .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);

            if (user == null)
            {
                TempData["Error"] = "المستخدم غير موجود";
                return RedirectToAction(nameof(Index));
            }

            var model = new UpdateSubscriptionViewModel
            {
                SubscriptionType = user.SubscriptionType,
                SubscriptionStartDate = DateTime.Now,
                Price = 0,
                SubscriptionTypes = Enum.GetValues(typeof(SubscriptionType))
                    .Cast<SubscriptionType>()
                    .Select(e => new SubscriptionTypeViewModel
                    {
                        Id = (int)e,
                        Name = e.GetDisplayName()
                    })
                    .ToList(),
                UserId = userId,
                FullName = user.FullName
            };

            return View(model);
        }

        // POST: /Subscription/UpdatePlan/{userId}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePlan(string userId, UpdateSubscriptionViewModel model)
        {
            model.SubscriptionTypes = Enum.GetValues(typeof(SubscriptionType))
                .Cast<SubscriptionType>()
                .Select(e => new SubscriptionTypeViewModel
                {
                    Id = (int)e,
                    Name = e.GetDisplayName()
                })
                .ToList();
            model.UserId = userId;

            if (ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var user = await _context.ApplicationUsers
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);

                if (user == null)
                {
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction(nameof(Index));
                }

                model.FullName = user.FullName;

                if (!user.IsActive)
                {
                    TempData["Error"] = "لا يمكن تعديل حساب محظور";
                    return RedirectToAction(nameof(Index));
                }

                if (model.SubscriptionType == 0)
                {
                    ModelState.AddModelError("SubscriptionType", "الرجاء اختيار خطة اشتراك");
                    return View(model);
                }

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
                    PaymentNote = "تعديل خطة الاشتراك بواسطة الأدمن"
                };
                _context.SubscriptionHistories.Add(subscriptionHistory);

                var auditLog = new AuditLog
                {
                    ApplicationUserId = _userManager.GetUserId(User),
                    Action = "تعديل اشتراك",
                    Details = $"تم تعديل اشتراك المستخدم {user.Email} إلى {model.SubscriptionType.GetDisplayName()}",
                    Timestamp = DateTime.Now
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync();

                try
                {
                    string emailBody = $@"<h3>مرحبًا {user.FullName}</h3>
                        <p>تم تعديل اشتراكك في نظام Smart Ledger.</p>
                        <p>تفاصيل الاشتراك الجديد:</p>
                        <ul>
                            <li>نوع الاشتراك: {model.SubscriptionType.GetDisplayName()}</li>
                            <li>تاريخ البداية: {model.SubscriptionStartDate:yyyy-MM-dd}</li>
                            <li>تاريخ الانتهاء: {user.SubscriptionEndDate:yyyy-MM-dd}</li>
                        </ul>";

                    await _emailSender.SendEmailAsync(user.Email, "تعديل اشتراكك في Smart Ledger", emailBody);
                }
                catch
                {
                    // تجاهل فشل إرسال البريد
                }

                TempData["Success"] = "تم تعديل الاشتراك بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ: {ex.Message}";
                return View(model);
            }
        }

        // GET: /Subscription/Renew/{userId}
        [HttpGet]
        public async Task<IActionResult> Renew(string userId)
        {
            var user = await _context.ApplicationUsers
                .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);

            if (user == null)
            {
                TempData["Error"] = "المستخدم غير موجود";
                return RedirectToAction(nameof(Index));
            }

            var model = new RenewSubscriptionViewModel
            {
                SubscriptionType = user.SubscriptionType,
                SubscriptionStartDate = DateTime.Now,
                Price = 0,
                PaymentNote = "تجديد يدوي بواسطة الأدمن",
                SubscriptionTypes = Enum.GetValues(typeof(SubscriptionType))
                    .Cast<SubscriptionType>()
                    .Select(e => new SubscriptionTypeViewModel
                    {
                        Id = (int)e,
                        Name = e.GetDisplayName()
                    })
                    .ToList(),
                UserId = userId,
                FullName = user.FullName
            };

            return View(model);
        }

        // POST: /Subscription/Renew/{userId}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Renew(string userId, RenewSubscriptionViewModel model)
        {
            model.SubscriptionTypes = Enum.GetValues(typeof(SubscriptionType))
                .Cast<SubscriptionType>()
                .Select(e => new SubscriptionTypeViewModel
                {
                    Id = (int)e,
                    Name = e.GetDisplayName()
                })
                .ToList();
            model.UserId = userId;

            if (ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var user = await _context.ApplicationUsers
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);

                if (user == null)
                {
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction(nameof(Index));
                }

                model.FullName = user.FullName;

                if (model.SubscriptionType == 0)
                {
                    ModelState.AddModelError("SubscriptionType", "الرجاء اختيار خطة اشتراك");
                    return View(model);
                }

                var subscriptionEndDate = CalculateSubscriptionEndDate(model.SubscriptionType, model.SubscriptionStartDate);
                if (subscriptionEndDate <= model.SubscriptionStartDate)
                {
                    ModelState.AddModelError("SubscriptionStartDate", "تاريخ البداية يجب أن ينتج عنه تاريخ انتهاء صالح");
                    return View(model);
                }

                user.SubscriptionType = model.SubscriptionType;
                user.SubscriptionStartDate = model.SubscriptionStartDate;
                user.SubscriptionEndDate = subscriptionEndDate;
                user.IsActive = true;

                var subscriptionHistory = new SubscriptionHistory
                {
                    ApplicationUserId = user.Id,
                    SubscriptionType = model.SubscriptionType,
                    StartDate = model.SubscriptionStartDate,
                    EndDate = subscriptionEndDate,
                    Price = model.Price,
                    PaymentNote = model.PaymentNote ?? "تجديد يدوي بواسطة الأدمن"
                };
                _context.SubscriptionHistories.Add(subscriptionHistory);

                var auditLog = new AuditLog
                {
                    ApplicationUserId = _userManager.GetUserId(User),
                    Action = "تجديد اشتراك",
                    Details = $"تم تجديد اشتراك المستخدم {user.Email} إلى {model.SubscriptionType.GetDisplayName()}",
                    Timestamp = DateTime.Now
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync();

                try
                {
                    string emailBody = $@"<h3>مرحبًا {user.FullName}</h3>
                        <p>تم تجديد اشتراكك في نظام Smart Ledger.</p>
                        <p>تفاصيل الاشتراك الجديد:</p>
                        <ul>
                            <li>نوع الاشتراك: {model.SubscriptionType.GetDisplayName()}</li>
                            <li>تاريخ البداية: {model.SubscriptionStartDate:yyyy-MM-dd}</li>
                            <li>تاريخ الانتهاء: {subscriptionEndDate:yyyy-MM-dd}</li>
                        </ul>";

                    await _emailSender.SendEmailAsync(user.Email, "تجديد اشتراكك في Smart Ledger", emailBody);
                }
                catch
                {
                    // تجاهل فشل إرسال البريد
                }

                TempData["Success"] = "تم تجديد الاشتراك بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ: {ex.Message}";
                return View(model);
            }
        }

        // POST: /Subscription/BlockConfirmed/{userId}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BlockConfirmed(string userId)
        {
            try
            {
                var user = await _context.ApplicationUsers
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);

                if (user == null)
                {
                    return Json(new { success = false, message = "المستخدم غير موجود" });
                }

                if (!user.IsActive)
                {
                    return Json(new { success = false, message = "الحساب محظور بالفعل" });
                }

                user.IsActive = false;

                var auditLog = new AuditLog
                {
                    ApplicationUserId = _userManager.GetUserId(User),
                    Action = "حظر حساب",
                    Details = $"تم حظر حساب المستخدم {user.Email} بشكل دائم",
                    Timestamp = DateTime.Now
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "تم حظر الحساب بشكل دائم بنجاح" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"حدث خطأ: {ex.Message}" });
            }
        }

        // POST: /Subscription/UnblockConfirmed/{userId}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnblockConfirmed(string userId)
        {
            try
            {
                var user = await _context.ApplicationUsers
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);

                if (user == null)
                {
                    return Json(new { success = false, message = "المستخدم غير موجود" });
                }

                if (user.IsActive)
                {
                    return Json(new { success = false, message = "الحساب نشط بالفعل" });
                }

                user.IsActive = true;

                var auditLog = new AuditLog
                {
                    ApplicationUserId = _userManager.GetUserId(User),
                    Action = "إلغاء حظر حساب",
                    Details = $"تم إلغاء حظر حساب المستخدم {user.Email}",
                    Timestamp = DateTime.Now
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync();

                try
                {
                    string emailBody = $@"<h3>مرحبًا {user.FullName}</h3>
                        <p>تم إلغاء حظر حسابك في نظام Smart Ledger.</p>
                        <p>يمكنك الآن تسجيل الدخول باستخدام بياناتك.</p>";

                    await _emailSender.SendEmailAsync(user.Email, "إلغاء حظر حسابك في Smart Ledger", emailBody);
                }
                catch
                {
                    // تجاهل فشل إرسال البريد
                }

                return Json(new { success = true, message = "تم إلغاء حظر الحساب بنجاح" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"حدث خطأ: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Revenues()
        {
            try
            {
                var today = DateTime.Today;
                var startOfMonth = new DateTime(today.Year, today.Month, 1);
                var startOfYear = new DateTime(today.Year, 1, 1);

                var model = new RevenueViewModel
                {
                    DailyRevenue = await _context.SubscriptionHistories
                        .Where(s => s.StartDate.Date == today)
                        .SumAsync(s => s.Price),
                    MonthlyRevenue = await _context.SubscriptionHistories
                        .Where(s => s.StartDate >= startOfMonth && s.StartDate <= today)
                        .SumAsync(s => s.Price),
                    YearlyRevenue = await _context.SubscriptionHistories
                        .Where(s => s.StartDate >= startOfYear && s.StartDate <= today)
                        .SumAsync(s => s.Price),
                    RevenueDetails = await _context.SubscriptionHistories
                        .Include(s => s.ApplicationUser)
                        .Where(s => s.StartDate >= startOfYear)
                        .Select(s => new RevenueDetailViewModel
                        {
                            UserName = s.ApplicationUser.FullName,
                            SubscriptionType = s.SubscriptionType.GetDisplayName(),
                            Price = s.Price,
                            StartDate = s.StartDate
                        })
                        .ToListAsync()
                };

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ أثناء استرجاع الإيرادات: {ex.Message}";
                return View(new RevenueViewModel());
            }
        }

        public class RevenueViewModel
        {
            public decimal DailyRevenue { get; set; }
            public decimal MonthlyRevenue { get; set; }
            public decimal YearlyRevenue { get; set; }
            public List<RevenueDetailViewModel> RevenueDetails { get; set; }
        }

        public class RevenueDetailViewModel
        {
            public string UserName { get; set; }
            public string SubscriptionType { get; set; }
            public decimal Price { get; set; }
            public DateTime StartDate { get; set; }
        }

        // POST: /Subscription/DeleteConfirmed/{userId}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string userId)
        {
            try
            {
                var user = await _context.ApplicationUsers
                    .Include(u => u.FinancialTransactions)
                    .Include(u => u.Clients)
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);

                if (user == null)
                {
                    return Json(new { success = false, message = "المستخدم غير موجود" });
                }

                if (user.FinancialTransactions.Any() || user.Clients.Any())
                {
                    return Json(new { success = false, message = "لا يمكن حذف الحساب لوجود معاملات أو عملاء مرتبطين" });
                }

                _context.ApplicationUsers.Remove(user);

                var auditLog = new AuditLog
                {
                    ApplicationUserId = _userManager.GetUserId(User),
                    Action = "حذف حساب",
                    Details = $"تم حذف حساب المستخدم {user.Email}",
                    Timestamp = DateTime.Now
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "تم حذف الحساب بنجاح" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"حدث خطأ: {ex.Message}" });
            }
        }

        // GET: /Subscription/Types
        [HttpGet]
        public IActionResult GetSubscriptionTypes()
        {
            var types = Enum.GetValues(typeof(SubscriptionType))
                .Cast<SubscriptionType>()
                .Select(e => new
                {
                    Id = (int)e,
                    Name = e.GetDisplayName()
                })
                .ToList();

            return Json(new { Data = types });
        }

        // Helper function to calculate subscription end date
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

    }

    // Helper class for enum display names
    public static class EnumExtensions
    {
        public static string GetDisplayName(this Enum enumValue)
        {
            var displayAttribute = enumValue.GetType()
                .GetMember(enumValue.ToString())
                .First()
                .GetCustomAttribute<DisplayAttribute>();
            return displayAttribute?.Name ?? enumValue.ToString();
        }
    }
}