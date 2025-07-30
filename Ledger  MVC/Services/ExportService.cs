using OfficeOpenXml;
using OfficeOpenXml.Style;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Drawing;
using System.Globalization;
using Ledger__MVC.Models;

namespace Ledger__MVC.Services
{
    public class ExportService
    {
        public byte[] ExportTransactionsToExcel(List<FinancialTransaction> transactions, Client client, string userFullName)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("تقرير المعاملات");
            
            worksheet.View.RightToLeft = true;
            
            // العنوان الرئيسي
            worksheet.Cells["A1:F1"].Merge = true;
            worksheet.Cells["A1"].Value = $"تقرير معاملات العميل: {client.Name}";
            worksheet.Cells["A1"].Style.Font.Size = 16;
            worksheet.Cells["A1"].Style.Font.Bold = true;
            worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells["A1"].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillPatternType.Solid;
            worksheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(79, 129, 189));
            worksheet.Cells["A1"].Style.Font.Color.SetColor(Color.White);
            
            // معلومات العميل
            worksheet.Cells["A3"].Value = "اسم العميل:";
            worksheet.Cells["B3"].Value = client.Name;
            worksheet.Cells["A4"].Value = "رقم الهاتف:";
            worksheet.Cells["B4"].Value = client.PhoneNumber;
            worksheet.Cells["A5"].Value = "تاريخ التقرير:";
            worksheet.Cells["B5"].Value = DateTime.Now.ToString("yyyy/MM/dd");
            worksheet.Cells["A6"].Value = "المستخدم:";
            worksheet.Cells["B6"].Value = userFullName;
            
