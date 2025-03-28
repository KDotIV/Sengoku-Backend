﻿namespace SengokuProvider.Library.Models.Common
{
    public enum CommandRegistry
    {
        //Event Commands
        UpdateEvent = 101,
        LinkTournamentByEvent = 102,
        IntakeEventsByLocation = 103,
        IntakeEventsByGames = 104,
        GetTournamentByLocation = 105,
        //Player Commands
        UpdatePlayer = 201,
        OnboardPlayerData = 202,
        IntakePlayersByTournament = 203,
        QueryPlayerStandingsCommand = 204,
        //Legend Commands
        UpdateLegend = 301,
        OnboardLegendsByPlayerData = 302,
        IntakeLegendsByTournament = 303,
        OnboardTournamentToLeague = 304,
        OnboardPlayerToLeague = 305,
        //User Commands
        CreateNewUser = 401,
    }
}
