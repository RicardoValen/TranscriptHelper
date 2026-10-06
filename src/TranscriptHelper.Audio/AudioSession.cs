using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TranscriptHelper.Audio;

public sealed record OutputDevice(string Id, string Name);
public sealed class AudioSession : IAsyncDisposable
{
    private readonly List<CaptureChannel> channels = [];
    public event Action<string, string, bool>? Transcript;
    public event Action<string, double>? Level;
    public event Action<string>? Error;
    public static IReadOnlyList<OutputDevice> Outputs()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        return devices.Select(d => new OutputDevice(d.ID, d.FriendlyName)).ToArray();
    }
    public async Task StartAsync(string outputId, bool microphone, string? key, string? region, string language)
    {
        if (channels.Count != 0) throw new InvalidOperationException("Already capturing.");
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(outputId);
            channels.Add(new CaptureChannel("Meeting audio", new WasapiLoopbackCapture(device), device));
            if (microphone) channels.Add(new CaptureChannel("You", new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) }));
            foreach (var channel in channels)
            {
                channel.Transcript += (speaker, text, final) => Transcript?.Invoke(speaker, text, final);
                channel.Level += (speaker, level) => Level?.Invoke(speaker, level);
                channel.Error += message => Error?.Invoke(message);
                await channel.StartAsync(key, region, language);
            }
        }
        catch { await DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        Exception? failure = null;
        foreach (var channel in channels)
        {
            try { await channel.DisposeAsync(); } catch (Exception ex) { failure ??= ex; }
        }
        channels.Clear();
        if (failure is not null) throw new InvalidOperationException("Audio cleanup reported an error.", failure);
    }
}

internal sealed class CaptureChannel(string speaker, IWaveIn capture, IDisposable? device = null) : IAsyncDisposable
{
    private BufferedWaveProvider? buffered;
    private ISampleProvider? samples;
    private PushAudioInputStream? stream;
    private AudioStreamFormat? format;
    private AudioConfig? audio;
    private SpeechConfig? config;
    private SpeechRecognizer? recognizer;
    private readonly object gate = new();
    private bool stopping;
    private bool started;
    private readonly TaskCompletionSource<bool> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action<string, string, bool>? Transcript;
    public event Action<string, double>? Level;
    public event Action<string>? Error;

    public async Task StartAsync(string? key, string? region, string language)
    {
        buffered = new BufferedWaveProvider(capture.WaveFormat) { ReadFully = false, BufferDuration = TimeSpan.FromSeconds(5) };
        ISampleProvider input = buffered.ToSampleProvider();
        if (input.WaveFormat.Channels == 2) input = new StereoToMonoSampleProvider(input) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (input.WaveFormat.Channels != 1) throw new NotSupportedException("Select a mono or stereo output device for this demo.");
        samples = new WdlResamplingSampleProvider(input, 16000);
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(region))
        {
            config = SpeechConfig.FromSubscription(key, region);
            config.SpeechRecognitionLanguage = language;
            format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
            stream = AudioInputStream.CreatePushStream(format);
            audio = AudioConfig.FromStreamInput(stream);
            recognizer = new SpeechRecognizer(config, audio);
            recognizer.Recognizing += (_, e) => Transcript?.Invoke(speaker, e.Result.Text, false);
            recognizer.Recognized += (_, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
                    Transcript?.Invoke(speaker, e.Result.Text, true);
            };
            recognizer.Canceled += (_, e) =>
            {
                if (e.Reason == CancellationReason.Error) Error?.Invoke($"{speaker}: Speech service {e.ErrorCode}. Check credentials, region, connectivity, and quota.");
            };
            await recognizer.StartContinuousRecognitionAsync();
        }
        capture.DataAvailable += OnData;
        capture.RecordingStopped += (_, e) =>
        {
            stopped.TrySetResult(true);
            if (e.Exception is not null) Error?.Invoke($"{speaker}: audio device stopped. {e.Exception.Message}");
        };
        capture.StartRecording();
        started = true;
    }
    private void OnData(object? sender, WaveInEventArgs e)
    {
        lock (gate)
        {
            if (stopping || buffered is null || samples is null) return;
            try
            {
                buffered.AddSamples(e.Buffer, 0, e.BytesRecorded);
                var floats = new float[4096];
                double peak = 0;
                int count;
                while ((count = samples.Read(floats, 0, floats.Length)) > 0)
                {
                    var pcm = new byte[count * 2];
                    for (var i = 0; i < count; i++)
                    {
                        peak = Math.Max(peak, Math.Abs(floats[i]));
                        var value = (short)(Math.Clamp(floats[i], -1f, 1f) * short.MaxValue);
                        pcm[i * 2] = (byte)value;
                        pcm[i * 2 + 1] = (byte)(value >> 8);
                    }
                    stream?.Write(pcm);
                }
                Level?.Invoke(speaker, Math.Min(1, peak));
            }
            catch (Exception ex)
            {
                stopping = true;
                Error?.Invoke($"{speaker}: capture processing failed. {ex.Message}");
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        lock (gate) stopping = true;
        try
        {
            if (started)
            {
                capture.StopRecording();
                await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            capture.DataAvailable -= OnData;
            capture.Dispose();
            stream?.Close();
            try { if (recognizer is not null) await recognizer.StopContinuousRecognitionAsync(); }
            finally
            {
                recognizer?.Dispose(); audio?.Dispose(); stream?.Dispose(); format?.Dispose(); device?.Dispose();
            }
        }
    }
}
