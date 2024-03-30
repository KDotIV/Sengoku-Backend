namespace SengokuProvider.API.Models.Common
{
    public class CreateTableCommand : ICommand
    {
        public required string TableName { get; set; }
        public Tuple<string, string>[]? TableDefinitions { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            return !string.IsNullOrEmpty(TableName) &&
                TableDefinitions != null &&
                TableDefinitions.Length > 0;
        }
    }
}
