using TranscriptHelper.Core.Meeting;

namespace TranscriptHelper.Core;

/// <summary>
/// OpenAI implementation with token-efficient prompt building using rolling summaries.
/// Demonstrates how to use MeetingContext for more intelligent prompting.
/// </summary>
public sealed class OpenAIMeetingAssistant : IMeetingAssistant
{
    private readonly string? apiKey;
    private readonly string modelId;
    private readonly MeetingContext? context;

    public OpenAIMeetingAssistant(string? apiKey = null, string modelId = "gpt-4-turbo", MeetingContext? context = null)
    {
        // For demo: use environment variable if not provided
        this.apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        this.modelId = modelId;
        this.context = context;
    }

    public async Task<AssistantAnswer> AskAsync(
        MeetingSnapshot meeting,
        string question,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question is required.", nameof(question));

        // For now, stub implementation that demonstrates context usage
        // Production version would:
        // 1. Prepare the meeting context (recent transcript, summary, decisions, action items)
        // 2. Build an efficient prompt that fits in token budget
        // 3. Call OpenAI API
        // 4. Parse response and extract source citations
        
        var contextualPrompt = BuildContextualPrompt(meeting, question);
        
        // Stub: Return demo response explaining how it would work
        var demoResponse = $"""
            [Demo Mode - OpenAI not configured]
            
            To use real AI, provide OPENAI_API_KEY environment variable.
            
            This query would send:
            {contextualPrompt}
            """;

        // Return demo response with all transcript entry IDs as sources
        var sourceIds = meeting.Entries.Select(e => e.Id).ToArray();
        return await Task.FromResult(new AssistantAnswer(demoResponse, sourceIds));
    }

    private string BuildContextualPrompt(MeetingSnapshot meeting, string question)
    {
        var participantList = string.Join(", ", meeting.Entries.Select(e => e.Speaker).Distinct());
        
        // Use rolling summaries if available to reduce token usage
        var contextSection = "";
        if (context != null && context.SummaryHistory.Count > 0)
        {
            // Include latest summary for context, then recent entries for detail
            var latestSummary = context.SummaryHistory.FirstOrDefault();
            if (latestSummary != null)
            {
                contextSection += $"Meeting Summary:\n{latestSummary.SummaryText}\n\n";
            }
        }

        var recentTranscript = string.Join("\n", meeting.Entries.TakeLast(10).Select(e =>
            $"[{e.Timestamp.ToLocalTime():HH:mm:ss}] {e.Speaker}: {e.Text}"));

        return $"""
            Meeting: {meeting.Title}
            Participants: {participantList}
            
            {contextSection}Recent transcript (last 10 entries):
            {recentTranscript}
            
            User question: {question}
            
            Provide a concise, helpful answer based on the meeting context.
            If the answer references specific people or decisions, cite the time and speaker.
            """;
    }
}
