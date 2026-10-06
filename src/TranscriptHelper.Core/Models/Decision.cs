namespace TranscriptHelper.Core.Models;

public sealed record Decision(
    Guid Id,
    string ParticipantId,
    string Description,
    string? Rationale,
    DateTimeOffset DecidedAt,
    Guid SourceSegmentId);
