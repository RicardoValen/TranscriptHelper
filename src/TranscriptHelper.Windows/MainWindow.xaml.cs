using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TranscriptHelper.Audio;
using TranscriptHelper.Core;
using TranscriptHelper.Core.Meeting;
using TranscriptHelper.Core.Services;
using TranscriptHelper.Core.Speaker;
using TranscriptHelper.Core.Insights;

namespace TranscriptHelper.Windows;
public partial class MainWindow : Window
{
    private readonly MeetingSession meeting = new("Desktop demo", diarizer: new SimpleVolumeDiarizer());
    private AudioSession? capture;
    private bool busy;
    private bool closing;
    private double expandedHeight = 410;
    private bool settingsOpen;
    private IMeetingAssistant assistant;
    private ProactiveInsightService? insightService;
    private AutoSummaryGenerator? summaryGenerator;
    private bool recapView;
    private string TranscriptText() => string.Join(Environment.NewLine, meeting.Snapshot().Entries.Select(x => $"[{x.Timestamp.ToLocalTime():HH:mm:ss}] {x.Speaker}: {x.Text}"));
     
    private string ParticipantsText()
    {
        var context = meeting.GetContext();
        if (context?.Participants.Count == 0) return "No participants yet.";
        return string.Join("\n", (context?.Participants ?? []).Select(p => 
            $"• {p.DisplayName}{(p.IsSelf ? " (You)" : "")}"));
    }
     
    private string ActionItemsText()
    {
        var context = meeting.GetContext();
        if (context?.ActionItems.Count == 0) return "No action items yet.";
        if (context == null) return "No action items yet.";
        return string.Join("\n\n", context.ActionItems.Select(a =>
        {
            var participant = context.Participants.FirstOrDefault(p => p.Id == a.ParticipantId);
            var owner = participant?.DisplayName ?? "Unknown";
            return $"✓ {a.Description}\n  Owner: {owner}\n  Due: {a.DueDate?.ToShortDateString() ?? "TBD"}\n  Status: {a.Status ?? "Pending"}";
        }));
    }
      
    private string DecisionsText()
    {
        var context = meeting.GetContext();
        if (context?.Decisions.Count == 0) return "No decisions made yet.";
        if (context == null) return "No decisions made yet.";
        return string.Join("\n\n", context.Decisions.Select(d =>
        {
            var participant = context.Participants.FirstOrDefault(p => p.Id == d.ParticipantId);
            var decidedBy = participant?.DisplayName ?? "Unknown";
            return $"→ {d.Description}\n  By: {decidedBy}\n  Rationale: {d.Rationale ?? "N/A"}";
        }));
    }
     
