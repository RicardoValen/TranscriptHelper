namespace TranscriptHelper.Core.Models;

public sealed record Participant(
    string Id,
    string DisplayName,
    bool IsSelf,
    DateTimeOffset JoinedAt,
    DateTimeOffset LastActive);
