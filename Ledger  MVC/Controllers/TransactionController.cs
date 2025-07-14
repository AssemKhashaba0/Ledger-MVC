using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Ledger__MVC.Data;
using Ledger__MVC.Models;
using System.Threading.Tasks;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;

namespace Ledger__MVC.Controllers
{
    [Authorize(Roles = nameof(UserRole.User))]
    public class TransactionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TransactionController> _logger;

        public TransactionController(ApplicationDbContext context, ILogger<TransactionController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // دالة مساعدة لاستخراج الاسم العربي من enum
        public static string GetEnumDisplayName(TransactionType type)
        {
            var fieldInfo = type.GetType().GetField(type.ToString());
            var displayAttribute = fieldInfo?.GetCustomAttribute<DisplayAttribute>();
            return displayAttribute?.Name ?? type.ToString();
        }

        // نموذج عرض المعاملات
        public class TransactionViewModel
        {
            public int Id { get; set; }
            public int ClientId { get; set; }
            public string ClientName { get; set; }
            public TransactionType Type { get; set; }
            public string TypeDisplayName => GetEnumDisplayName(Type);
            public decimal Amount { get; set; }
            public DateTime Date { get; set; }
            public string Notes { get; set; }
        }

        // نموذج إضافة/تعديل معاملة
        public class CreateUpdateTransactionViewModel
        {
            [Required(ErrorMessage = "العميل مطلوب")]
            public int ClientId { get; set; }

            [Required(ErrorMessage = "نوع المعاملة مطلوب")]
            public TransactionType Type { get; set; }

            [Required(ErrorMessage = "المبلغ مطلوب")]
            [Range(0.01, double.MaxValue, ErrorMessage = "المبلغ يجب أن يكون أكبر من 0")]
            public decimal Amount { get; set; }

            [Required(ErrorMessage = "التاريخ مطلوب")]
            [DataType(DataType.Date)]
            public DateTime Date { get; set; }

            public string Notes { get; set; }
        }

        // نموذج ملخص المعاملات
        public class TransactionSummaryViewModel
        {
            public int ClientId { get; set; }
            public string ClientName { get; set; }
            public decimal TotalCredit { get; set; }
            public decimal TotalDebit { get; set; }
            public decimal NetBalance { get; set; }
            public string Status { get; set; }
            public List<TransactionViewModel> Transactions { get; set; }
        }

        // GET: /Transaction/Index
        [HttpGet]
        public async Task<IActionResult> Index(int? clientId)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("فشل استخراج معرف المستخدم من التوكن.");
                    TempData["Error"] = "المستخدم غير مصرح له";
                    return RedirectToAction("Index", "Home");
                }

