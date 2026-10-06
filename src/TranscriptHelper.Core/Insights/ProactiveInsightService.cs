using TranscriptHelper.Core.Models;
using TranscriptHelper.Core.Meeting;

namespace TranscriptHelper.Core.Insights;

/// <summary>
/// Analyzes meeting transcript in real-time to extract and proactively surface:
/// - Blockers (impediments to progress)
/// - Decisions (commitments made)
/// - Action items (tasks assigned)
/// - Open questions (unresolved issues)
/// </summary>
public sealed class ProactiveInsightService
{
    private readonly IMeetingAssistant assistant;
    private readonly MeetingContext context;
    private int lastProcessedSegmentCount = 0;

    public event EventHandler<InsightDetectedEventArgs>? InsightDetected;

    public ProactiveInsightService(IMeetingAssistant assistant, MeetingContext context)
    {
        this.assistant = assistant;
        this.context = context;
    }

    /// <summary>
    /// Analyzes recent segments to extract insights.
    /// Performs quick keyword-based extraction first, then optionally deeper AI analysis.
    /// </summary>
    public async Task AnalyzeRecentSegmentsAsync(CancellationToken ct = default)
    {
        var recentSegments = context.GetRecentTranscript();
        if (recentSegments.Length == lastProcessedSegmentCount)
            return;  // No new segments

        var newSegments = recentSegments.Skip(lastProcessedSegmentCount).ToList();
        lastProcessedSegmentCount = recentSegments.Length;

        // Step 1: Quick marker-based extraction
        foreach (var insight in ExtractMarkedInsights(newSegments))
        {
            if (insight is ActionItem action)
                context.ActionItems.Add(action);
            else if (insight is Decision decision)
                context.Decisions.Add(decision);
            
            InsightDetected?.Invoke(this, new InsightDetectedEventArgs(insight));
        }

        // Step 2: Deeper AI analysis if meeting has meaningful exchanges
        if (ShouldInvokeDeepAnalysis(newSegments))
        {
            await PerformDeepAnalysisAsync(newSegments, ct);
        }
    }

    private List<object> ExtractMarkedInsights(IReadOnlyList<TranscriptSegment> segments)
    {
        var insights = new List<object>();

        foreach (var segment in segments)
        {
            // Action item: explicit "Action:" prefix
            if (segment.Text.StartsWith("Action:", StringComparison.OrdinalIgnoreCase))
            {
                insights.Add(new ActionItem(
                    Guid.NewGuid(),
                    segment.ParticipantId,
                    segment.Text["Action:".Length..].Trim(),
                    DueDate: null,
                    Status: "pending",
                    segment.Id));
            }
            // Decision: explicit "Decision:" prefix
            else if (segment.Text.StartsWith("Decision:", StringComparison.OrdinalIgnoreCase))
            {
                insights.Add(new Decision(
                    Guid.NewGuid(),
                    segment.ParticipantId,
                    segment.Text["Decision:".Length..].Trim(),
                    Rationale: null,
                    segment.Timestamp,
                    segment.Id));
            }
            // Blocker: explicit "Blocker:" or "Blocked:" prefix
            else if (segment.Text.StartsWith("Blocker:", StringComparison.OrdinalIgnoreCase) ||
                     segment.Text.StartsWith("Blocked:", StringComparison.OrdinalIgnoreCase))
            {
                var text = segment.Text.StartsWith("Blocker:", StringComparison.OrdinalIgnoreCase)
                    ? segment.Text["Blocker:".Length..].Trim()
                    : segment.Text["Blocked:".Length..].Trim();
                
                insights.Add(new ActionItem(
                    Guid.NewGuid(),
                    segment.ParticipantId,
                    text,
                    DueDate: null,
                    Status: "blocked",
                    segment.Id));
            }
        }

        return insights;
    }

    private bool ShouldInvokeDeepAnalysis(IReadOnlyList<TranscriptSegment> segments)
    {
        if (segments.Count < 3)
            return false;  // Too few new segments

        // Look for keywords suggesting important content
        var text = string.Join(" ", segments.Select(s => s.Text)).ToLowerInvariant();
        var importanceKeywords = new[] { "need", "must", "should", "problem", "issue", "concern", "urgent", "critical", "delay", "risk", "decision", "plan" };

        return importanceKeywords.Any(kw => text.Contains(kw));
    }

    private async Task PerformDeepAnalysisAsync(IReadOnlyList<TranscriptSegment> segments, CancellationToken ct)
    {
        try
        {
            // Convert meeting context to transcript snapshot for the AI provider
            var transcript = context.GetRecentTranscript();
            var entries = transcript.Select(s =>
            {
                var participant = context.Participants.FirstOrDefault(p => p.Id == s.ParticipantId);
                var displayName = participant?.DisplayName ?? "Unknown";
                return new TranscriptEntry(s.Id, s.Timestamp, displayName, s.Text);
            }).ToArray();
            var meetingId = Guid.TryParse(context.MeetingId, out var id) ? id : Guid.NewGuid();
            var snapshot = new MeetingSnapshot(meetingId, context.MeetingId, true, Array.AsReadOnly(entries));
            
            var prompt = BuildAnalysisPrompt(segments);
            var answer = await assistant.AskAsync(snapshot, prompt, ct);
            
            // Parse structured insights from response
            // (For demo, just use the raw response; production would parse structured output)
            // In a real system, you'd extract action items, decisions, blockers from the AI response
        }
        catch (Exception ex)
        {
            // Log but don't crash; proactive analysis is optional enhancement
            System.Diagnostics.Debug.WriteLine($"Deep analysis failed: {ex.Message}");
        }
    }

    private static string BuildAnalysisPrompt(IReadOnlyList<TranscriptSegment> segments)
    {
        var transcript = string.Join("\n", segments.Select(s => $"  {s.Text}"));
        return $"""
            Based on this recent conversation, identify any:
            1. Blockers or impediments to progress
            2. Decisions that were made
            3. Action items or tasks assigned
            4. Open questions that remain unresolved
            
            Recent conversation:
            {transcript}
            
            List findings concisely.
            """;
    }
}

/// <summary>
/// Event args for when a new insight is detected.
/// </summary>
public sealed class InsightDetectedEventArgs(object insight) : EventArgs
{
    public object Insight { get; } = insight;
}
