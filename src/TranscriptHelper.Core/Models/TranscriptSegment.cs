namespace TranscriptHelper.Core.Models;

public sealed record TranscriptSegment(
    Guid Id,
    string ParticipantId,
    DateTimeOffset Timestamp,
    string Text,
    bool IsInterim);
