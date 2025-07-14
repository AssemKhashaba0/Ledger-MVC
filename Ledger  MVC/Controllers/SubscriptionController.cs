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
    [Authorize(Roles = nameof(UserRole.Admin))]
    public class SubscriptionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;

        public SubscriptionController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        // نموذج عرض قائمة المستخدمين
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
        }

        // نموذج تعديل الاشتراك
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
        }

        // نموذج تجديد الاشتراك
        public class RenewSubscriptionViewModel
        {
            [Required(ErrorMessage = "نوع الاشتراك مطلوب")]
            public SubscriptionType SubscriptionType { get; set; }

            [Required(ErrorMessage = "تاريخ البداية مطلوب")]
            [DataType(DataType.Date)]
            public DateTime SubscriptionStartDate { get; set; }

            [Required(ErrorMessage = "تاريخ الانتهاء مطلوب")]
            [DataType(DataType.Date)]
            public DateTime SubscriptionEndDate { get; set; }

            [Required(ErrorMessage = "السعر مطلوب")]
            [Range(0, double.MaxValue, ErrorMessage = "السعر يجب أن يكون أكبر من أو يساوي 0")]
            public decimal Price { get; set; }

            public string PaymentNote { get; set; }
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
                        Status = u.IsActive && u.SubscriptionEndDate >= DateTime.Now ? "نشط" : "منتهي",
                        ClientCount = u.Clients.Count,
                        TransactionCount = u.FinancialTransactions.Count
                        //LastSeen = u.LastSeen,
                    });

                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(u => u.FullName.Contains(search) || u.PhoneNumber.Contains(search));
                }

                if (isActive.HasValue)
                {
                    query = query.Where(u => u.Status == (isActive.Value ? "نشط" : "منتهي"));
                }

                if (subscriptionType.HasValue)
                {
                    query = query.Where(u => u.SubscriptionType == subscriptionType.Value);
                }

                var users = await query.ToListAsync();
                ViewData["SubscriptionTypes"] = Enum.GetValues(typeof(SubscriptionType))
                    .Cast<SubscriptionType>()
                    .Select(e => new { Id = (int)e, Name = e.GetDisplayName() })
                    .ToList();

                return View(users);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ غير متوقع: {ex.Message}";
                return View(new List<UserListViewModel>());
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
                Price = 0
            };

            ViewData["SubscriptionTypes"] = Enum.GetValues(typeof(SubscriptionType))
                .Cast<SubscriptionType>()
                .Select(e => new { Id = (int)e, Name = e.GetDisplayName() })
                .ToList();

            ViewData["UserId"] = userId;
            ViewData["FullName"] = user.FullName;
            return View(model);
        }

        // POST: /Subscription/UpdatePlan/{userId}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePlan(string userId, UpdateSubscriptionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewData["SubscriptionTypes"] = Enum.GetValues(typeof(SubscriptionType))
                    .Cast<SubscriptionType>()
                    .Select(e => new { Id = (int)e, Name = e.GetDisplayName() })
                    .ToList();
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

                if (!user.IsActive)
                {
                    TempData["Error"] = "لا يمكن تعديل حساب محظور";
                    return RedirectToAction(nameof(Index));
                }

                if (model.SubscriptionType == 0)
                {
                    ModelState.AddModelError("SubscriptionType", "الرجاء اختيار خطة اشتراك");
                    ViewData["SubscriptionTypes"] = Enum.GetValues(typeof(SubscriptionType))
                        .Cast<SubscriptionType>()
                        .Select(e => new { Id = (int)e, Name = e.GetDisplayName() })
                        .ToList();
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
                ViewData["SubscriptionTypes"] = Enum.GetValues(typeof(SubscriptionType))
                    .Cast<SubscriptionType>()
                    .Select(e => new { Id = (int)e, Name = e.GetDisplayName() })
                    .ToList();
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
                SubscriptionEndDate = CalculateSubscriptionEndDate(user.SubscriptionType, DateTime.Now),
                Price = 0,
                PaymentNote = "تجديد يدوي بواسطة الأدمن"
            };

            ViewData["SubscriptionTypes"] = Enum.GetValues(typeof(SubscriptionType))
                .Cast<SubscriptionType>()
                .Select(e => new { Id = (int)e, Name = e.GetDisplayName() })
                .ToList();

            ViewData["UserId"] = userId;
            ViewData["FullName"] = user.FullName;
            return View(model);
        }

        // POST: /Subscription/Renew/{userId}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Renew(string userId, RenewSubscriptionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewData["SubscriptionTypes"] = Enum.GetValues(typeof(SubscriptionType))
                    .Cast<SubscriptionType>()
                    .Select(e => new { Id = (int)e, Name = e.GetDisplayName() })
                    .ToList();
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

                if (model.SubscriptionType == 0)
                {
                    ModelState.AddModelError("SubscriptionType", "الرجاء اختيار خطة اشتراك");
                    ViewData["SubscriptionTypes"] = Enum.GetValues(typeof(SubscriptionType))
                        .Cast<SubscriptionType>()
                        .Select(e => new { Id = (int)e, Name = e.GetDisplayName() })
                        .ToList();
                    return View(model);
                }

                if (model.SubscriptionEndDate <= model.SubscriptionStartDate)
                {
                    ModelState.AddModelError("SubscriptionEndDate", "الرجاء تحديد تاريخ انتهاء صالح");
                    ViewData["SubscriptionTypes"] = Enum.GetValues(typeof(SubscriptionType))
                        .Cast<SubscriptionType>()
                        .Select(e => new { Id = (int)e, Name = e.GetDisplayName() })
                        .ToList();
                    return View(model);
                }

                user.SubscriptionType = model.SubscriptionType;
                user.SubscriptionStartDate = model.SubscriptionStartDate;
                user.SubscriptionEndDate = model.SubscriptionEndDate;
                user.IsActive = true;

                var subscriptionHistory = new SubscriptionHistory
                {
                    ApplicationUserId = user.Id,
                    SubscriptionType = model.SubscriptionType,
                    StartDate = model.SubscriptionStartDate,
                    EndDate = model.SubscriptionEndDate,
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
                            <li>تاريخ الانتهاء: {model.SubscriptionEndDate:yyyy-MM-dd}</li>
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
                ViewData["SubscriptionTypes"] = Enum.GetValues(typeof(SubscriptionType))
                    .Cast<SubscriptionType>()
                    .Select(e => new { Id = (int)e, Name = e.GetDisplayName() })
                    .ToList();
                return View(model);
            }
        }

        // GET: /Subscription/Block/{userId}
        [HttpGet]
        public async Task<IActionResult> Block(string userId)
        {
            var user = await _context.ApplicationUsers
                .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);

            if (user == null)
            {
                TempData["Error"] = "المستخدم غير موجود";
                return RedirectToAction(nameof(Index));
            }

            if (!user.IsActive)
            {
                TempData["Error"] = "الحساب محظور بالفعل";
                return RedirectToAction(nameof(Index));
            }

            ViewData["UserId"] = userId;
            ViewData["FullName"] = user.FullName;
            return View();
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
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction(nameof(Index));
                }

                if (!user.IsActive)
                {
                    TempData["Error"] = "الحساب محظور بالفعل";
                    return RedirectToAction(nameof(Index));
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

                TempData["Success"] = "تم حظر الحساب بشكل دائم بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: /Subscription/Unblock/{userId}
        [HttpGet]
        public async Task<IActionResult> Unblock(string userId)
        {
            var user = await _context.ApplicationUsers
                .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);

            if (user == null)
            {
                TempData["Error"] = "المستخدم غير موجود";
                return RedirectToAction(nameof(Index));
            }

            if (user.IsActive)
            {
                TempData["Error"] = "الحساب نشط بالفعل";
                return RedirectToAction(nameof(Index));
            }

            ViewData["UserId"] = userId;
            ViewData["FullName"] = user.FullName;
            return View();
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
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction(nameof(Index));
                }

                if (user.IsActive)
                {
                    TempData["Error"] = "الحساب نشط بالفعل";
                    return RedirectToAction(nameof(Index));
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

                TempData["Success"] = "تم إلغاء حظر الحساب بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: /Subscription/Delete/{userId}
        [HttpGet]
        public async Task<IActionResult> Delete(string userId)
        {
            var user = await _context.ApplicationUsers
                .Include(u => u.FinancialTransactions)
                .Include(u => u.Clients)
                .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);

            if (user == null)
            {
                TempData["Error"] = "المستخدم غير موجود";
                return RedirectToAction(nameof(Index));
            }

            ViewData["UserId"] = userId;
            ViewData["FullName"] = user.FullName;
            return View();
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
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction(nameof(Index));
                }

                if (user.FinancialTransactions.Any() || user.Clients.Any())
                {
                    TempData["Error"] = "لا يمكن حذف الحساب لوجود معاملات أو عملاء مرتبطين";
                    return RedirectToAction(nameof(Index));
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

                TempData["Success"] = "تم حذف الحساب بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ: {ex.Message}";
                return RedirectToAction(nameof(Index));
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

        // دالة لحساب تاريخ انتهاء الاشتراك
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

    // دالة مساعدة للحصول على اسم العرض
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