            // الإجماليات
            var totalDebt = transactions.Where(t => t.Type == TransactionType.Debt).Sum(t => t.Amount);
            var totalPayment = transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount);
            var netBalance = totalDebt - totalPayment;
            
            worksheet.Cells["A8"].Value = "إجمالي ليك:";
            worksheet.Cells["B8"].Value = totalDebt;
            worksheet.Cells["B8"].Style.Numberformat.Format = "#,##0.00 ج.م";
            worksheet.Cells["A9"].Value = "إجمالي عليك:";
            worksheet.Cells["B9"].Value = totalPayment;
            worksheet.Cells["B9"].Style.Numberformat.Format = "#,##0.00 ج.م";
            worksheet.Cells["A10"].Value = "الرصيد النهائي:";
            worksheet.Cells["B10"].Value = Math.Abs(netBalance);
            worksheet.Cells["B10"].Style.Numberformat.Format = "#,##0.00 ج.م";
            
            // رؤوس الأعمدة
            var headers = new[] { "التاريخ", "النوع", "المبلغ", "الملاحظات" };
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[12, i + 1].Value = headers[i];
                worksheet.Cells[12, i + 1].Style.Font.Bold = true;
                worksheet.Cells[12, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillPatternType.Solid;
                worksheet.Cells[12, i + 1].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                worksheet.Cells[12, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
            }
            
            // البيانات
            for (int i = 0; i < transactions.Count; i++)
            {
                var transaction = transactions.OrderBy(t => t.Date).ToList()[i];
                var row = i + 13;
                
                worksheet.Cells[row, 1].Value = transaction.Date.ToString("yyyy/MM/dd");
                worksheet.Cells[row, 2].Value = transaction.Type == TransactionType.Debt ? "ليك (مديون)" : "عليك (دفعة)";
                worksheet.Cells[row, 3].Value = transaction.Amount;
                worksheet.Cells[row, 3].Style.Numberformat.Format = "#,##0.00 ج.م";
                worksheet.Cells[row, 4].Value = transaction.Notes ?? "";
                
                // تلوين حسب النوع
                var rowColor = transaction.Type == TransactionType.Debt ? 
                              Color.FromArgb(255, 235, 235) : 
                              Color.FromArgb(235, 255, 235);
                              
                for (int col = 1; col <= 4; col++)
                {
                    worksheet.Cells[row, col].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillPatternType.Solid;
                    worksheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(rowColor);
                    worksheet.Cells[row, col].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                }
            }
            
            // تنسيق الأعمدة
            for (int col = 1; col <= 4; col++)
            {
                worksheet.Column(col).AutoFit();
            }
            
            return package.GetAsByteArray();
        }
        
        public byte[] ExportClientToPdf(List<FinancialTransaction> transactions, Client client, string userFullName)
        {
            using var stream = new MemoryStream();
            var document = new Document(PageSize.A4, 40, 40, 50, 50);
            var writer = PdfWriter.GetInstance(document, stream);
            
            document.Open();
            
            // إعداد الخطوط العربية
            BaseFont arabicFont;
            try
            {
                arabicFont = BaseFont.CreateFont("c:\\windows\\fonts\\tahoma.ttf", BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
            }
            catch
            {
                try
                {
                    arabicFont = BaseFont.CreateFont("c:\\windows\\fonts\\arialuni.ttf", BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
                }
                catch
                {
                    try
                    {
                        arabicFont = BaseFont.CreateFont("c:\\windows\\fonts\\arial.ttf", BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
                    }
                    catch
                    {
                        arabicFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED);
                    }
                }
            }
            
            var titleFont = new iTextSharp.text.Font(arabicFont, 18, iTextSharp.text.Font.BOLD, new BaseColor(255, 255, 255));
            var headerFont = new iTextSharp.text.Font(arabicFont, 12, iTextSharp.text.Font.BOLD, new BaseColor(44, 62, 80));
            var normalFont = new iTextSharp.text.Font(arabicFont, 10, iTextSharp.text.Font.NORMAL, new BaseColor(52, 73, 94));
            var smallFont = new iTextSharp.text.Font(arabicFont, 8, iTextSharp.text.Font.NORMAL, new BaseColor(127, 140, 141));
            
            // إضافة اللوجو والعنوان
            var logoHeaderTable = new PdfPTable(2) { WidthPercentage = 100 };
            logoHeaderTable.SetWidths(new float[] { 1, 4 });
            
            // إضافة اللوجو
            try
            {
                var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "IMG", "ASVS Logo.png");
                if (File.Exists(logoPath))
                {
                    var logo = iTextSharp.text.Image.GetInstance(logoPath);
                    logo.ScaleToFit(60f, 60f);
                    
                    var logoCell = new PdfPCell(logo)
                    {
                        BackgroundColor = new BaseColor(44, 62, 80), // Dark Blue-Gray
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        VerticalAlignment = Element.ALIGN_MIDDLE,
                        Padding = 10,
                        Border = iTextSharp.text.Rectangle.NO_BORDER
                    };
                    logoHeaderTable.AddCell(logoCell);
                }
                else
                {
                    var emptyCell = new PdfPCell(new Phrase(""))
                    {
                        BackgroundColor = new BaseColor(44, 62, 80),
                        Border = iTextSharp.text.Rectangle.NO_BORDER
                    };
                    logoHeaderTable.AddCell(emptyCell);
                }
            }
            catch
            {
                var emptyCell = new PdfPCell(new Phrase(""))
                {
                    BackgroundColor = new BaseColor(44, 62, 80),
                    Border = iTextSharp.text.Rectangle.NO_BORDER
                };
                logoHeaderTable.AddCell(emptyCell);
            }
            
            // العنوان
            var titleCell = new PdfPCell(new Phrase($"تقرير معاملات العميل: {client.Name}", titleFont))
            {
                BackgroundColor = new BaseColor(44, 62, 80), // Dark Blue-Gray
                HorizontalAlignment = Element.ALIGN_CENTER,
                VerticalAlignment = Element.ALIGN_MIDDLE,
                Padding = 15,
                Border = iTextSharp.text.Rectangle.NO_BORDER,
                RunDirection = PdfWriter.RUN_DIRECTION_RTL
            };
            logoHeaderTable.AddCell(titleCell);
            
            logoHeaderTable.SpacingAfter = 20;
            document.Add(logoHeaderTable);
            
            // معلومات العميل
            var clientInfoTable = new PdfPTable(4) { WidthPercentage = 100 };
            clientInfoTable.SetWidths(new float[] { 1, 2, 1, 2 });
            clientInfoTable.RunDirection = PdfWriter.RUN_DIRECTION_RTL;
            
            clientInfoTable.AddCell(CreateInfoCell("اسم العميل:", headerFont, true));
            clientInfoTable.AddCell(CreateInfoCell(client.Name, normalFont, true));
            clientInfoTable.AddCell(CreateInfoCell("رقم الهاتف:", headerFont, true));
            clientInfoTable.AddCell(CreateInfoCell(client.PhoneNumber, normalFont, false));
            
            clientInfoTable.AddCell(CreateInfoCell("البريد الإلكتروني:", headerFont, true));
            clientInfoTable.AddCell(CreateInfoCell(client.Email ?? "غير محدد", normalFont, false));
            clientInfoTable.AddCell(CreateInfoCell("تاريخ التقرير:", headerFont, true));
            clientInfoTable.AddCell(CreateInfoCell(DateTime.Now.ToString("yyyy/MM/dd HH:mm"), normalFont, false));
            
            clientInfoTable.AddCell(CreateInfoCell("المستخدم:", headerFont, true));
            clientInfoTable.AddCell(CreateInfoCell(userFullName, normalFont, true));
            clientInfoTable.AddCell(CreateInfoCell("عدد المعاملات:", headerFont, true));
            clientInfoTable.AddCell(CreateInfoCell(transactions.Count.ToString(), normalFont, false));
            
            clientInfoTable.SpacingAfter = 20;
            document.Add(clientInfoTable);
            
            // الإجماليات
            var totalDebt = transactions.Where(t => t.Type == TransactionType.Debt).Sum(t => t.Amount);
            var totalPayment = transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount);
            var netBalance = totalDebt - totalPayment;
            
            var summaryTable = new PdfPTable(3) { WidthPercentage = 100 };
            summaryTable.SetWidths(new float[] { 1, 1, 1 });
            summaryTable.RunDirection = PdfWriter.RUN_DIRECTION_RTL;
            
            var debtText = $"إجمالي ليك\n{totalDebt:N2} ج.م";
            var debtCell = new PdfPCell(new Phrase(debtText, new iTextSharp.text.Font(arabicFont, 12, iTextSharp.text.Font.BOLD, new BaseColor(231, 76, 60))))
            {
                BackgroundColor = new BaseColor(253, 237, 236), // Light Red
                HorizontalAlignment = Element.ALIGN_CENTER,
                Padding = 15,
                Border = iTextSharp.text.Rectangle.BOX,
                BorderColor = new BaseColor(231, 76, 60),
                BorderWidth = 2,
                RunDirection = PdfWriter.RUN_DIRECTION_RTL
            };
            summaryTable.AddCell(debtCell);
            
            var paymentText = $"إجمالي عليك\n{totalPayment:N2} ج.م";
            var paymentCell = new PdfPCell(new Phrase(paymentText, new iTextSharp.text.Font(arabicFont, 12, iTextSharp.text.Font.BOLD, new BaseColor(39, 174, 96))))
            {
                BackgroundColor = new BaseColor(232, 246, 243), // Light Green
                HorizontalAlignment = Element.ALIGN_CENTER,
                Padding = 15,
                Border = iTextSharp.text.Rectangle.BOX,
                BorderColor = new BaseColor(39, 174, 96),
                BorderWidth = 2,
                RunDirection = PdfWriter.RUN_DIRECTION_RTL
            };
            summaryTable.AddCell(paymentCell);
            
            var balanceColor = netBalance > 0 ? new BaseColor(253, 237, 236) : 
                              netBalance < 0 ? new BaseColor(232, 246, 243) : 
                              new BaseColor(236, 240, 241);
            var borderColor = netBalance > 0 ? new BaseColor(231, 76, 60) : 
                             netBalance < 0 ? new BaseColor(39, 174, 96) : 
                             new BaseColor(149, 165, 166);
            var textColor = netBalance > 0 ? new BaseColor(231, 76, 60) : 
                           netBalance < 0 ? new BaseColor(39, 174, 96) : 
                           new BaseColor(52, 73, 94);
            
            var balanceStatus = netBalance > 0 ? "ليك فلوس" : netBalance < 0 ? "عليك فلوس" : "متسوي";
            var balanceText = $"الحالة النهائية\n{balanceStatus}\n{Math.Abs(netBalance):N2} ج.م";
            
            var balanceCell = new PdfPCell(new Phrase(balanceText, new iTextSharp.text.Font(arabicFont, 12, iTextSharp.text.Font.BOLD, textColor)))
            {
                BackgroundColor = balanceColor,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Padding = 15,
                Border = iTextSharp.text.Rectangle.BOX,
                BorderColor = borderColor,
                BorderWidth = 2,
                RunDirection = PdfWriter.RUN_DIRECTION_RTL
            };
            summaryTable.AddCell(balanceCell);
            
            summaryTable.SpacingAfter = 25;
            document.Add(summaryTable);
            
            // عنوان جدول المعاملات في جدول منفصل
            var titleTable = new PdfPTable(1) { WidthPercentage = 100 };
            var transactionTitleCell = new PdfPCell(new Phrase("تفاصيل المعاملات", new iTextSharp.text.Font(arabicFont, 14, iTextSharp.text.Font.BOLD, new BaseColor(44, 62, 80))))
            {
                HorizontalAlignment = Element.ALIGN_CENTER,
                Padding = 12,
                Border = iTextSharp.text.Rectangle.NO_BORDER,
                BackgroundColor = new BaseColor(236, 240, 241),
                RunDirection = PdfWriter.RUN_DIRECTION_RTL
            };
            titleTable.AddCell(transactionTitleCell);
            titleTable.SpacingAfter = 15;
            document.Add(titleTable);
            
            // جدول المعاملات
            var transactionTable = new PdfPTable(5) { WidthPercentage = 100 };
            transactionTable.SetWidths(new float[] { 1, 2, 2, 2, 3 });
            transactionTable.RunDirection = PdfWriter.RUN_DIRECTION_RTL;
            
            // رؤوس الأعمدة
            var headers = new[] { "#", "التاريخ", "النوع", "المبلغ", "الملاحظات" };
            foreach (var header in headers)
            {
                var headerCell2 = new PdfPCell(new Phrase(header, new iTextSharp.text.Font(arabicFont, 11, iTextSharp.text.Font.BOLD, new BaseColor(255, 255, 255))))
                {
                    BackgroundColor = new BaseColor(52, 73, 94), // Dark Gray-Blue
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    Padding = 12,
                    Border = iTextSharp.text.Rectangle.BOX,
                    BorderColor = new BaseColor(44, 62, 80),
                    BorderWidth = 1,
                    RunDirection = PdfWriter.RUN_DIRECTION_RTL
                };
                transactionTable.AddCell(headerCell2);
            }
            
            // البيانات
            var counter = 1;
            foreach (var transaction in transactions.OrderBy(t => t.Date))
            {
                var rowColor = counter % 2 == 0 ? new BaseColor(250, 250, 250) : new BaseColor(255, 255, 255);
                var typeColor = transaction.Type == TransactionType.Debt ? 
                               new BaseColor(254, 245, 245) : new BaseColor(245, 251, 248);
                
                transactionTable.AddCell(CreateDataCell(counter.ToString(), normalFont, rowColor, false));
                transactionTable.AddCell(CreateDataCell(transaction.Date.ToString("yyyy/MM/dd"), normalFont, rowColor, false));
                
                var typeText = transaction.Type == TransactionType.Debt ? "ليك (مديون)" : "عليك (دفعة)";
                var typeFont = new iTextSharp.text.Font(arabicFont, 10, iTextSharp.text.Font.BOLD, 
                    transaction.Type == TransactionType.Debt ? new BaseColor(231, 76, 60) : new BaseColor(39, 174, 96));
                transactionTable.AddCell(CreateDataCellWithFont(typeText, typeFont, typeColor, true));
                
                transactionTable.AddCell(CreateDataCell($"{transaction.Amount:N2} ج.م", normalFont, rowColor, false));
                transactionTable.AddCell(CreateDataCell(transaction.Notes ?? "-", normalFont, rowColor, true));
                
                counter++;
            }
            
            document.Add(transactionTable);
            
            // Footer باستخدام جدول بدلاً من Paragraph
            var footerTable = new PdfPTable(1) { WidthPercentage = 100 };
            var footerText = $"تم إنشاء التقرير في: {DateTime.Now:yyyy/MM/dd HH:mm} | بواسطة: {userFullName}";
            var footerCell = new PdfPCell(new Phrase(footerText, smallFont))
            {
                HorizontalAlignment = Element.ALIGN_CENTER,
                Border = iTextSharp.text.Rectangle.NO_BORDER,
                Padding = 15,
                BackgroundColor = new BaseColor(248, 249, 250),
                RunDirection = PdfWriter.RUN_DIRECTION_RTL
            };
            footerTable.AddCell(footerCell);
            footerTable.SpacingBefore = 30;
            document.Add(footerTable);
            
            document.Close();
            return stream.ToArray();
        }

        private PdfPCell CreateInfoCell(string text, iTextSharp.text.Font font, bool isArabic)
        {
            return new PdfPCell(new Phrase(text, font))
            {
                BackgroundColor = new BaseColor(250, 250, 250),
                Padding = 10,
                Border = iTextSharp.text.Rectangle.BOX,
                BorderColor = new BaseColor(189, 195, 199),
                BorderWidth = 1,
                RunDirection = isArabic ? PdfWriter.RUN_DIRECTION_RTL : PdfWriter.RUN_DIRECTION_LTR,
                HorizontalAlignment = isArabic ? Element.ALIGN_RIGHT : Element.ALIGN_CENTER
            };
        }

        private PdfPCell CreateDataCell(string text, iTextSharp.text.Font font, BaseColor backgroundColor, bool isArabic)
        {
            return new PdfPCell(new Phrase(text, font))
            {
                BackgroundColor = backgroundColor,
                Padding = 10,
                Border = iTextSharp.text.Rectangle.BOX,
                BorderColor = new BaseColor(189, 195, 199),
                BorderWidth = 0.5f,
                HorizontalAlignment = Element.ALIGN_CENTER,
                RunDirection = isArabic ? PdfWriter.RUN_DIRECTION_RTL : PdfWriter.RUN_DIRECTION_LTR
            };
        }

        private PdfPCell CreateDataCellWithFont(string text, iTextSharp.text.Font font, BaseColor backgroundColor, bool isArabic)
        {
            return new PdfPCell(new Phrase(text, font))
            {
                BackgroundColor = backgroundColor,
                Padding = 10,
                Border = iTextSharp.text.Rectangle.BOX,
                BorderColor = new BaseColor(189, 195, 199),
                BorderWidth = 0.5f,
                HorizontalAlignment = Element.ALIGN_CENTER,
                RunDirection = isArabic ? PdfWriter.RUN_DIRECTION_RTL : PdfWriter.RUN_DIRECTION_LTR
            };
        }

        public byte[] ExportAllClientsToExcel(List<Client> clients, string userFullName)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("تقرير جميع العملاء");
            
            worksheet.View.RightToLeft = true;
            
            // العنوان الرئيسي
            worksheet.Cells["A1:G1"].Merge = true;
            worksheet.Cells["A1"].Value = "تقرير جميع العملاء";
            worksheet.Cells["A1"].Style.Font.Size = 16;
            worksheet.Cells["A1"].Style.Font.Bold = true;
            worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells["A1"].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillPatternType.Solid;
            worksheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(79, 129, 189));
            worksheet.Cells["A1"].Style.Font.Color.SetColor(Color.White);
            
            // معلومات التقرير
            worksheet.Cells["A3"].Value = "تاريخ التقرير:";
            worksheet.Cells["B3"].Value = DateTime.Now.ToString("yyyy/MM/dd");
            worksheet.Cells["A4"].Value = "المستخدم:";
            worksheet.Cells["B4"].Value = userFullName;
            worksheet.Cells["A5"].Value = "عدد العملاء:";
            worksheet.Cells["B5"].Value = clients.Count;
            
            // رؤوس الأعمدة
            var headers = new[] { "اسم العميل", "رقم الهاتف", "البريد الإلكتروني", "عدد المعاملات", "إجمالي ليك", "إجمالي عليك", "الرصيد النهائي" };
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[7, i + 1].Value = headers[i];
                worksheet.Cells[7, i + 1].Style.Font.Bold = true;
                worksheet.Cells[7, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillPatternType.Solid;
                worksheet.Cells[7, i + 1].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                worksheet.Cells[7, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
            }
            
            // البيانات
            for (int i = 0; i < clients.Count; i++)
            {
                var client = clients[i];
                var row = i + 8;
                
                var totalDebt = client.Transactions?.Where(t => t.Type == TransactionType.Debt).Sum(t => t.Amount) ?? 0;
                var totalPayment = client.Transactions?.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount) ?? 0;
                var netBalance = totalDebt - totalPayment;
                
                worksheet.Cells[row, 1].Value = client.Name;
                worksheet.Cells[row, 2].Value = client.PhoneNumber;
                worksheet.Cells[row, 3].Value = client.Email ?? "";
                worksheet.Cells[row, 4].Value = client.Transactions?.Count ?? 0;
                worksheet.Cells[row, 5].Value = totalDebt;
                worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00 ج.م";
                worksheet.Cells[row, 6].Value = totalPayment;
                worksheet.Cells[row, 6].Style.Numberformat.Format = "#,##0.00 ج.م";
                worksheet.Cells[row, 7].Value = Math.Abs(netBalance);
                worksheet.Cells[row, 7].Style.Numberformat.Format = "#,##0.00 ج.م";
                
                // تلوين حسب الحالة
                var rowColor = netBalance > 0 ? Color.FromArgb(255, 235, 235) : 
                              netBalance < 0 ? Color.FromArgb(235, 255, 235) : 
                              Color.FromArgb(240, 240, 240);
                              
                for (int col = 1; col <= 7; col++)
                {
                    worksheet.Cells[row, col].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillPatternType.Solid;
                    worksheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(rowColor);
                    worksheet.Cells[row, col].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                }
            }
            
            // تنسيق الأعمدة
            for (int col = 1; col <= 7; col++)
            {
                worksheet.Column(col).AutoFit();
            }
            
            return package.GetAsByteArray();
        }
    }
}



