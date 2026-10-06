using TranscriptHelper.Core;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
var meeting = new MeetingSession("Checks");
Check(!meeting.TryAppend("Marco", "Ignored while paused", out _), "Paused session retained an entry.");
meeting.SetEnabled(true);
meeting.TryAppend("Marco", "Decision: Separate core logic.", out var decision);
var before = meeting.Snapshot();
meeting.TryAppend("Maria", "Action: Maria will review it.", out var action);
meeting.TryAppend("Alex", "Question: Is live context available?", out _);
Check(before.Entries.Count == 1, "Snapshot changed after append.");
Check(meeting.Snapshot().Entries.Select(x => x.Speaker).Distinct().Count() == 3, "Multi-speaker tracking failed.");
meeting.SetEnabled(false);
Check(!meeting.TryAppend("Alex", "Ignored", out _), "Pause did not stop ingestion.");
Check(InsightExtractor.Extract(meeting.Snapshot()).Count == 3, "Explicit marker extraction failed.");
var assistant = new DemoMeetingAssistant();
var answer = await assistant.AskAsync(meeting.Snapshot(), "actions");
Check(answer.SourceEntryIds.SequenceEqual(new[] { action!.Id }), "Action answer references incorrect entries.");
Check(!answer.SourceEntryIds.Contains(decision!.Id), "Decision leaked into action result.");
var unsupported = await assistant.AskAsync(meeting.Snapshot(), "What should I say next?");
Check(unsupported.SourceEntryIds.Count == 0 && unsupported.Text.Contains("Demo mode"), "Demo fabricated an unsupported answer.");
using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
try { await assistant.AskAsync(meeting.Snapshot(), "summary", cancellation.Token); throw new Exception("Cancellation ignored."); }
catch (OperationCanceledException) { }
Console.WriteLine("All checks passed.");
