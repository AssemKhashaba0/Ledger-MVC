using Ledger__MVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Ledger__MVC.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        [AllowAnonymous]
        public IActionResult Index()
        {
            try
            {
                // إذا كان المستخدم مسجل دخول، توجيهه حسب الصلاحية
                if (User.Identity.IsAuthenticated)
                {
                    if (User.IsInRole("Admin"))
                    {
                        return RedirectToAction("Dashboard", "User");
                    }
                    else
                    {
                        return RedirectToAction("Summary", "Transaction");
                    }
                }

                return View();
            }
            catch (Exception ex)
            {
                // Log the error with more details
                _logger.LogError(ex, "Error in Home/Index: {Message}", ex.Message);
                
                // Return a simple view instead of Error to avoid redirect loops
                ViewBag.ErrorMessage = "حدث خطأ في تحميل الصفحة";
                return View("~/Views/Shared/SimpleError.cshtml");
            }
        }

        public IActionResult Privacy()
        {
            return View();
        }
        public IActionResult FQ()
        {
            return View();
        }
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            _logger.LogError("Error occurred. RequestId: {RequestId}", requestId);
            
            return View(new ErrorViewModel { RequestId = requestId }); 
        }
    }
}
