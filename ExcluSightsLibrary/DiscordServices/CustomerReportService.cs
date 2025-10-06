using ClosedXML.Excel;
using ExcluSightsLibrary.DiscordModels;
using Microsoft.Extensions.Logging;
using System.Text;

namespace ExcluSightsLibrary.DiscordServices
{
    public enum ReportFormat { Excel, Csv, GoogleSheets }
    public sealed class GenerateReportOptions
    {
        public ReportFormat Format { get; init; } = ReportFormat.Excel;
        public string? EmailTo { get; init; }
        public bool UploadToGoogleSheets { get; init; } = false;
        public string? GoogleSpreadsheetId { get; init; }
        public string SheetTitle { get; init; } = "Customer Report";
    }

    public interface ICustomerReportService
    {
        Task<(byte[]? File, string? ContentType, string? FileName, string? GoogleSheetUrl)> GenerateCustomerReportAsync(ulong guildId,
            GenerateReportOptions options, CancellationToken ct = default);
    }

    public sealed class CustomerReportService : ICustomerReportService
    {
        private readonly ICustomerQueryService _customerQuery;
        private readonly IEmailSender _email;
        private readonly IGoogleSheetsClient _sheets;
        private readonly ILogger<CustomerReportService> _log;

        public CustomerReportService(
            ICustomerQueryService customerQuery,
            IEmailSender email,
            IGoogleSheetsClient sheets,
            ILogger<CustomerReportService> log)
        {
            _customerQuery = customerQuery;
            _email = email;
            _sheets = sheets;
            _log = log;
        }
        public async Task<(byte[]? File, string? ContentType, string? FileName, string? GoogleSheetUrl)>
            GenerateCustomerReportAsync(ulong guildId, GenerateReportOptions opts, CancellationToken ct = default)
        {
            IReadOnlyList<SolePlayDTO> rows = (IReadOnlyList<SolePlayDTO>)await _customerQuery.GetCustomersDataByGuildId(guildId, ct);
            if (rows is null || rows.Count == 0)
                return (null, null, null, null);

            // Build table (header + values) for reuse across CSV/Excel/Sheets
            var header = new object[]
            {
            "CustomerId","DiscordId","DiscordTag","FirstName","LastName","Gender","ShoeSize","Interests"
            };

            var table = new List<IReadOnlyList<object>>(rows.Count + 1) { header };
            foreach (var r in rows)
            {
                table.Add(new object[]
                {
                r.CustomerId,
                r.DiscordId.ToString(),
                r.DiscordTag,
                r.CustomerFirstName ?? "",
                r.CustomerLastName ?? "",
                r.Gender.ToString() ?? "",
                r.ShoeSize.ToString() ?? "",
                r.Interests
                });
            }

            byte[]? fileBytes = null;
            string? contentType = null;
            string? fileName = null;
            string? googleUrl = null;

            // Create file if asked (Excel or CSV)
            if (opts.Format == ReportFormat.Excel)
            {
                (fileBytes, contentType, fileName) = BuildExcel(table, $"Guild-{guildId}-Report.xlsx");
            }
            else
            {
                (fileBytes, contentType, fileName) = BuildCsv(table, $"Guild-{guildId}-Report.csv");
            }

            // Upload to Google Sheets if requested
            if (opts.UploadToGoogleSheets)
            {
                googleUrl = await _sheets.UploadTableAsync(
                    opts.GoogleSpreadsheetId ?? "",
                    opts.SheetTitle,
                    table,
                    ct);
            }

            // Email if provided
            if (!string.IsNullOrWhiteSpace(opts.EmailTo))
            {
                var subject = $"Customer Report for Guild {guildId}";
                var linkNote = googleUrl is null ? "" : $"<p>Google Sheet: <a href=\"{googleUrl}\">{googleUrl}</a></p>";
                var body = $"<p>Attached is your report.</p>{linkNote}";

                (string FileName, string ContentType, byte[] Bytes)? attach = null;
                if (fileBytes != null && contentType != null && fileName != null)
                    attach = (fileName, contentType, fileBytes);

                await _email.SendAsync(opts.EmailTo, subject, body, attach, ct);
            }

            return (fileBytes, contentType, fileName, googleUrl);
        }
        private static (byte[] Bytes, string ContentType, string FileName) BuildCsv(
            IReadOnlyList<IReadOnlyList<object>> table, string fileName)
        {
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)); // BOM for Excel
            for (int i = 0; i < table.Count; i++)
            {
                var line = string.Join(",", table[i].Select(EscapeCsv));
                writer.WriteLine(line);
            }
            writer.Flush();
            return (ms.ToArray(), "text/csv", fileName);

            static string EscapeCsv(object o)
            {
                var s = o?.ToString() ?? "";
                if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                    s = "\"" + s.Replace("\"", "\"\"") + "\"";
                return s;
            }
        }
        private static (byte[] Bytes, string ContentType, string FileName) BuildExcel(
            IReadOnlyList<IReadOnlyList<object>> table, string fileName)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Report");

            for (int i = 0; i < table.Count; i++)
            {
                var row = ws.Row(i + 1);
                var cols = table[i];
                for (int c = 0; c < cols.Count; c++)
                    row.Cell(c + 1).Value = (XLCellValue)(cols[c] ?? "");
            }
            ws.RangeUsed().SetAutoFilter();
            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return (ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
    }
}
