using TranscriptHelper.Core.Models;

namespace TranscriptHelper.Core.Speaker;

/// <summary>
/// Simple volume-based speaker diarization for demo purposes.
/// Tracks average volume per known speaker and assigns new speech to closest match.
/// Real implementation should use speaker embedding models or voice fingerprinting.
/// </summary>
public sealed class SimpleVolumeDiarizer : ISpeakerDiarizer
{
    private readonly Dictionary<string, VolumeProfile> profiles = [];
    private const int SampleWindowMs = 100;  // Analyze ~100ms chunks
    private const float SilenceThreshold = 0.02f;  // Below this = silence

    private sealed class VolumeProfile
    {
        public string ParticipantId { get; init; } = "";
        public double VolumeSum { get; set; }
        public int SampleCount { get; set; }
        public double AverageVolume => SampleCount > 0 ? VolumeSum / SampleCount : 0;
    }

    public void OnParticipantChanged(Participant participant, bool added)
    {
        if (added)
        {
            if (!profiles.ContainsKey(participant.Id))
                profiles[participant.Id] = new VolumeProfile { ParticipantId = participant.Id };
        }
        else
        {
            profiles.Remove(participant.Id);
        }
    }

    public Task<string?> IdentifySpeakerAsync(
        float[] audioSamples,
        IReadOnlyList<Participant> knownParticipants,
        CancellationToken ct = default)
    {
        if (audioSamples.Length == 0 || knownParticipants.Count == 0)
            return Task.FromResult<string?>(null);

        // Calculate RMS (root mean square) power of the audio frame
        double sumSquares = 0;
        foreach (var sample in audioSamples)
            sumSquares += sample * sample;
        double rms = Math.Sqrt(sumSquares / audioSamples.Length);

        // If mostly silence, return unknown speaker
        if (rms < SilenceThreshold)
            return Task.FromResult<string?>(null);

        // Update volume profiles for all known participants
        foreach (var participant in knownParticipants)
        {
            if (!profiles.TryGetValue(participant.Id, out var profile))
            {
                profile = new VolumeProfile { ParticipantId = participant.Id };
                profiles[participant.Id] = profile;
            }

            // Accumulate volume statistics (simple exponential smoothing)
            profile.VolumeSum = profile.VolumeSum * 0.9 + rms * 0.1;
            profile.SampleCount = Math.Min(profile.SampleCount + 1, 1000);  // Cap to prevent overflow
        }

        // Assign to the participant with closest matching volume
        // This is naive but works for 2-3 speakers; real system uses speaker embeddings
        var bestMatch = profiles.Values
            .OrderBy(p => Math.Abs(p.AverageVolume - rms))
            .FirstOrDefault();

        return Task.FromResult<string?>(bestMatch?.ParticipantId ?? knownParticipants.FirstOrDefault()?.Id);
    }
}