                var user = await _context.ApplicationUsers
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);
                if (user == null)
                {
                    _logger.LogWarning("المستخدم غير موجود. UserId: {UserId}", userId);
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction("Index", "Home");
                }

                if (!user.IsActive || user.SubscriptionEndDate < DateTime.Now)
                {
                    _logger.LogWarning("اشتراك المستخدم منتهي أو الحساب غير نشط. UserId: {UserId}, IsActive: {IsActive}, SubscriptionEndDate: {EndDate}",
                        userId, user.IsActive, user.SubscriptionEndDate);
                    TempData["Error"] = "انتهى اشتراكك، برجاء التجديد";
                    return RedirectToAction("Index", "Home");
                }

                var query = _context.FinancialTransactions
                    .Where(t => t.ApplicationUserId == userId)
                    .Include(t => t.Client)
                    .AsQueryable();

                if (clientId.HasValue)
                {
                    var clientExists = await _context.Clients
                        .AnyAsync(c => c.Id == clientId.Value && c.ApplicationUserId == userId);
                    if (!clientExists)
                    {
                        _logger.LogWarning("العميل غير موجود أو لا ينتمي للمستخدم. ClientId: {ClientId}, UserId: {UserId}", clientId, userId);
                        TempData["Error"] = "العميل غير موجود";
                        return RedirectToAction(nameof(Index));
                    }
                    query = query.Where(t => t.ClientId == clientId.Value);
                }

                var transactions = await query
                    .Select(t => new TransactionViewModel
                    {
                        Id = t.Id,
                        ClientId = t.ClientId,
                        ClientName = t.Client.Name,
                        Type = t.Type,
                        Amount = t.Amount,
                        Date = t.Date,
                        Notes = t.Notes
                    })
                    .OrderByDescending(t => t.Date)
                    .ToListAsync();

                ViewData["Clients"] = await _context.Clients
                    .Where(c => c.ApplicationUserId == userId)
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync();

                return View(transactions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ أثناء استرجاع المعاملات. UserId: {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["Error"] = "حدث خطأ غير متوقع";
                return View(new List<TransactionViewModel>());
            }
        }

        // GET: /Transaction/Create
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["Error"] = "المستخدم غير مصرح له";
                return RedirectToAction("Index", "Home");
            }

            var user = await _context.ApplicationUsers
                .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);
            if (user == null || !user.IsActive || user.SubscriptionEndDate < DateTime.Now)
            {
                TempData["Error"] = user == null ? "المستخدم غير موجود" : "انتهى اشتراكك، برجاء التجديد";
                return RedirectToAction("Index", "Home");
            }

            ViewData["Clients"] = await _context.Clients
                .Where(c => c.ApplicationUserId == userId)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();

            ViewData["TransactionTypes"] = Enum.GetValues(typeof(TransactionType))
                .Cast<TransactionType>()
                .Select(t => new { Id = (int)t, Name = GetEnumDisplayName(t) })
                .ToList();

            return View(new CreateUpdateTransactionViewModel { Date = DateTime.Now });
        }

        // POST: /Transaction/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateUpdateTransactionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Clients"] = await _context.Clients
                    .Where(c => c.ApplicationUserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync();
                ViewData["TransactionTypes"] = Enum.GetValues(typeof(TransactionType))
                    .Cast<TransactionType>()
                    .Select(t => new { Id = (int)t, Name = GetEnumDisplayName(t) })
                    .ToList();
                return View(model);
            }

            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    TempData["Error"] = "المستخدم غير مصرح له";
                    return RedirectToAction("Index", "Home");
                }

                var user = await _context.ApplicationUsers
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);
                if (user == null)
                {
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction("Index", "Home");
                }

                if (!user.IsActive || user.SubscriptionEndDate < DateTime.Now)
                {
                    TempData["Error"] = "انتهى اشتراكك، برجاء التجديد";
                    return RedirectToAction("Index", "Home");
                }

                var client = await _context.Clients
                    .FirstOrDefaultAsync(c => c.Id == model.ClientId && c.ApplicationUserId == userId);
                if (client == null)
                {
                    TempData["Error"] = "العميل غير موجود";
                    return RedirectToAction(nameof(Index));
                }

                var transaction = new FinancialTransaction
                {
                    ClientId = model.ClientId,
                    ApplicationUserId = userId,
                    Type = model.Type,
                    Amount = model.Amount,
                    Date = model.Date,
                    Notes = model.Notes
                };

                _context.FinancialTransactions.Add(transaction);

                var auditLog = new AuditLog
                {
                    ApplicationUserId = userId,
                    Action = "إضافة معاملة مالية",
                    Details = $"تم إضافة معاملة {GetEnumDisplayName(model.Type)} بقيمة {model.Amount} للعميل {client.Name} بواسطة المستخدم {user.Email}",
                    Timestamp = DateTime.Now
                };
                _context.AuditLogs.Add(auditLog);

                var notification = new Notification
                {
                    ApplicationUserId = userId,
                    Title = "تم إضافة معاملة جديدة",
                    Message = $"تم إضافة معاملة {GetEnumDisplayName(model.Type)} بقيمة {model.Amount} للعميل {client.Name}",
                    CreatedAt = DateTime.Now,
                    IsRead = false
                };
                _context.Notifications.Add(notification);

                await _context.SaveChangesAsync();

                TempData["Success"] = "تم إضافة المعاملة بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ أثناء إضافة المعاملة. UserId: {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["Error"] = "حدث خطأ غير متوقع";
                ViewData["Clients"] = await _context.Clients
                    .Where(c => c.ApplicationUserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync();
                ViewData["TransactionTypes"] = Enum.GetValues(typeof(TransactionType))
                    .Cast<TransactionType>()
                    .Select(t => new { Id = (int)t, Name = GetEnumDisplayName(t) })
                    .ToList();
                return View(model);
            }
        }

        // GET: /Transaction/Update/{id}
        [HttpGet]
        public async Task<IActionResult> Update(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["Error"] = "المستخدم غير مصرح له";
                return RedirectToAction("Index", "Home");
            }

            var transaction = await _context.FinancialTransactions
                .Include(t => t.Client)
                .FirstOrDefaultAsync(t => t.Id == id && t.ApplicationUserId == userId);

            if (transaction == null)
            {
                TempData["Error"] = "المعاملة غير موجودة أو لا تخصك";
                return RedirectToAction(nameof(Index));
            }

            var model = new CreateUpdateTransactionViewModel
            {
                ClientId = transaction.ClientId,
                Type = transaction.Type,
                Amount = transaction.Amount,
                Date = transaction.Date,
                Notes = transaction.Notes
            };

            ViewData["Clients"] = await _context.Clients
                .Where(c => c.ApplicationUserId == userId)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            ViewData["TransactionTypes"] = Enum.GetValues(typeof(TransactionType))
                .Cast<TransactionType>()
                .Select(t => new { Id = (int)t, Name = GetEnumDisplayName(t) })
                .ToList();

            return View(model);
        }

        // POST: /Transaction/Update/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int id, CreateUpdateTransactionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Clients"] = await _context.Clients
                    .Where(c => c.ApplicationUserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync();
                ViewData["TransactionTypes"] = Enum.GetValues(typeof(TransactionType))
                    .Cast<TransactionType>()
                    .Select(t => new { Id = (int)t, Name = GetEnumDisplayName(t) })
                    .ToList();
                return View(model);
            }

            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    TempData["Error"] = "المستخدم غير مصرح له";
                    return RedirectToAction("Index", "Home");
                }

                var transaction = await _context.FinancialTransactions
                    .Include(t => t.Client)
                    .FirstOrDefaultAsync(t => t.Id == id && t.ApplicationUserId == userId);

                if (transaction == null)
                {
                    TempData["Error"] = "المعاملة غير موجودة أو لا تخصك";
                    return RedirectToAction(nameof(Index));
                }

                var client = await _context.Clients
                    .FirstOrDefaultAsync(c => c.Id == model.ClientId && c.ApplicationUserId == userId);
                if (client == null)
                {
                    TempData["Error"] = "العميل غير موجود";
                    return RedirectToAction(nameof(Index));
                }

                transaction.ClientId = model.ClientId;
                transaction.Type = model.Type;
                transaction.Amount = model.Amount;
                transaction.Date = model.Date;
                transaction.Notes = model.Notes;

                _context.AuditLogs.Add(new AuditLog
                {
                    ApplicationUserId = userId,
                    Action = "تعديل معاملة",
                    Details = $"تم تعديل المعاملة رقم {transaction.Id} للعميل {client.Name}",
                    Timestamp = DateTime.Now
                });

                await _context.SaveChangesAsync();

                TempData["Success"] = "تم تعديل المعاملة بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ أثناء تعديل المعاملة. UserId: {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["Error"] = "حدث خطأ غير متوقع";
                ViewData["Clients"] = await _context.Clients
                    .Where(c => c.ApplicationUserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync();
                ViewData["TransactionTypes"] = Enum.GetValues(typeof(TransactionType))
                    .Cast<TransactionType>()
                    .Select(t => new { Id = (int)t, Name = GetEnumDisplayName(t) })
                    .ToList();
                return View(model);
            }
        }

        // GET: /Transaction/Delete/{id}
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["Error"] = "المستخدم غير مصرح له";
                return RedirectToAction("Index", "Home");
            }

            var transaction = await _context.FinancialTransactions
                .Include(t => t.Client)
                .FirstOrDefaultAsync(t => t.Id == id && t.ApplicationUserId == userId);

            if (transaction == null)
            {
                TempData["Error"] = "المعاملة غير موجودة أو لا تخصك";
                return RedirectToAction(nameof(Index));
            }

            var model = new TransactionViewModel
            {
                Id = transaction.Id,
                ClientId = transaction.ClientId,
                ClientName = transaction.Client.Name,
                Type = transaction.Type,
                Amount = transaction.Amount,
                Date = transaction.Date,
                Notes = transaction.Notes
            };

            return View(model);
        }

        // POST: /Transaction/DeleteConfirmed/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    TempData["Error"] = "المستخدم غير مصرح له";
                    return RedirectToAction("Index", "Home");
                }

                var transaction = await _context.FinancialTransactions
                    .Include(t => t.Client)
                    .FirstOrDefaultAsync(t => t.Id == id && t.ApplicationUserId == userId);

                if (transaction == null)
                {
                    TempData["Error"] = "المعاملة غير موجودة أو لا تخصك";
                    return RedirectToAction(nameof(Index));
                }

                _context.FinancialTransactions.Remove(transaction);

                _context.AuditLogs.Add(new AuditLog
                {
                    ApplicationUserId = userId,
                    Action = "حذف معاملة",
                    Details = $"تم حذف المعاملة رقم {transaction.Id} للعميل {transaction.Client.Name}",
                    Timestamp = DateTime.Now
                });

                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف المعاملة بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ أثناء حذف المعاملة. UserId: {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["Error"] = "حدث خطأ غير متوقع";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: /Transaction/Summary
        [HttpGet]
        public async Task<IActionResult> Summary()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    TempData["Error"] = "المستخدم غير مصرح له";
                    return RedirectToAction("Index", "Home");
                }

                var user = await _context.ApplicationUsers
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);
                if (user == null)
                {
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction("Index", "Home");
                }

                if (!user.IsActive || user.SubscriptionEndDate < DateTime.Now)
                {
                    TempData["Error"] = "انتهى اشتراكك، برجاء التجديد";
                    return RedirectToAction("Index", "Home");
                }

                var transactions = await _context.FinancialTransactions
                    .Where(t => t.ApplicationUserId == userId)
                    .Include(t => t.Client)
                    .OrderByDescending(t => t.Date)
                    .ToListAsync();

                var clientSummaries = transactions
                    .GroupBy(t => t.ClientId)
                    .Select(g =>
                    {
                        var client = g.First().Client;
                        var credit = g.Where(t => t.Type == TransactionType.Debt).Sum(t => t.Amount);
                        var debit = g.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount);
                        return new TransactionSummaryViewModel
                        {
                            ClientId = client.Id,
                            ClientName = client.Name,
                            TotalCredit = credit,
                            TotalDebit = debit,
                            NetBalance = credit - debit,
                            Status = credit - debit > 0 ? "ليه فلوس" : credit - debit < 0 ? "عليه فلوس" : "متسوي",
                            Transactions = g.Select(t => new TransactionViewModel
                            {
                                Id = t.Id,
                                ClientId = t.ClientId,
                                ClientName = t.Client.Name,
                                Type = t.Type,
                                Amount = t.Amount,
                                Date = t.Date,
                                Notes = t.Notes
                            }).ToList()
                        };
                    })
                    .ToList();

                var totalCredit = transactions.Where(t => t.Type == TransactionType.Debt).Sum(t => t.Amount);
                var totalDebit = transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount);
                var balance = totalCredit - totalDebit;
                var overallStatus = balance > 0 ? "ليك فلوس بره" : balance < 0 ? "عليك فلوس لناس" : "متساويين";

                ViewData["OverallSummary"] = new { TotalCredit = totalCredit, TotalDebit = totalDebit, Net = balance, Status = overallStatus };

                return View(clientSummaries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ أثناء استرجاع ملخص المعاملات. UserId: {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["Error"] = "حدث خطأ غير متوقع";
                return View(new List<TransactionSummaryViewModel>());
            }
        }

        // GET: /Transaction/Details/{id}
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["Error"] = "المستخدم غير مصرح له";
                return RedirectToAction("Index", "Home");
            }

            var transaction = await _context.FinancialTransactions
                .Include(t => t.Client)
                .FirstOrDefaultAsync(t => t.Id == id && t.ApplicationUserId == userId);

            if (transaction == null)
            {
                TempData["Error"] = "المعاملة غير موجودة أو لا تخصك";
                return RedirectToAction(nameof(Index));
            }

            var model = new TransactionViewModel
            {
                Id = transaction.Id,
                ClientId = transaction.ClientId,
                ClientName = transaction.Client.Name,
                Type = transaction.Type,
                Amount = transaction.Amount,
                Date = transaction.Date,
                Notes = transaction.Notes
            };

            return View(model);
        }
    }
}