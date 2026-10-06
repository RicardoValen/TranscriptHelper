namespace TranscriptHelper.Core.Models;

public sealed record OpenQuestion(
    Guid Id,
    string Question,
    string AskedByParticipantId,
    string? Answer,
    Guid SourceSegmentId);
