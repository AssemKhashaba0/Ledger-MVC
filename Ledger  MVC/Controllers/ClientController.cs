using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using Ledger__MVC.Data;
using Ledger__MVC.Models;
using System.ComponentModel.DataAnnotations;

namespace Ledger__MVC.Controllers
{
    [Authorize(Roles = nameof(UserRole.User))]
    public class ClientController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ClientController> _logger;

        public ClientController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger<ClientController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        // ViewModel for Index
        public class ClientIndexViewModel
        {
            public List<ClientViewModel> Clients { get; set; }
        }

        // ViewModel for Client List
        public class ClientViewModel
        {
            public int Id { get; set; }
            [Required(ErrorMessage = "الاسم مطلوب")]
            public string Name { get; set; }
            [Required(ErrorMessage = "رقم الهاتف مطلوب")]
            public string PhoneNumber { get; set; }
            [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صحيح")]
            public string? Email { get; set; }
            public int TransactionCount { get; set; }
            public decimal NetBalance { get; set; } // Added for balance summary
            public string Status { get; set; } // Added for status (له فلوس، عليه فلوس، متسوي)
        }

        // ViewModel for Client Details
        public class ClientDetailsViewModel
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string PhoneNumber { get; set; }
            public string? Email { get; set; }
            public int TransactionsCount { get; set; }
            public List<TransactionViewModel> Transactions { get; set; }
        }

        // ViewModel for Transactions
        public class TransactionViewModel
        {
            public int Id { get; set; }
            public TransactionType Type { get; set; }
            public decimal Amount { get; set; }
            public DateTime Date { get; set; }
            public string Notes { get; set; }
        }

        // ViewModel for Update Client
        public class UpdateClientViewModel
        {
            public int Id { get; set; }
            [Required(ErrorMessage = "الاسم مطلوب")]
            public string Name { get; set; }
            [Required(ErrorMessage = "رقم الهاتف مطلوب")]
            public string PhoneNumber { get; set; }
            [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صحيح")]
            public string? Email { get; set; }
        }

        // GET: /Client/Index
        [HttpGet]
        // GET: /Client/Index
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    _logger.LogWarning("المستخدم غير موجود. UserId: {UserId}", User.Identity.Name);
                    return NotFound("المستخدم غير موجود");
                }

                if (!user.IsActive || user.SubscriptionEndDate < DateTime.Now)
                {
                    _logger.LogWarning("اشتراك المستخدم منتهي أو الحساب غير نشط. UserId: {UserId}", user.Id);
                    TempData["Error"] = "انتهى اشتراكك، برجاء التجديد";
                    return RedirectToAction("Renewal", "Auth");
                }

