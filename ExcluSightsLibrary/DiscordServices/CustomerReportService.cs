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
        public bool SaveToLocal { get; init; } = false;
        public string? LocalDirectory { get; init; }
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

        public CustomerReportService(ICustomerQueryService customerQuery, IEmailSender email, IGoogleSheetsClient sheets, ILogger<CustomerReportService> log)
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
            foreach (var row in rows)
            {
                table.Add(new object[]
                {
                row.CustomerId,
                row.DiscordId.ToString(),
                row.DiscordTag,
                row.CustomerFirstName ?? "",
                row.CustomerLastName ?? "",
                (int?)row.Gender,
                (double?)row.ShoeSize,
                row.Interests
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

            if (opts.SaveToLocal && fileBytes != null && fileName != null)
            {
                try
                {
                    // default path: ./reports/
                    var baseDir = opts.LocalDirectory ?? Path.Combine(AppContext.BaseDirectory, "reports");
                    if (!Directory.Exists(baseDir))
                        Directory.CreateDirectory(baseDir);

                    var filePath = Path.Combine(baseDir, fileName);

                    await File.WriteAllBytesAsync(filePath, fileBytes, ct);
                    _log.LogInformation("Saved report locally: {Path}", filePath);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to save report locally.");
                }
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
        private static (byte[] Bytes, string ContentType, string FileName) BuildCsv(IReadOnlyList<IReadOnlyList<object>> table, string fileName)
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

            static string EscapeCsv(object currentObject)
            {
                var currentString = currentObject?.ToString() ?? "";
                if (currentString.Contains('"') || currentString.Contains(',') || currentString.Contains('\n') || currentString.Contains('\r'))
                    currentString = "\"" + currentString.Replace("\"", "\"\"") + "\"";
                return currentString;
            }
        }
        private static (byte[] Bytes, string ContentType, string FileName) BuildExcel(IReadOnlyList<IReadOnlyList<object?>> table, string fileName)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Report");

            for (int i = 0; i < table.Count; i++)
            {
                var cols = table[i];
                for (int j = 0; j < cols.Count; j++)
                {
                    var cell = ws.Cell(i + 1, j + 1); // i is the 0-based row index
                    WriteCell(cell, cols[j], j);  // j is the 0-based column index
                }
            }

            var used = ws.RangeUsed();
            if (used != null)
            {
                used.SetAutoFilter();
                ws.Columns().AdjustToContents();
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return (ms.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
        }
        private static void WriteCell(IXLCell cell, object? value, int columnIndex)
        {
            if (columnIndex == 1)
            {
                cell.SetValue(value?.ToString() ?? string.Empty);
                cell.Style.NumberFormat.Format = "@"; // text format
                return;
            }

            if (value is null)
            {
                cell.SetValue(string.Empty);
                return;
            }

            switch (value)
            {
                case string s:
                    cell.SetValue(s);
                    break;
                case int i:
                    cell.SetValue(i);
                    break;
                case long l:
                    cell.SetValue(l);
                    break;
                case double d:
                    cell.SetValue(d);
                    break;
                case bool b:
                    cell.SetValue(b);
                    break;
                case DateTime dt:
                    cell.SetValue(dt);
                    cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                    break;
                case DateTimeOffset dto:
                    cell.SetValue(dto.UtcDateTime);
                    cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                    break;
                case Enum e:
                    cell.SetValue(e.ToString());
                    break;
                default:
                    cell.SetValue(value.ToString() ?? string.Empty);
                    break;
            }
        }
    }
}
