using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Ledger__MVC.Data;
using Ledger__MVC.Models;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using System.Globalization;
using Ledger__MVC.Services;

namespace Ledger__MVC.Controllers
{
    [Authorize(Roles = nameof(UserRole.User))]
    public class TransactionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TransactionController> _logger;
        private readonly ExportService _exportService;

        public TransactionController(ApplicationDbContext context, ILogger<TransactionController> logger, ExportService exportService)
        {
            _context = context;
            _logger = logger;
            _exportService = exportService;
        }

        // Helper method to get enum display name
        public static string GetEnumDisplayName(TransactionType type)
        {
            var fieldInfo = type.GetType().GetField(type.ToString());
            var displayAttribute = fieldInfo?.GetCustomAttribute<DisplayAttribute>();
            return displayAttribute?.Name ?? type.ToString();
        }

        // ViewModel for displaying transactions
        public class TransactionViewModel
        {
            public int Id { get; set; }
            public int ClientId { get; set; }
            public string ClientName { get; set; }
            public TransactionType Type { get; set; }
            public string TypeDisplayName { get; set; }
            public decimal Amount { get; set; }
            public DateTime Date { get; set; }
            public string Notes { get; set; }
            
            // تفاصيل العميل
            public decimal ClientTotalDebt { get; set; }
            public decimal ClientTotalPayment { get; set; }
            public decimal ClientNetBalance { get; set; }
            public string ClientStatus { get; set; }
        }

