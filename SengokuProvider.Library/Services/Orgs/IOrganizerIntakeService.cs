namespace SengokuProvider.Library.Services.Orgs
{
    public interface IOrganizerIntakeService
    {
        public Task StartBracketRun(int[] tournamentIds, int userId);
        public Task AddBracketToRun(int[] tournamentIds, int userId);
        public Task UpdateBracketFromCurrentRun(int[] tournamentIds, int userId);
        public Task DeleteBracketFromCurrentRun(int tournamentIds, int userId);
    }
}