using TranscriptHelper.Core.Models;

namespace TranscriptHelper.Core.Speaker;

/// <summary>
/// Identifies which participant is speaking based on audio characteristics.
/// Implementations can use volume, frequency, speaker recognition, or other signals.
/// </summary>
public interface ISpeakerDiarizer
{
    /// <summary>
    /// Identifies the speaker for the given audio frame.
    /// </summary>
    /// <param name="audioSamples">Float samples [-1, 1] at 16kHz mono</param>
    /// <param name="knownParticipants">Participants to match against</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Participant ID of the detected speaker, or null if unknown</returns>
    Task<string?> IdentifySpeakerAsync(
        float[] audioSamples,
        IReadOnlyList<Participant> knownParticipants,
        CancellationToken ct = default);

    /// <summary>
    /// Called when a participant joins or leaves to update the diarizer state.
    /// </summary>
    void OnParticipantChanged(Participant participant, bool added);
}
