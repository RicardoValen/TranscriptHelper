namespace TranscriptHelper.Core;

using TranscriptHelper.Core.Meeting;
using TranscriptHelper.Core.Models;
using TranscriptHelper.Core.Speaker;

public sealed record TranscriptEntry(Guid Id, DateTimeOffset Timestamp, string Speaker, string Text);
public sealed record MeetingSnapshot(Guid Id, string Title, bool IsEnabled, IReadOnlyList<TranscriptEntry> Entries);
public sealed record MeetingInsight(string Kind, string Speaker, string Text, Guid SourceEntryId);
public sealed record AssistantAnswer(string Text, IReadOnlyList<Guid> SourceEntryIds);

// Provider implementations receive a snapshot, never a mutable meeting session.
public interface IMeetingAssistant
{
    Task<AssistantAnswer> AskAsync(MeetingSnapshot meeting, string question, CancellationToken cancellationToken = default);
}

public sealed class MeetingSession(string title, ISpeakerDiarizer? diarizer = null)
{
    private readonly object gate = new();
    private MeetingContext? context;
    private readonly Dictionary<string, string> speakerToParticipantId = [];
    private readonly ISpeakerDiarizer diarizer = diarizer ?? new NullSpeakerDiarizer();
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; } = string.IsNullOrWhiteSpace(title) ? "Untitled meeting" : title.Trim();
    private bool enabled;

    public void SetEnabled(bool value) { lock (gate) enabled = value; }
    
    public bool TryAppend(string speaker, string text, out TranscriptEntry? entry)
    {
        if (string.IsNullOrWhiteSpace(speaker)) throw new ArgumentException("Speaker is required.", nameof(speaker));
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Text is required.", nameof(text));
        lock (gate)
        {
            entry = null;
            if (!enabled) return false;
            
            context ??= new MeetingContext(Id.ToString());
            
            // Map speaker name to participant ID (auto-create if needed)
            if (!speakerToParticipantId.TryGetValue(speaker, out var participantId))
            {
                participantId = Guid.NewGuid().ToString();
                speakerToParticipantId[speaker] = participantId;
                var participant = new Participant(
                    participantId, 
                    speaker, 
                    speaker == "You", 
                    DateTimeOffset.UtcNow, 
                    DateTimeOffset.UtcNow);
                context.Participants.Add(participant);
                diarizer.OnParticipantChanged(participant, added: true);
            }
            
            // Create segment and add to context
            var segment = new TranscriptSegment(
                Guid.NewGuid(),
                participantId,
                DateTimeOffset.UtcNow,
                text.Trim(),
                IsInterim: false);
            
            context.AppendSegment(segment);
            
            // Convert to old entry type for backward compatibility
            entry = new TranscriptEntry(segment.Id, segment.Timestamp, speaker, segment.Text);
            return true;
        }
    }
    
    public MeetingSnapshot Snapshot()
    {
        lock (gate)
        {
            if (context?.GetRecentTranscript() is { } segments && segments.Length > 0)
            {
                var entries = segments.Select(s => 
                {
                    var participant = context.Participants.FirstOrDefault(p => p.Id == s.ParticipantId);
                    var displayName = participant?.DisplayName ?? "Unknown";
                    return new TranscriptEntry(s.Id, s.Timestamp, displayName, s.Text);
                }).ToArray();
                
                return new(Id, Title, enabled, Array.AsReadOnly(entries));
            }
            return new(Id, Title, enabled, Array.Empty<TranscriptEntry>());
        }
    }

    public MeetingContext? GetContext()
    {
        lock (gate) return context;
    }
}

/// <summary>
/// Null implementation of diarizer that just passes through speaker names.
/// Used as default; can be replaced with real diarization at runtime.
/// </summary>
internal sealed class NullSpeakerDiarizer : ISpeakerDiarizer
{
    public void OnParticipantChanged(Participant participant, bool added) { }
    public Task<string?> IdentifySpeakerAsync(float[] audioSamples, IReadOnlyList<Participant> knownParticipants, CancellationToken ct = default) 
        => Task.FromResult<string?>(null);
}

// Explicit markers make the demo predictable; this is not natural-language inference.
public static class InsightExtractor
{
    public static IReadOnlyList<MeetingInsight> Extract(MeetingSnapshot snapshot) =>
        snapshot.Entries.SelectMany(entry =>
            new[] { "Decision:", "Action:", "Question:" }
                .Where(marker => entry.Text.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                .Select(marker => new MeetingInsight(marker.TrimEnd(':'), entry.Speaker,
                    entry.Text[marker.Length..].Trim(), entry.Id))).ToArray();
}
