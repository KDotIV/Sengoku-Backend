using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace ExcluSightsLibrary.DiscordServices
{
    public sealed class GoogleSheetsClient : IGoogleSheetsClient
    {
        private readonly SheetsService _svc;

        public GoogleSheetsClient(SheetsService svc)
        {
            _svc = svc;
        }
        public async Task<string> UploadTableAsync(string spreadsheetId, string sheetTitle, IReadOnlyList<IReadOnlyList<object>> rows, CancellationToken ct = default)
        {
            // Create/new sheet if no spreadsheetId
            if (string.IsNullOrWhiteSpace(spreadsheetId))
            {
                var createReq = new Google.Apis.Sheets.v4.Data.Spreadsheet
                {
                    Properties = new Google.Apis.Sheets.v4.Data.SpreadsheetProperties { Title = sheetTitle }
                };
                var created = await _svc.Spreadsheets.Create(createReq).ExecuteAsync(ct);
                spreadsheetId = created.SpreadsheetId;
            }

            // Clear or create the sheet tab
            var batch = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request>
            {
                new Request
                {
                    AddSheet = new AddSheetRequest
                    {
                        Properties = new SheetProperties{ Title = sheetTitle }
                    }
                }
            }
            };
            try
            {
                await _svc.Spreadsheets.BatchUpdate(batch, spreadsheetId).ExecuteAsync(ct);
            }
            catch
            {
                // If the sheet exists, clear it
                var clear = new ClearValuesRequest();
                await _svc.Spreadsheets.Values.Clear(clear, spreadsheetId, $"{sheetTitle}!A:Z").ExecuteAsync(ct);
            }

            // Write values
            var vr = new ValueRange { Values = (IList<IList<object>>)rows.Select(r => r.ToList()).ToList() };
            var upd = _svc.Spreadsheets.Values.Update(vr, spreadsheetId, $"{sheetTitle}!A1");
            upd.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
            await upd.ExecuteAsync(ct);

            return $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit#gid=0";
        }
    }
}
