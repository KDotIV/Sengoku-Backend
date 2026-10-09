namespace SengokuProvider.Library.Models.User;

/// <summary>Local identities and their distinct start.gg identities.</summary>
public sealed record StartggProfileLink(int UserId, int PlayerId, int StartggUserId, int StartggPlayerId, string Slug);
