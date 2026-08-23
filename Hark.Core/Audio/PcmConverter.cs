using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Hark.Core.Audio;

/// <summary>
/// Adapt — converts the captured audio into the format the recognizer requires.
/// WASAPI loopback typically delivers 48 kHz stereo 32-bit float; Azure Speech expects
/// 16 kHz mono 16-bit PCM. This stage downmixes to mono, resamples to 16 kHz, and quantizes to 16-bit.
/// </summary>
public sealed class PcmConverter
{
    #region Constants

    /// <summary>Target sample rate required by the speech recognizer.</summary>
    public const int TargetSampleRate = 16_000;

    /// <summary>Target channel count (mono).</summary>
    public const int TargetChannels = 1;

    #endregion

    #region Fields

    /// <summary>Buffers the raw capture bytes so the pull-based resampler chain can draw from them.</summary>
    private readonly BufferedWaveProvider _buffer;

    /// <summary>The head of the conversion chain: mono, resampled to <see cref="TargetSampleRate"/>.</summary>
    private readonly ISampleProvider _resampled;

    /// <summary>Scratch buffer for reading resampled float samples before 16-bit quantization.</summary>
    private readonly float[] _scratch = new float[TargetSampleRate]; // up to ~1s of mono audio

    /// <summary>Reused accumulator for one call's worth of resampled float output (avoids per-call growth churn).</summary>
    private readonly List<float> _floatAccum = new(TargetSampleRate);

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Builds a converter for the given source format (from <see cref="Capture.LoopbackCaptureService.WaveFormat"/>).
    /// </summary>
    /// <param name="sourceFormat">The native capture format.</param>
    public PcmConverter(WaveFormat sourceFormat)
    {
        ArgumentNullException.ThrowIfNull(sourceFormat);

        _buffer = new BufferedWaveProvider(sourceFormat)
        {
            // Return 0 (not silence) when drained, so the read loop terminates.
            ReadFully = false,
            // Tolerate bursts without throwing; we drain on every capture callback.
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2),
        };

        // Capture delivers IEEE float; interpret the buffered bytes as float samples.
        ISampleProvider samples = _buffer.ToSampleProvider();

        // Downmix to mono only when the source is multi-channel.
        if (sourceFormat.Channels > 1)
            samples = new StereoToMonoSampleProvider(samples) { LeftVolume = 0.5f, RightVolume = 0.5f };

        // High-quality resample to the recognizer's expected rate.
        _resampled = new WdlResamplingSampleProvider(samples, TargetSampleRate);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Feeds one capture buffer through the chain and returns the resulting 16 kHz mono 16-bit PCM bytes.
    /// May return an empty array when not enough input has accumulated to produce output.
    /// </summary>
    /// <param name="input">The raw capture buffer.</param>
    /// <param name="bytes">The number of valid bytes in <paramref name="input"/>.</param>
    public byte[] Convert(byte[] input, int bytes) => QuantizeToPcm16(ConvertToFloat(input, bytes));

    /// <summary>
    /// Feeds one capture buffer through the chain and returns the resulting 16 kHz mono float samples
    /// (still in [-1, 1], not yet quantized). Exposed so multiple sources can be summed before
    /// quantization — mixing in the float domain avoids double-clipping. May return an empty array
    /// when not enough input has accumulated to produce output.
    /// </summary>
    /// <param name="input">The raw capture buffer.</param>
    /// <param name="bytes">The number of valid bytes in <paramref name="input"/>.</param>
    public float[] ConvertToFloat(byte[] input, int bytes)
    {
        _buffer.AddSamples(input, 0, bytes);

        _floatAccum.Clear();
        int read;
        while ((read = _resampled.Read(_scratch, 0, _scratch.Length)) > 0)
            for (int i = 0; i < read; i++)
                _floatAccum.Add(_scratch[i]);

        return _floatAccum.ToArray();
    }

    /// <summary>
    /// Quantizes 16 kHz mono float samples (in [-1, 1]) to signed 16-bit little-endian PCM bytes,
    /// clamping out-of-range values that summing multiple sources can produce.
    /// </summary>
    /// <param name="samples">The float samples to quantize.</param>
    public static byte[] QuantizeToPcm16(ReadOnlySpan<float> samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            float f = samples[i];
            f = f > 1f ? 1f : f < -1f ? -1f : f;
            short s = (short)(f * short.MaxValue);
            pcm[i * 2] = (byte)(s & 0xFF);
            pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        return pcm;
    }

    #endregion
}