                var clients = await _context.Clients
                    .Where(c => c.ApplicationUserId == user.Id)
                    .Include(c => c.Transactions)
                    .Select(c => new ClientViewModel
                    {
                        Id = c.Id,
                        Name = c.Name,
                        PhoneNumber = c.PhoneNumber,
                        Email = c.Email,
                        TransactionCount = c.Transactions.Count,
                        NetBalance = c.Transactions.Where(t => t.Type == TransactionType.Debt).Sum(t => t.Amount) -
                                     c.Transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount),
                        Status = c.Transactions.Any()
                            ? (c.Transactions.Where(t => t.Type == TransactionType.Debt).Sum(t => t.Amount) -
                               c.Transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount) > 0
                               ? "ليه فلوس"
                               : (c.Transactions.Where(t => t.Type == TransactionType.Debt).Sum(t => t.Amount) -
                                  c.Transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount) < 0
                                  ? "عليه فلوس"
                                  : "متسوي"))
                            : "متسوي"
                    })
                    .ToListAsync();

                var model = new ClientIndexViewModel
                {
                    Clients = clients
                };

                // Add transaction types to ViewData
                ViewData["TransactionTypes"] = Enum.GetValues(typeof(TransactionType))
                    .Cast<TransactionType>()
                    .Select(t => new TransactionController.TransactionTypeViewModel
                    {
                        Id = (int)t,
                        Name = TransactionController.GetEnumDisplayName(t)
                    })
                    .ToList();

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ غير متوقع أثناء استرجاع قائمة العملاء. UserId: {UserId}", User.Identity.Name);
                TempData["Error"] = "حدث خطأ غير متوقع، حاول لاحقًا";
                return View(new ClientIndexViewModel { Clients = new List<ClientViewModel>() });
            }
        }
        // GET: /Client/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View(new ClientViewModel());
        }

        // POST: /Client/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ClientViewModel model)
        {
            if (ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    _logger.LogWarning("المستخدم غير موجود. UserId: {UserId}", User.Identity.Name);
                    TempData["Error"] = "المستخدم غير موجود";
                    return View(model);
                }

                if (!user.IsActive || user.SubscriptionEndDate < DateTime.Now)
                {
                    _logger.LogWarning("اشتراك المستخدم منتهي أو الحساب غير نشط. UserId: {UserId}", user.Id);
                    TempData["Error"] = "انتهى اشتراكك، برجاء التجديد";
                    return RedirectToAction("Renewal", "Auth");
                }

                var existingClient = await _context.Clients
                    .FirstOrDefaultAsync(c => c.PhoneNumber == model.PhoneNumber && c.ApplicationUserId == user.Id);
                if (existingClient != null)
                {
                    ModelState.AddModelError("PhoneNumber", "رقم الهاتف مستخدم مسبقًا");
                    return View(model);
                }

                var client = new Client
                {
                    Name = model.Name,
                    PhoneNumber = model.PhoneNumber,
                    Email = model.Email,
                    ApplicationUserId = user.Id
                };

                _context.Clients.Add(client);

                _context.AuditLogs.Add(new AuditLog
                {
                    ApplicationUserId = user.Id,
                    Action = "إضافة عميل",
                    Details = $"تم إضافة العميل {model.Name} بواسطة المستخدم {user.Email}",
                    Timestamp = DateTime.Now
                });

                await _context.SaveChangesAsync();
                TempData["Success"] = "تم إضافة العميل بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ أثناء إضافة العميل. UserId: {UserId}", User.Identity.Name);
                TempData["Error"] = "حدث خطأ، حاول لاحقًا";
                return View(model);
            }
        }

        // GET: /Client/Details/{id}
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    _logger.LogWarning("المستخدم غير موجود. UserId: {UserId}", User.Identity.Name);
                    return NotFound("المستخدم غير موجود");
                }

                var client = await _context.Clients
                    .Include(c => c.Transactions)
                    .FirstOrDefaultAsync(c => c.Id == id && c.ApplicationUserId == user.Id);

                if (client == null)
                {
                    TempData["Error"] = "العميل غير موجود أو لا ينتمي لك";
                    return RedirectToAction(nameof(Index));
                }

                var model = new ClientDetailsViewModel
                {
                    Id = client.Id,
                    Name = client.Name,
                    PhoneNumber = client.PhoneNumber,
                    Email = client.Email,
                    TransactionsCount = client.Transactions.Count,
                    Transactions = client.Transactions.Select(t => new TransactionViewModel
                    {
                        Id = t.Id,
                        Type = t.Type,
                        Amount = t.Amount,
                        Date = t.Date,
                        Notes = t.Notes
                    }).ToList()
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ أثناء استرجاع تفاصيل العميل. UserId: {UserId}", User.Identity.Name);
                TempData["Error"] = "حدث خطأ، حاول لاحقًا";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: /Client/Edit/{id}
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("المستخدم غير موجود. UserId: {UserId}", User.Identity.Name);
                return NotFound("المستخدم غير موجود");
            }

            var client = await _context.Clients
                .FirstOrDefaultAsync(c => c.Id == id && c.ApplicationUserId == user.Id);

            if (client == null)
            {
                TempData["Error"] = "العميل غير موجود أو لا ينتمي لك";
                return RedirectToAction(nameof(Index));
            }

            var model = new UpdateClientViewModel
            {
                Id = client.Id,
                Name = client.Name,
                PhoneNumber = client.PhoneNumber,
                Email = client.Email
            };

            return View(model);
        }

        // POST: /Client/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UpdateClientViewModel model)
        {
            if (ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    _logger.LogWarning("المستخدم غير موجود. UserId: {UserId}", User.Identity.Name);
                    TempData["Error"] = "المستخدم غير موجود";
                    return View(model);
                }

                var client = await _context.Clients
                    .FirstOrDefaultAsync(c => c.Id == model.Id && c.ApplicationUserId == user.Id);

                if (client == null)
                {
                    TempData["Error"] = "العميل غير موجود أو لا ينتمي لك";
                    return RedirectToAction(nameof(Index));
                }

                var duplicatePhone = await _context.Clients
                    .AnyAsync(c => c.PhoneNumber == model.PhoneNumber && c.Id != model.Id && c.ApplicationUserId == user.Id);
                if (duplicatePhone)
                {
                    ModelState.AddModelError("PhoneNumber", "رقم الهاتف مستخدم مسبقًا");
                    return View(model);
                }

                client.Name = model.Name;
                client.PhoneNumber = model.PhoneNumber;
                client.Email = model.Email;

                _context.AuditLogs.Add(new AuditLog
                {
                    ApplicationUserId = user.Id,
                    Action = "تعديل عميل",
                    Details = $"تم تعديل بيانات العميل {client.Name}",
                    Timestamp = DateTime.Now
                });

                await _context.SaveChangesAsync();
                TempData["Success"] = "تم تعديل بيانات العميل بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ أثناء تعديل العميل. UserId: {UserId}", User.Identity.Name);
                TempData["Error"] = "حدث خطأ، حاول لاحقًا";
                return View(model);
            }
        }

        // POST: /Client/DeleteConfirmed/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return Json(new { success = false, message = "المستخدم غير موجود" });
                }

                var client = await _context.Clients
                    .Include(c => c.Transactions)
                    .FirstOrDefaultAsync(c => c.Id == id && c.ApplicationUserId == user.Id);

                if (client == null)
                {
                    return Json(new { success = false, message = "العميل غير موجود أو لا ينتمي لك" });
                }

                if (client.Transactions.Any())
                {
                    return Json(new { success = false, message = "لا يمكن حذف العميل لوجود معاملات مرتبطة به" });
                }

                _context.Clients.Remove(client);

                _context.AuditLogs.Add(new AuditLog
                {
                    ApplicationUserId = user.Id,
                    Action = "حذف عميل",
                    Details = $"تم حذف العميل {client.Name}",
                    Timestamp = DateTime.Now
                });

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "تم حذف العميل بنجاح" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ أثناء حذف العميل. UserId: {UserId}", User.Identity.Name);
                return Json(new { success = false, message = $"حدث خطأ: {ex.Message}" });
            }
        }
    }
}
