namespace SengokuProvider.API.Models.Common
{
    public class CreateTableCommand
    {
        public required string TableName { get; set; }
        public Tuple<string, string>[]? TableDefinitions { get; set; }
        public string? Response { get; set; }
    }
}
