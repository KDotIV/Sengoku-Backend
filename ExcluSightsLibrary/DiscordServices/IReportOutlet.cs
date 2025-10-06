namespace ExcluSightsLibrary.DiscordServices
{
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string htmlBody,
            (string FileName, string ContentType, byte[] Bytes)? attachment = null, CancellationToken ct = default);
    }
    public interface IGoogleSheetsClient
    {
        Task<string> UploadTableAsync(string spreadsheetId, string sheetTitle, IReadOnlyList<IReadOnlyList<object>> rows, CancellationToken ct = default);
    }
}
