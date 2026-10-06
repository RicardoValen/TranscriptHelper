using System.Text.Json;
using TranscriptHelper.Core;

var session = new MeetingSession("Standalone meeting demo");
IMeetingAssistant assistant = new DemoMeetingAssistant();
Console.WriteLine("Transcript Helper | local demo | no audio capture or Copilot connection");
Console.WriteLine("Commands: /start, /pause, /sample, /transcript, /insights, /ask QUESTION, /export, /new, /quit");
Console.WriteLine("While enabled, enter Speaker: message. Mark items with Decision:, Action:, or Question:.");
while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine()?.Trim();
    if (input is null || input.Equals("/quit", StringComparison.OrdinalIgnoreCase)) break;
    if (input.Length == 0) continue;
    try
    {
        switch (input.ToLowerInvariant())
        {
            case "/start": session.SetEnabled(true); Console.WriteLine("Enabled."); break;
            case "/pause": session.SetEnabled(false); Console.WriteLine("Paused; new entries will not be retained."); break;
            case "/new": session = new MeetingSession("Standalone meeting demo"); Console.WriteLine("New session, paused."); break;
            case "/sample":
                if (!session.Snapshot().IsEnabled) { Console.WriteLine("Use /start first."); break; }
                foreach (var (speaker, text) in new[] {
                    ("Marco", "We need a standalone demo before integrating Teams."),
                    ("Maria", "Question: Can the same meeting logic work in another interface?"),
                    ("Alex", "Decision: Keep the domain logic independent of the interface and AI provider."),
                    ("Marco", "Action: Marco will review the demo by Friday."),
                    ("Maria", "Action: Maria will investigate supported live meeting access.") })
                    session.TryAppend(speaker, text, out _);
                Console.WriteLine("Added five sample entries from three speakers."); break;
            case "/transcript":
                var snapshot = session.Snapshot();
                Console.WriteLine($"{snapshot.Title} | {(snapshot.IsEnabled ? "enabled" : "paused")} | {snapshot.Entries.Count} entries");
                foreach (var entry in snapshot.Entries) Console.WriteLine($"[{entry.Id.ToString()[..8]}] {entry.Timestamp:HH:mm:ss} {entry.Speaker}: {entry.Text}");
                break;
            case "/insights":
                var insights = InsightExtractor.Extract(session.Snapshot());
                if (insights.Count == 0) Console.WriteLine("No marked decisions, actions, or questions.");
                foreach (var item in insights) Console.WriteLine($"{item.Kind} | {item.Speaker} | {item.Text} | source {item.SourceEntryId.ToString()[..8]}");
                break;
            case "/export":
                var export = session.Snapshot();
                var path = Path.GetFullPath($"meeting-{export.Id}.json");
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Saved {path}"); break;
            default:
                if (input.StartsWith("/ask ", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine((await assistant.AskAsync(session.Snapshot(), input[5..])).Text);
                else if (input.StartsWith('/')) Console.WriteLine("Unknown command.");
                else
                {
                    var colon = input.IndexOf(':');
                    if (colon <= 0) { Console.WriteLine("Use Speaker: message."); break; }
                    Console.WriteLine(session.TryAppend(input[..colon], input[(colon + 1)..], out _) ? "Added." : "Paused; use /start.");
                }
                break;
        }
    }
    catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
    { Console.WriteLine($"Could not complete command: {error.Message}"); }
}
