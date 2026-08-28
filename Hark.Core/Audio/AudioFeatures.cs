namespace Hark.Core.Audio;

/// <summary>
/// A ~20 Hz snapshot of the captured audio's loudness across a few perceptual dimensions. All
/// values are unnormalized RMS magnitudes (roughly 0..1); the consumer gates and shapes them.
/// Splitting the energy into bands lets independent visual parameters react to different facets of
/// the sound at once — the way WavBall drives separate bass/treble envelopes rather than one RMS.
/// </summary>
/// <param name="Level">Overall broadband RMS — drives the eye's core brightness/pulse.</param>
/// <param name="Bass">Low-pass (~&lt;330 Hz) RMS — voiced/vowel body; drives the pupil's dilation swell.</param>
/// <param name="Treble">High-pass (~&gt;330 Hz) RMS — consonants/sibilance; drives the highlight's shimmer.</param>
public readonly record struct AudioFeatures(double Level, double Bass, double Treble);