        // ViewModel for creating/updating transactions
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
            public List<ClientSelectViewModel> Clients { get; set; } = new List<ClientSelectViewModel>();
            public List<TransactionTypeViewModel> TransactionTypes { get; set; } = new List<TransactionTypeViewModel>();
        }

        // ViewModel for client selection
        public class ClientSelectViewModel
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        // ViewModel for transaction types
        public class TransactionTypeViewModel
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        // ViewModel for transaction summary
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

        // ViewModel for transaction index
        public class TransactionIndexViewModel
        {
            public List<TransactionViewModel> Transactions { get; set; } = new List<TransactionViewModel>();
            public int? ClientId { get; set; }
        }

        // GET: /Transaction/Index
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Index(int? clientId)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("Failed to extract user ID from token.");
                    TempData["Error"] = "المستخدم غير مصرح له";
                    return RedirectToAction("Index", "Home");
                }

                var user = await _context.ApplicationUsers
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);
                if (user == null)
                {
                    _logger.LogWarning("User not found. UserId: {UserId}", userId);
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction("Index", "Home");
                }

                if (!user.IsActive || user.SubscriptionEndDate < DateTime.Now)
                {
                    _logger.LogWarning("User subscription expired or account inactive. UserId: {UserId}, IsActive: {IsActive}, SubscriptionEndDate: {EndDate}",
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
                    query = query.Where(t => t.ClientId == clientId.Value);
                }

                var transactions = await query
                    .OrderByDescending(t => t.Date)
                    .Select(t => new TransactionViewModel
                    {
                        Id = t.Id,
                        ClientId = t.ClientId,
                        ClientName = t.Client.Name,
                        Type = t.Type,
                        TypeDisplayName = t.Type == TransactionType.Debt ? "ليك (مديون)" : "عليك (دفعة)",
                        Amount = t.Amount,
                        Date = t.Date,
                        Notes = t.Notes
                    })
                    .ToListAsync();

                ViewData["Clients"] = await _context.Clients
                    .Where(c => c.ApplicationUserId == userId)
                    .Select(c => new ClientSelectViewModel { Id = c.Id, Name = c.Name })
                    .ToListAsync();

                var model = new TransactionIndexViewModel
                {
                    Transactions = transactions,
                    ClientId = clientId
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving transactions. UserId: {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["Error"] = "حدث خطأ غير متوقع";
                
                var emptyModel = new TransactionIndexViewModel
                {
                    Transactions = new List<TransactionViewModel>(),
                    ClientId = clientId
                };
                
                return View(emptyModel);
            }
        }

        // GET: /Transaction/Create
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Create(int? clientId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Failed to extract user ID from token.");
                TempData["Error"] = "المستخدم غير مصرح له";
                return RedirectToAction("Index", "Home");
            }

            var user = await _context.ApplicationUsers
                .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);
            if (user == null || !user.IsActive || user.SubscriptionEndDate < DateTime.Now)
            {
                _logger.LogWarning("User not found, inactive, or subscription expired. UserId: {UserId}", userId);
                TempData["Error"] = user == null ? "المستخدم غير موجود" : "انتهى اشتراكك، برجاء التجديد";
                return RedirectToAction("Index", "Home");
            }

            var model = new CreateUpdateTransactionViewModel
            {
                Date = DateTime.Now,
                ClientId = clientId ?? 0,
                Clients = await _context.Clients
                    .Where(c => c.ApplicationUserId == userId)
                    .Select(c => new ClientSelectViewModel { Id = c.Id, Name = c.Name })
                    .ToListAsync(),
                TransactionTypes = Enum.GetValues(typeof(TransactionType))
                    .Cast<TransactionType>()
                    .Select(t => new TransactionTypeViewModel { Id = (int)t, Name = GetEnumDisplayName(t) })
                    .ToList()
            };

            return View(model);
        }

        // POST: /Transaction/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Create(CreateUpdateTransactionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                model.Clients = await _context.Clients
                    .Where(c => c.ApplicationUserId == userId)
                    .Select(c => new ClientSelectViewModel { Id = c.Id, Name = c.Name })
                    .ToListAsync();
                model.TransactionTypes = Enum.GetValues(typeof(TransactionType))
                    .Cast<TransactionType>()
                    .Select(t => new TransactionTypeViewModel { Id = (int)t, Name = GetEnumDisplayName(t) })
                    .ToList();

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "بيانات الإدخال غير صحيحة" });
                }
                return View(model);
            }

            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("Failed to extract user ID from token.");
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return Json(new { success = false, message = "المستخدم غير مصرح له" });
                    }
                    TempData["Error"] = "المستخدم غير مصرح له";
                    return RedirectToAction("Index", "Home");
                }

                var user = await _context.ApplicationUsers
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);
                if (user == null)
                {
                    _logger.LogWarning("User not found. UserId: {UserId}", userId);
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return Json(new { success = false, message = "المستخدم غير موجود" });
                    }
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction("Index", "Home");
                }

                if (!user.IsActive || user.SubscriptionEndDate < DateTime.Now)
                {
                    _logger.LogWarning("User subscription expired or account inactive. UserId: {UserId}", userId);
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return Json(new { success = false, message = "انتهى اشتراكك، برجاء التجديد" });
                    }
                    TempData["Error"] = "انتهى اشتراكك، برجاء التجديد";
                    return RedirectToAction("Index", "Home");
                }

                var client = await _context.Clients
                    .FirstOrDefaultAsync(c => c.Id == model.ClientId && c.ApplicationUserId == userId);
                if (client == null)
                {
                    _logger.LogWarning("Client not found. ClientId: {ClientId}, UserId: {UserId}", model.ClientId, userId);
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return Json(new { success = false, message = "العميل غير موجود" });
                    }
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

                _context.AuditLogs.Add(new AuditLog
                {
                    ApplicationUserId = userId,
                    Action = "إضافة معاملة مالية",
                    Details = $"تم إضافة معاملة {GetEnumDisplayName(model.Type)} بقيمة {model.Amount} للعميل {client.Name} بواسطة المستخدم {user.Email}",
                    Timestamp = DateTime.Now
                });

                _context.Notifications.Add(new Notification
                {
                    ApplicationUserId = userId,
                    Title = "تم إضافة معاملة جديدة",
                    Message = $"تم إضافة معاملة {GetEnumDisplayName(model.Type)} بقيمة {model.Amount} للعميل {client.Name}",
                    CreatedAt = DateTime.Now,
                    IsRead = false
                });

                await _context.SaveChangesAsync();

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = true, message = "تم إضافة المعاملة بنجاح" });
                }

                TempData["Success"] = "تم إضافة المعاملة بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating transaction. UserId: {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = $"حدث خطأ: {ex.Message}" });
                }
                TempData["Error"] = "حدث خطأ غير متوقع";
                model.Clients = await _context.Clients
                    .Where(c => c.ApplicationUserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
                    .Select(c => new ClientSelectViewModel { Id = c.Id, Name = c.Name })
                    .ToListAsync();
                model.TransactionTypes = Enum.GetValues(typeof(TransactionType))
                    .Cast<TransactionType>()
                    .Select(t => new TransactionTypeViewModel { Id = (int)t, Name = GetEnumDisplayName(t) })
                    .ToList();
                return View(model);
            }
        }

        // GET: /Transaction/Update/{id}
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Update(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Failed to extract user ID from token.");
                TempData["Error"] = "المستخدم غير مصرح له";
                return RedirectToAction("Index", "Home");
            }

            var transaction = await _context.FinancialTransactions
                .Include(t => t.Client)
                .FirstOrDefaultAsync(t => t.Id == id && t.ApplicationUserId == userId);

            if (transaction == null)
            {
                _logger.LogWarning("Transaction not found or does not belong to user. TransactionId: {TransactionId}, UserId: {UserId}", id, userId);
                TempData["Error"] = "المعاملة غير موجودة أو لا تخصك";
                return RedirectToAction(nameof(Index));
            }

            var model = new CreateUpdateTransactionViewModel
            {
                ClientId = transaction.ClientId,
                Type = transaction.Type,
                Amount = transaction.Amount,
                Date = transaction.Date,
                Notes = transaction.Notes,
                Clients = await _context.Clients
                    .Where(c => c.ApplicationUserId == userId)
                    .Select(c => new ClientSelectViewModel { Id = c.Id, Name = c.Name })
                    .ToListAsync(),
                TransactionTypes = Enum.GetValues(typeof(TransactionType))
                    .Cast<TransactionType>()
                    .Select(t => new TransactionTypeViewModel { Id = (int)t, Name = GetEnumDisplayName(t) })
                    .ToList()
            };

            return View(model);
        }

        // POST: /Transaction/Update/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Update(int id, CreateUpdateTransactionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                model.Clients = await _context.Clients
                    .Where(c => c.ApplicationUserId == userId)
                    .Select(c => new ClientSelectViewModel { Id = c.Id, Name = c.Name })
                    .ToListAsync();
                model.TransactionTypes = Enum.GetValues(typeof(TransactionType))
                    .Cast<TransactionType>()
                    .Select(t => new TransactionTypeViewModel { Id = (int)t, Name = GetEnumDisplayName(t) })
                    .ToList();
                return View(model);
            }

            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("Failed to extract user ID from token.");
                    TempData["Error"] = "المستخدم غير مصرح له";
                    return RedirectToAction("Index", "Home");
                }

                var transaction = await _context.FinancialTransactions
                    .Include(t => t.Client)
                    .FirstOrDefaultAsync(t => t.Id == id && t.ApplicationUserId == userId);

                if (transaction == null)
                {
                    _logger.LogWarning("Transaction not found or does not belong to user. TransactionId: {TransactionId}, UserId: {UserId}", id, userId);
                    TempData["Error"] = "المعاملة غير موجودة أو لا تخصك";
                    return RedirectToAction(nameof(Index));
                }

                var client = await _context.Clients
                    .FirstOrDefaultAsync(c => c.Id == model.ClientId && c.ApplicationUserId == userId);
                if (client == null)
                {
                    _logger.LogWarning("Client not found. ClientId: {ClientId}, UserId: {UserId}", model.ClientId, userId);
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
                _logger.LogError(ex, "Error updating transaction. UserId: {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["Error"] = "حدث خطأ غير متوقع";
                model.Clients = await _context.Clients
                    .Where(c => c.ApplicationUserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
                    .Select(c => new ClientSelectViewModel { Id = c.Id, Name = c.Name })
                    .ToListAsync();
                model.TransactionTypes = Enum.GetValues(typeof(TransactionType))
                    .Cast<TransactionType>()
                    .Select(t => new TransactionTypeViewModel { Id = (int)t, Name = GetEnumDisplayName(t) })
                    .ToList();
                return View(model);
            }
        }

        // POST: /Transaction/DeleteConfirmed/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Json(new { success = false, message = "المستخدم غير مصرح له" });
                }

                var transaction = await _context.FinancialTransactions
                    .Include(t => t.Client)
                    .FirstOrDefaultAsync(t => t.Id == id && t.ApplicationUserId == userId);

                if (transaction == null)
                {
                    return Json(new { success = false, message = "المعاملة غير موجودة أو لا تخصك" });
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

                return Json(new { success = true, message = "تم حذف المعاملة بنجاح" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting transaction. UserId: {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
                return Json(new { success = false, message = $"حدث خطأ: {ex.Message}" });
            }
        }

        // GET: /Transaction/Summary
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Summary()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("Failed to extract user ID from token.");
                    TempData["Error"] = "المستخدم غير مصرح له";
                    return RedirectToAction("Index", "Home");
                }

                var user = await _context.ApplicationUsers
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.User);
                if (user == null)
                {
                    _logger.LogWarning("User not found. UserId: {UserId}", userId);
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction("Index", "Home");
                }

                if (!user.IsActive || user.SubscriptionEndDate < DateTime.Now)
                {
                    _logger.LogWarning("User subscription expired or account inactive. UserId: {UserId}", userId);
                    TempData["Error"] = "انتهى اشتراكك، برجاء التجديد";
                    return RedirectToAction("Index", "Home");
                }

                // Fetch all transactions for the user
                var transactions = await _context.FinancialTransactions
                    .Where(t => t.ApplicationUserId == userId)
                    .Include(t => t.Client)
                    .ToListAsync();

                // حساب الإجماليات بشكل صحيح
                var totalCredit = transactions.Where(t => t.Type == TransactionType.Debt).Sum(t => t.Amount); // المبالغ المستحقة ليك
                var totalDebit = transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount); // المبالغ المدفوعة عليك
                var netBalance = totalCredit - totalDebit; // الرصيد الصافي

                string status;
                string actionMessage;

                if (netBalance > 0)
                {
                    status = "لك مستحقات! 💰";
                    actionMessage = $"يوجد لديك مبلغ {Math.Abs(netBalance):N2} جنيه مستحق من العملاء. تابع مع عملائك لتحصيل المبالغ المستحقة.";
                }
                else if (netBalance < 0)
                {
                    status = "عليك مدفوعات! ⚠️";
                    actionMessage = $"يوجد عليك مبلغ {Math.Abs(netBalance):N2} جنيه مستحق للعملاء. تأكد من سداد المبالغ المستحقة في الوقت المناسب.";
                }
                else
                {
                    status = "حساباتك متساوية! ✅";
                    actionMessage = "أنت في المسار الصحيح! استمر في إدارة معاملاتك بكفاءة للحفاظ على التوازن.";
                }

                // إحصائيات إضافية
                var clientCount = await _context.Clients.CountAsync(c => c.ApplicationUserId == userId);
                var transactionCount = transactions.Count;
                var thisMonthTransactions = transactions.Where(t => t.Date.Month == DateTime.Now.Month && t.Date.Year == DateTime.Now.Year).Count();
                var avgTransactionAmount = transactions.Any() ? transactions.Average(t => t.Amount) : 0;

                // Prepare the summary view model
                var summary = new
                {
                    TotalCredit = totalCredit,
                    TotalDebit = totalDebit,
                    NetBalance = netBalance,
                    Status = status,
                    ActionMessage = actionMessage,
                    ClientCount = clientCount,
                    TransactionCount = transactionCount,
                    ThisMonthTransactions = thisMonthTransactions,
                    AverageTransactionAmount = avgTransactionAmount,
                    UserName = user.FullName ?? user.UserName
                };

                ViewData["FinancialSummary"] = summary;
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving financial summary. UserId: {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["Error"] = "حدث خطأ غير متوقع، حاول لاحقًا";
                return View();
            }
        }
        // GET: /Transaction/Details/{id}
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Details(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Failed to extract user ID from token.");
                TempData["Error"] = "المستخدم غير مصرح له";
                return RedirectToAction("Index", "Home");
            }

            var transaction = await _context.FinancialTransactions
                .Include(t => t.Client)
                .ThenInclude(c => c.Transactions)
                .FirstOrDefaultAsync(t => t.Id == id && t.ApplicationUserId == userId);

            if (transaction == null)
            {
                _logger.LogWarning("Transaction not found or does not belong to user. TransactionId: {TransactionId}, UserId: {UserId}", id, userId);
                TempData["Error"] = "المعاملة غير موجودة أو لا تخصك";
                return RedirectToAction(nameof(Index));
            }

            // حساب إجماليات العميل
            var totalDebt = transaction.Client.Transactions.Where(t => t.Type == TransactionType.Debt).Sum(t => t.Amount);
            var totalPayment = transaction.Client.Transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount);
            var netBalance = totalDebt - totalPayment;

            var model = new TransactionViewModel
            {
                Id = transaction.Id,
                ClientId = transaction.ClientId,
                ClientName = transaction.Client.Name,
                Type = transaction.Type,
                TypeDisplayName = transaction.Type == TransactionType.Debt ? "ليك (مديون)" : "عليك (دفعة)",
                Amount = transaction.Amount,
                Date = transaction.Date,
                Notes = transaction.Notes,
                // إضافة تفاصيل العميل
                ClientTotalDebt = totalDebt,
                ClientTotalPayment = totalPayment,
                ClientNetBalance = netBalance,
                ClientStatus = netBalance > 0 ? "ليك فلوس" : netBalance < 0 ? "عليك فلوس" : "متسوي"
            };

            return View(model);
        }

        // GET: /Transaction/ExportClientTransactionsToHtml/{clientId}
        [HttpGet]
        public async Task<IActionResult> ExportClientTransactionsToHtml(int clientId)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized();
                }

                var client = await _context.Clients
                    .Include(c => c.Transactions)
                    .FirstOrDefaultAsync(c => c.Id == clientId && c.ApplicationUserId == userId);

                if (client == null)
                {
                    TempData["Error"] = "العميل غير موجود أو لا ينتمي لك";
                    return RedirectToAction(nameof(Index));
                }

                // حساب الإجماليات
                var totalDebt = client.Transactions.Where(t => t.Type == TransactionType.Debt).Sum(t => t.Amount);
                var totalPayment = client.Transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount);
                var netBalance = totalDebt - totalPayment;

                var model = new
                {
                    Client = client,
                    TotalDebt = totalDebt,
                    TotalPayment = totalPayment,
                    NetBalance = netBalance,
                    Status = netBalance > 0 ? "ليك فلوس" : netBalance < 0 ? "عليك فلوس" : "متسوي"
                };

                return View("TransactionReport", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ أثناء تصدير معاملات العميل. ClientId: {ClientId}", clientId);
                TempData["Error"] = "حدث خطأ أثناء تصدير التقرير";
                return RedirectToAction(nameof(Index), new { clientId });
            }
        }

        // GET: /Transaction/ExportClientToExcel/{clientId}
        [HttpGet]
        public async Task<IActionResult> ExportClientToExcel(int clientId)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var user = await _context.ApplicationUsers.FindAsync(userId);
                
                var client = await _context.Clients
                    .Include(c => c.Transactions)
                    .FirstOrDefaultAsync(c => c.Id == clientId && c.ApplicationUserId == userId);

                if (client == null)
                {
                    TempData["Error"] = "العميل غير موجود";
                    return RedirectToAction(nameof(Index));
                }

                var excelData = _exportService.ExportTransactionsToExcel(client.Transactions.ToList(), client, user.FullName);
                var fileName = $"معاملات_{client.Name}_{DateTime.Now:yyyy-MM-dd}.xlsx";
                
                return File(excelData, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ في تصدير Excel");
                TempData["Error"] = "حدث خطأ أثناء التصدير";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: /Transaction/ExportClientToPdf/{clientId}
        [HttpGet]
        public async Task<IActionResult> ExportClientToPdf(int clientId)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var user = await _context.ApplicationUsers.FindAsync(userId);
                
                var client = await _context.Clients
                    .Include(c => c.Transactions)
                    .FirstOrDefaultAsync(c => c.Id == clientId && c.ApplicationUserId == userId);

                if (client == null)
                {
                    TempData["Error"] = "العميل غير موجود";
                    return RedirectToAction(nameof(Index));
                }

                var pdfData = _exportService.ExportClientToPdf(client.Transactions.ToList(), client, user.FullName);
                var fileName = $"معاملات_{client.Name}_{DateTime.Now:yyyy-MM-dd}.pdf";
                
                return File(pdfData, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ في تصدير PDF");
                TempData["Error"] = "حدث خطأ أثناء التصدير";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: /Transaction/ExportAllToExcel
        [HttpGet]
        public async Task<IActionResult> ExportAllToExcel()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var user = await _context.ApplicationUsers.FindAsync(userId);
                
                var transactions = await _context.FinancialTransactions
                    .Where(t => t.ApplicationUserId == userId)
                    .Include(t => t.Client)
                    .ToListAsync();

                var dummyClient = new Client { Name = "جميع العملاء", PhoneNumber = "متعدد" };
                var excelData = _exportService.ExportTransactionsToExcel(transactions, dummyClient, user.FullName);
                var fileName = $"جميع_المعاملات_{DateTime.Now:yyyy-MM-dd}.xlsx";
                
                return File(excelData, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ في تصدير جميع المعاملات");
                TempData["Error"] = "حدث خطأ أثناء التصدير";
                return RedirectToAction(nameof(Index));
            }
        }
    }
  
}