    private async Task AskDemoAsync(string question)
    {
        if (meeting.Snapshot().Entries.Count == 0) { Partial.Text = "No transcript yet. Configure Azure Speech in Audio setup and start listening."; return; }
        var answer = await assistant.AskAsync(meeting.Snapshot(), question);
        recapView = true; Transcript.Text = answer.Text; Partial.Text = "";
    }
    private async void AskClick(object sender, RoutedEventArgs e) => await AskDemoAsync(Prompt.Text);
    private void AssistClick(object sender, RoutedEventArgs e)
    { recapView = false; Transcript.Text = TranscriptText(); ModeBadge.Content = "Live transcript"; }
    private async void RecapClick(object sender, RoutedEventArgs e)
    { ModeBadge.Content = "Recap"; await AskDemoAsync("summary"); }
    private void SuggestClick(object sender, RoutedEventArgs e)
    { ModeBadge.Content = "What should I say?"; Partial.Text = "Suggestions require a real AI provider. Copilot is not connected in this demo."; }
    private void FollowUpClick(object sender, RoutedEventArgs e)
    { ModeBadge.Content = "Follow-up questions"; Partial.Text = "Follow-up generation requires a real AI provider. Copilot is not connected in this demo."; }
    private void CollapseClick(object sender, RoutedEventArgs e)
    {
        if (Shell.Visibility == Visibility.Visible)
        { expandedHeight = Height; Shell.Visibility = Visibility.Collapsed; Height = 100; CollapseButton.Content = "⌃ Show"; }
        else { Shell.Visibility = Visibility.Visible; Height = expandedHeight; CollapseButton.Content = "⌄ Hide"; }
    }
    private void SettingsExpanded(object sender, RoutedEventArgs e) { settingsOpen = true; Height = Math.Max(Height, 780); }
    private void SettingsCollapsed(object sender, RoutedEventArgs e) { if (settingsOpen) { Height = 410; settingsOpen = false; } }
    private async void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == System.Windows.Input.Key.H) { CollapseClick(sender, e); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Enter) { await AskDemoAsync(Prompt.Text); e.Handled = true; }
    }
    public MainWindow()
    {
        InitializeComponent();
        Key.Password = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ?? "";
        Region.Text = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ?? "";
        
        // Get meeting context early for assistant initialization
        var context = meeting.GetContext();
        
        // Initialize AI assistant (OpenAI if API key provided, else Demo)
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        assistant = !string.IsNullOrWhiteSpace(openAiKey) 
            ? (IMeetingAssistant)new OpenAIMeetingAssistant(openAiKey, "gpt-4-turbo", context)
            : new DemoMeetingAssistant();
        
        // Initialize auto-summary generator for token efficiency in long meetings
        if (context != null)
        {
            summaryGenerator = new AutoSummaryGenerator(assistant, context, entriesBetweenSummary: 50);
            
            // Initialize proactive insight service
            insightService = new ProactiveInsightService(assistant, context);
            insightService.InsightDetected += (s, args) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Status.Text = $"Insight detected: {args.Insight.GetType().Name}";
                }));
            };
        }
        
        try { Output.ItemsSource = AudioSession.Outputs(); Output.SelectedIndex = 0; }
        catch (Exception ex) { Status.Text = $"Could not enumerate output devices: {ex.Message}"; }
    }
    private async void StartClick(object sender, RoutedEventArgs e)
    {
        if (busy || capture is not null) return;
        if (Output.SelectedItem is not OutputDevice output) { Status.Text = "Choose an output device."; return; }
        var key = Key.Password.Trim(); var region = Region.Text.Trim();
        if (string.IsNullOrEmpty(key) != string.IsNullOrEmpty(region)) { Status.Text = "Provide both Speech key and region, or leave both empty."; return; }
        busy = true; Start.IsEnabled = false; Output.IsEnabled = false; Mic.IsEnabled = false;
        capture = new AudioSession();
        capture.Level += (speaker, level) => Dispatcher.BeginInvoke(new Action(() =>
        { if (speaker == "You") MicLevel.Value = level; else SpeakerLevel.Value = level; }));
        capture.Error += message => Dispatcher.BeginInvoke(new Action(() => Status.Text = message));
        capture.Transcript += (speaker, text, final) => Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (final && meeting.TryAppend(speaker, text, out _))
            { 
                if (!recapView) { Transcript.Text = TranscriptText(); Transcript.ScrollToEnd(); } 
                Partial.Text = ""; 
                
                // Try to generate summary for token efficiency in long meetings
                if (summaryGenerator != null)
                {
                    try
                    {
                        await summaryGenerator.TryGenerateSummaryAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Summary generation error: {ex.Message}");
                    }
                }
            }
            else if (!final) Partial.Text = $"{speaker}: {text}";
        }));
        meeting.SetEnabled(true);
        try
        {
            Status.Text = "Starting capture…";
            await capture.StartAsync(output.Id, Mic.IsChecked == true, key, region, LanguageCode.Text.Trim());
            Status.Text = string.IsNullOrEmpty(key) ? "Listening — meters only; transcription is not configured." : "Listening — audio is sent to Azure Speech for live transcription.";
            Stop.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Status.Text = $"Start failed: {ex.Message}";
            try { await capture.DisposeAsync(); } catch { /* Preserve startup error. */ }
            capture = null; meeting.SetEnabled(false); SetIdle();
        }
        finally { busy = false; }
    }
    private async Task StopCaptureAsync()
    {
        var current = capture; capture = null;
        if (current is null) return;
        try { await current.DisposeAsync(); Status.Text = "Stopped."; }
        catch (Exception ex) { Status.Text = $"Stopped with an error: {ex.Message}"; }
        finally
        {
            // Final recognition callbacks may already be queued on the dispatcher.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            meeting.SetEnabled(false); Partial.Text = ""; SetIdle();
        }
    }
    private void SetIdle() { Start.IsEnabled = true; Stop.IsEnabled = false; Output.IsEnabled = true; Mic.IsEnabled = true; MicLevel.Value = 0; SpeakerLevel.Value = 0; }
    private async void StopClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        busy = true; Stop.IsEnabled = false;
        try { await StopCaptureAsync(); } finally { busy = false; }
    }
    private void ExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON transcript|*.json", FileName = "meeting-transcript.json" };
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(meeting.Snapshot(), new JsonSerializerOptions { WriteIndented = true })); Status.Text = "Transcript exported."; }
        catch (Exception ex) { Status.Text = $"Export failed: {ex.Message}"; }
    }
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void OpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (Shell is not null) Shell.Background = new SolidColorBrush(Color.FromArgb((byte)(e.NewValue * 255), 8, 11, 16)); }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        if (busy) { Status.Text = "Wait for the current operation, then close."; return; }
        busy = true;
        await StopCaptureAsync();
        closing = true; Close();
    }
}
