using TranscriptHelper.Core.Models;

namespace TranscriptHelper.Core.Meeting;

public sealed class MeetingContext
{
    private const int RecentSegmentLimit = 30;
    private readonly object gate = new();
    private readonly LinkedList<TranscriptSegment> recentTranscript = [];
    private readonly List<RollingSummary> summaryHistory = [];

    public string MeetingId { get; }
    public List<Participant> Participants { get; } = [];
    public List<ActionItem> ActionItems { get; } = [];
    public List<Decision> Decisions { get; } = [];
    public List<OpenQuestion> OpenQuestions { get; } = [];
    
    /// <summary>Historical rolling summaries, newest first.</summary>
    public IReadOnlyList<RollingSummary> SummaryHistory 
    { 
        get 
        { 
            lock (gate) return summaryHistory.AsReadOnly(); 
        } 
    }

    public MeetingContext(string meetingId)
    {
        MeetingId = meetingId;
    }

    public void AppendSegment(TranscriptSegment segment)
    {
        lock (gate)
        {
            recentTranscript.AddLast(segment);
            while (recentTranscript.Count > RecentSegmentLimit)
                recentTranscript.RemoveFirst();
        }
    }

    public void AddSummary(RollingSummary summary)
    {
        lock (gate)
        {
            summaryHistory.Insert(0, summary);  // Newest first
            // Keep only last 10 summaries to avoid unbounded growth
            while (summaryHistory.Count > 10)
                summaryHistory.RemoveAt(summaryHistory.Count - 1);
        }
    }

    public TranscriptSegment[] GetRecentTranscript()
    {
        lock (gate) return recentTranscript.ToArray();
    }

    public MeetingContextSnapshot ToSnapshot()
    {
        lock (gate)
            return new(
                MeetingId,
                Participants.AsReadOnly(),
                recentTranscript.ToArray().AsReadOnly(),
                ActionItems.AsReadOnly(),
                Decisions.AsReadOnly(),
                OpenQuestions.AsReadOnly(),
                summaryHistory.AsReadOnly());
    }
}

public sealed record MeetingContextSnapshot(
    string MeetingId,
    IReadOnlyList<Participant> Participants,
    IReadOnlyList<TranscriptSegment> RecentTranscript,
    IReadOnlyList<ActionItem> ActionItems,
    IReadOnlyList<Decision> Decisions,
    IReadOnlyList<OpenQuestion> OpenQuestions,
    IReadOnlyList<RollingSummary> SummaryHistory);
