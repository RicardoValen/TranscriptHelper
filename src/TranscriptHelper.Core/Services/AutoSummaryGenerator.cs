using TranscriptHelper.Core.Meeting;
using TranscriptHelper.Core.Models;

namespace TranscriptHelper.Core.Services;

/// <summary>
/// Generates rolling summaries of transcript segments to enable efficient AI context building.
/// Triggered after every N new entries to reduce token usage in long meetings.
/// </summary>
public sealed class AutoSummaryGenerator
{
    private readonly IMeetingAssistant assistant;
    private readonly MeetingContext context;
    private readonly int entriesBetweenSummary;
    private int lastSummarizedCount;

    /// <summary>
    /// Creates a new auto-summary generator.
    /// </summary>
    /// <param name="assistant">AI provider for generating summaries.</param>
    /// <param name="context">Meeting context to track and store summaries.</param>
    /// <param name="entriesBetweenSummary">Generate summary after this many new entries (default 50).</param>
    public AutoSummaryGenerator(IMeetingAssistant assistant, MeetingContext context, int entriesBetweenSummary = 50)
    {
        this.assistant = assistant;
        this.context = context;
        this.entriesBetweenSummary = entriesBetweenSummary;
        this.lastSummarizedCount = 0;
    }

    /// <summary>
    /// Analyzes recent entries and generates a summary if threshold is reached.
    /// Call this after each new transcript entry is added.
    /// </summary>
    public async Task TryGenerateSummaryAsync(CancellationToken ct = default)
    {
        var recent = context.GetRecentTranscript();
        int currentCount = recent.Length + lastSummarizedCount;  // Approximation of total entries seen

        if (currentCount - lastSummarizedCount < entriesBetweenSummary)
            return;  // Not enough new entries yet

        try
        {
            var summary = await GenerateSummaryAsync(recent, ct);
            if (summary != null)
            {
                context.AddSummary(summary);
                lastSummarizedCount = currentCount;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Summary generation failed: {ex.Message}");
            // Don't crash; summaries are nice-to-have optimization
        }
    }

    private async Task<RollingSummary?> GenerateSummaryAsync(TranscriptSegment[] recentSegments, CancellationToken ct)
    {
        if (recentSegments.Length == 0)
            return null;

        // Extract key participants from recent entries
        var participants = new HashSet<string>();
        var segmentTexts = new List<string>();
        foreach (var seg in recentSegments)
        {
            participants.Add(seg.ParticipantId);
            segmentTexts.Add(seg.Text);
        }

        // Build a concise prompt asking for summary of key points
        var transcript = string.Join(" ", segmentTexts);
        if (transcript.Length > 1000)
            transcript = transcript.Substring(0, 1000) + "...";

        var prompt = $@"Summarize the key points, decisions, and action items from this meeting segment in 2-3 sentences:
{transcript}

Focus on: decisions made, action items assigned, blockers identified.";

        try
        {
            // Convert recent segments to MeetingSnapshot for AI provider
            var entries = recentSegments.Select(s =>
            {
                var participant = context.Participants.FirstOrDefault(p => p.Id == s.ParticipantId);
                var displayName = participant?.DisplayName ?? "Unknown";
                return new TranscriptEntry(s.Id, s.Timestamp, displayName, s.Text);
            }).ToArray();

            var meetingId = Guid.TryParse(context.MeetingId, out var id) ? id : Guid.NewGuid();
            var snapshot = new MeetingSnapshot(meetingId, context.MeetingId, true, Array.AsReadOnly(entries));

            var answer = await assistant.AskAsync(snapshot, prompt, ct);
            
            return new RollingSummary(
                Timestamp: DateTimeOffset.UtcNow,
                EntriesCount: recentSegments.Length,
                SummaryText: answer.Text,
                KeyParticipants: participants.ToList().AsReadOnly()
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to generate summary via AI: {ex.Message}");
            
            // Fallback: marker-based quick summary if AI fails
            return GenerateQuickSummaryFallback(recentSegments, participants);
        }
    }

    private RollingSummary GenerateQuickSummaryFallback(TranscriptSegment[] segments, HashSet<string> participants)
    {
        // Quick extraction of marked items
        var actions = segments.Where(s => s.Text.Contains("Action:", StringComparison.OrdinalIgnoreCase)).Count();
        var decisions = segments.Where(s => s.Text.Contains("Decision:", StringComparison.OrdinalIgnoreCase)).Count();
        var blockers = segments.Where(s => s.Text.Contains("Blocker:", StringComparison.OrdinalIgnoreCase)).Count();

        var summary = $"{segments.Length} entries: {actions} actions, {decisions} decisions, {blockers} blockers";
        
        return new RollingSummary(
            Timestamp: DateTimeOffset.UtcNow,
            EntriesCount: segments.Length,
            SummaryText: summary,
            KeyParticipants: participants.ToList().AsReadOnly()
        );
    }
}
