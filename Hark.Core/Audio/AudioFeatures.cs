namespace Hark.Core.Audio;

/// <summary>
/// A ~20 Hz snapshot of the captured audio's loudness across a few perceptual dimensions. All
/// values are unnormalized RMS magnitudes (roughly 0..1); the consumer gates and shapes them.
/// Splitting the energy into bands lets independent visual parameters react to different facets of
/// the sound at once — the way WavBall drives separate bass/treble envelopes rather than one RMS.
/// </summary>
/// <param name="Level">System (loopback) broadband RMS — the far-side / system-audio core pulse.</param>
/// <param name="Bass">System low-pass (~&lt;330 Hz) RMS — voiced/vowel body; drives the pupil's dilation swell.</param>
/// <param name="Treble">System high-pass (~&gt;330 Hz) RMS — consonants/sibilance; drives the highlight's shimmer.</param>
/// <param name="MicLevel">Microphone broadband RMS (0 when the mic isn't mixed) — kept SEPARATE from the
/// system bands so a consumer can react to the mic on its own sensitivity path; mic speech is far quieter
/// in absolute RMS than system playback, so a headset-only user needs a hotter mapping, not a hotter input.</param>
/// <param name="MicBass">Microphone low-pass RMS.</param>
/// <param name="MicTreble">Microphone high-pass RMS.</param>
public readonly record struct AudioFeatures(
    double Level, double Bass, double Treble,
    double MicLevel, double MicBass, double MicTreble);
