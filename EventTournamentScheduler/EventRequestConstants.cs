namespace EventTournamentScheduler
{
    public static class EventRequestConstants
    {
        public static string[] StateCodes =
        [
            "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA", "HI", "ID",
            "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS",
            "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH", "OK",
            "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV",
            "WI", "WY"
        ];
        public static string[] Filters = ["addrState: $state"];
        public static string[] VariableDefinitions = ["$perPage: Int", "$state: String!"];
        public const string sengokuBaseUrl = "https://sengoku-alexandria-qa.azurewebsites.net";
    }
}