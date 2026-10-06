using TranscriptHelper.Core.Models;

namespace TranscriptHelper.Core.Meeting;

public sealed class MeetingContext
{
    private const int RecentSegmentLimit = 30;
    private readonly object gate = new();
    private readonly LinkedList<TranscriptSegment> recentTranscript = [];

    public string MeetingId { get; }
    public List<Participant> Participants { get; } = [];
    public List<ActionItem> ActionItems { get; } = [];
    public List<Decision> Decisions { get; } = [];
    public List<OpenQuestion> OpenQuestions { get; } = [];
    public string RollingSummary { get; set; } = "";

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
                RollingSummary);
    }
}

public sealed record MeetingContextSnapshot(
    string MeetingId,
    IReadOnlyList<Participant> Participants,
    IReadOnlyList<TranscriptSegment> RecentTranscript,
    IReadOnlyList<ActionItem> ActionItems,
    IReadOnlyList<Decision> Decisions,
    IReadOnlyList<OpenQuestion> OpenQuestions,
    string RollingSummary);
