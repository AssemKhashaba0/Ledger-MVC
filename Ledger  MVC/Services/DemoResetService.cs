using Microsoft.EntityFrameworkCore;
using Ledger__MVC.Data;
using Ledger__MVC.Models;

namespace Ledger__MVC.Services
{
    public class DemoResetService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DemoResetService> _logger;

        public DemoResetService(IServiceProvider serviceProvider, ILogger<DemoResetService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    await ResetDemoDataIfNeeded(context);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطأ في خدمة إعادة تعيين الديمو");
                }

                // انتظار 10 دقائق
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        private async Task ResetDemoDataIfNeeded(ApplicationDbContext context)
        {
            const string demoEmail = "demo@daftar.com";
            
            var demoUser = await context.ApplicationUsers
                .FirstOrDefaultAsync(u => u.Email == demoEmail);

            if (demoUser == null) return;

            // التحقق من آخر إعادة تعيين
            var timeSinceLastReset = DateTime.Now - (demoUser.LastDemoReset ?? DateTime.MinValue);
            
            if (timeSinceLastReset.TotalMinutes >= 10)
            {
                _logger.LogInformation("إعادة تعيين بيانات الديمو");
                await ResetDemoData(context, demoUser.Id);
            }
        }

        private async Task ResetDemoData(ApplicationDbContext context, string userId)
        {
            // حذف البيانات القديمة
            var existingClients = await context.Clients
                .Where(c => c.ApplicationUserId == userId)
                .ToListAsync();
            context.Clients.RemoveRange(existingClients);

            var existingTransactions = await context.FinancialTransactions
                .Where(t => t.ApplicationUserId == userId)
                .ToListAsync();
            context.FinancialTransactions.RemoveRange(existingTransactions);

            // إضافة بيانات جديدة
            var demoClients = new List<Client>
            {
                new Client { Name = "عميل تجريبي 1", PhoneNumber = "01012345678", ApplicationUserId = userId },
                new Client { Name = "عميل تجريبي 2", PhoneNumber = "01098765432", ApplicationUserId = userId }
            };

            context.Clients.AddRange(demoClients);
            await context.SaveChangesAsync();

            var demoTransactions = new List<FinancialTransaction>
            {
                new FinancialTransaction
                {
                    ClientId = demoClients[0].Id,
                    Amount = 1000,
                    Type = TransactionType.Debt,
                    Notes = "معاملة تجريبية",
                    Date = DateTime.Now,
                    ApplicationUserId = userId
                }
            };

            context.FinancialTransactions.AddRange(demoTransactions);

            // تحديث وقت الإعادة
            var user = await context.ApplicationUsers.FindAsync(userId);
            if (user != null)
            {
                user.LastDemoReset = DateTime.Now;
            }

            await context.SaveChangesAsync();
        }
    }
}

