namespace TranscriptHelper.Core.Models;

public sealed record ActionItem(
    Guid Id,
    string ParticipantId,
    string Description,
    DateTime? DueDate,
    string? Status,
    Guid SourceSegmentId);
