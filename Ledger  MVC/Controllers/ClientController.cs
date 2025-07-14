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

        // نموذج إدخال بيانات العميل
        public class ClientViewModel
        {
            [Required(ErrorMessage = "الاسم مطلوب")]
            public string Name { get; set; }

            [Required(ErrorMessage = "رقم الهاتف مطلوب")]
            public string PhoneNumber { get; set; }

            [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صحيح")]
            public string? Email { get; set; }
        }

        // نموذج تعديل بيانات العميل
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
                    .Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.PhoneNumber,
                        c.Email,
                        TransactionCount = c.Transactions.Count
                    })
                    .ToListAsync();

                return View(clients);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ غير متوقع أثناء استرجاع قائمة العملاء. UserId: {UserId}", User.Identity.Name);
                TempData["Error"] = "حدث خطأ غير متوقع، حاول لاحقًا";
                return View(new List<object>());
            }
        }

        // GET: /Client/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Client/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ClientViewModel model)
        {
            if (!ModelState.IsValid)
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

                var model = new
                {
                    client.Id,
                    client.Name,
                    client.PhoneNumber,
                    client.Email,
                    TransactionsCount = client.Transactions.Count,
                    Transactions = client.Transactions.Select(t => new
                    {
                        t.Id,
                        t.Type,
                        t.Amount,
                        t.Date,
                        t.Notes
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
            if (!ModelState.IsValid)
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

        // GET: /Client/Delete/{id}
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
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

            var model = new
            {
                client.Id,
                client.Name,
                client.PhoneNumber,
                client.Email
            };

            return View(model);
        }

        // POST: /Client/Delete/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    _logger.LogWarning("المستخدم غير موجود. UserId: {UserId}", User.Identity.Name);
                    TempData["Error"] = "المستخدم غير موجود";
                    return RedirectToAction(nameof(Index));
                }

                var client = await _context.Clients
                    .Include(c => c.Transactions)
                    .FirstOrDefaultAsync(c => c.Id == id && c.ApplicationUserId == user.Id);

                if (client == null)
                {
                    TempData["Error"] = "العميل غير موجود أو لا ينتمي لك";
                    return RedirectToAction(nameof(Index));
                }

                if (client.Transactions.Any())
                {
                    TempData["Error"] = "لا يمكن حذف العميل لوجود معاملات مرتبطة به";
                    return RedirectToAction(nameof(Index));
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
                TempData["Success"] = "تم حذف العميل بنجاح";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ أثناء حذف العميل. UserId: {UserId}", User.Identity.Name);
                TempData["Error"] = "حدث خطأ، حاول لاحقًا";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}