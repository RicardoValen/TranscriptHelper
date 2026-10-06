namespace TranscriptHelper.Core;

// No model, network connection, or Copilot license is used by this adapter.
public sealed class DemoMeetingAssistant : IMeetingAssistant
{
    public Task<AssistantAnswer> AskAsync(MeetingSnapshot meeting, string question, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("Question is required.", nameof(question));
        IEnumerable<TranscriptEntry> selected = meeting.Entries;
        if (question.Contains("action", StringComparison.OrdinalIgnoreCase) || question.Contains("task", StringComparison.OrdinalIgnoreCase))
            selected = selected.Where(x => x.Text.StartsWith("Action:", StringComparison.OrdinalIgnoreCase));
        else if (question.Contains("decision", StringComparison.OrdinalIgnoreCase))
            selected = selected.Where(x => x.Text.StartsWith("Decision:", StringComparison.OrdinalIgnoreCase));
        else if (!question.Contains("summary", StringComparison.OrdinalIgnoreCase) && !question.Contains("summarize", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new AssistantAnswer("Demo mode supports summary, decisions, and actions queries. A real AI provider will handle other questions.", []));
        var sources = selected.ToArray();
        var text = sources.Length == 0 ? "No matching transcript entries." :
            string.Join(Environment.NewLine, sources.Select(x => $"[{x.Id.ToString()[..8]}] {x.Speaker}: {x.Text}"));
        return Task.FromResult(new AssistantAnswer("DEMO — transcript excerpts, not an AI-generated answer" + Environment.NewLine + text, sources.Select(x => x.Id).ToArray()));
    }
}
