using Ledger__MVC.Data;
using Ledger__MVC.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Ledger__MVC.SubscriptionValidationMiddleware
{
    public class SubscriptionValidationMiddleware
    {
        private readonly RequestDelegate _next;

        public SubscriptionValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    var user = await dbContext.ApplicationUsers
                        .FirstOrDefaultAsync(u => u.Id == userId);

                    if (user != null && (!user.IsActive || user.SubscriptionEndDate < DateTime.Now))
                    {
                        // تسجيل خروج تلقائي
                        var signInManager = context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();
                        await signInManager.SignOutAsync();
                        
                        // إعادة توجيه لصفحة التجديد
                        context.Response.Redirect("/Auth/Renewal");
                        return;
                    }
                }
            }

            await _next(context);
        }
    }
}
