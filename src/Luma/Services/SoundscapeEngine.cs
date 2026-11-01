using System;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;

namespace Luma.Services;

/// <summary>
/// Soundscape preset types.
/// </summary>
public enum SoundscapePreset
{
    AlphaFocus432Hz,
    CyberpunkRain,
    DeepBrownNoise,
    CoffeeShopVinyl
}

/// <summary>
/// Native procedural soundscape generator and player.
/// Generates 16-bit 44.1kHz stereo audio entirely in memory without external audio libraries.
/// </summary>
public sealed class SoundscapeEngine : IDisposable
{
    private const int SampleRate = 44100;
    private const int Channels = 2; // Stereo
    private const int BitsPerSample = 16;
    private const double LoopSeconds = 6.0; // 6-second seamless audio loops

    private SoundPlayer? _player;
    private MemoryStream? _audioStream;
    private readonly object _lock = new();
    private bool _isPlaying;
    private SoundscapePreset _currentPreset = SoundscapePreset.AlphaFocus432Hz;
    private double _volume = 0.65; // 0.0 to 1.0

    // Focus Timer (Pomodoro)
    private CancellationTokenSource? _timerCts;
    private TimeSpan _timerRemaining = TimeSpan.Zero;
    private bool _isTimerRunning;

    public event Action<bool>? PlaybackStateChanged;
    public event Action<SoundscapePreset>? PresetChanged;
    public event Action<TimeSpan>? TimerTicked;
    public event Action? TimerFinished;

    public bool IsPlaying => _isPlaying;
    public SoundscapePreset CurrentPreset => _currentPreset;
    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_volume - clamped) > 0.001)
            {
                _volume = clamped;
                if (_isPlaying)
                {
                    // Regenerate current loop with new volume
                    RestartCurrentPreset();
                }
            }
        }
    }

    public TimeSpan TimerRemaining => _timerRemaining;
    public bool IsTimerRunning => _isTimerRunning;

    public SoundscapeEngine()
    {
    }

    public void SetPreset(SoundscapePreset preset)
    {
        if (_currentPreset == preset && _isPlaying) return;
        _currentPreset = preset;
        PresetChanged?.Invoke(preset);
        if (_isPlaying)
        {
            RestartCurrentPreset();
        }
    }

    public void Play()
    {
        lock (_lock)
        {
            if (_isPlaying) return;
            GenerateAndStartLoop();
            _isPlaying = true;
            PlaybackStateChanged?.Invoke(true);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isPlaying) return;
            StopInternal();
            _isPlaying = false;
            PlaybackStateChanged?.Invoke(false);
        }
    }

    public void TogglePlayPause()
    {
        if (_isPlaying) Stop();
        else Play();
    }

    public void StartFocusTimer(int minutes)
    {
        StopFocusTimer();
        _timerRemaining = TimeSpan.FromMinutes(minutes);
        _isTimerRunning = true;
        _timerCts = new CancellationTokenSource();
        var token = _timerCts.Token;

        Play();

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _timerRemaining.TotalSeconds > 0)
            {
                await Task.Delay(1000, token);
                _timerRemaining = _timerRemaining.Subtract(TimeSpan.FromSeconds(1));
                TimerTicked?.Invoke(_timerRemaining);
            }

            if (!token.IsCancellationRequested)
            {
                _isTimerRunning = false;
                Stop();
                TimerFinished?.Invoke();
            }
        }, token);
    }

    public void StopFocusTimer()
    {
        if (_isTimerRunning)
        {
            _timerCts?.Cancel();
            _timerCts?.Dispose();
            _timerCts = null;
            _isTimerRunning = false;
            _timerRemaining = TimeSpan.Zero;
            TimerTicked?.Invoke(TimeSpan.Zero);
        }
    }

    private void RestartCurrentPreset()
    {
        lock (_lock)
        {
            StopInternal();
            GenerateAndStartLoop();
        }
    }

    private void StopInternal()
    {
        try
        {
            _player?.Stop();
            _player?.Dispose();
            _player = null;

            _audioStream?.Dispose();
            _audioStream = null;
        }
        catch { }
    }

    private void GenerateAndStartLoop()
    {
        byte[] wavBytes = GenerateWavBytes(_currentPreset, LoopSeconds, _volume);
        _audioStream = new MemoryStream(wavBytes);
        _player = new SoundPlayer(_audioStream);
        _player.Load();
        _player.PlayLooping();
    }

    /// <summary>
    /// Procedurally synthesizes a 16-bit 44.1kHz stereo WAV buffer.
    /// </summary>
    public static byte[] GenerateWavBytes(SoundscapePreset preset, double durationSeconds, double volume)
    {
        int sampleCount = (int)(SampleRate * durationSeconds);
        // Stereo: 2 channels * 2 bytes per sample (short) = 4 bytes per frame
        int pcmDataSize = sampleCount * Channels * sizeof(short);
        int totalFileSize = 44 + pcmDataSize;

        using var ms = new MemoryStream(totalFileSize);
        using var bw = new BinaryWriter(ms);

        // --- RIFF Header ---
        bw.Write("RIFF"u8);
        bw.Write(totalFileSize - 8);
        bw.Write("WAVE"u8);

        // --- 'fmt ' subchunk ---
        bw.Write("fmt "u8);
        bw.Write(16); // SubChunk1Size (16 for PCM)
        bw.Write((short)1); // AudioFormat: PCM = 1
        bw.Write((short)Channels); // 2 channels
        bw.Write(SampleRate);
        bw.Write(SampleRate * Channels * (BitsPerSample / 8)); // ByteRate
        bw.Write((short)(Channels * (BitsPerSample / 8))); // BlockAlign
        bw.Write((short)BitsPerSample);

        // --- 'data' subchunk ---
        bw.Write("data"u8);
        bw.Write(pcmDataSize);

        // Generate audio data according to selected preset
        switch (preset)
        {
            case SoundscapePreset.AlphaFocus432Hz:
                GenerateAlphaBinauralPcm(bw, sampleCount, volume);
                break;
            case SoundscapePreset.CyberpunkRain:
                GenerateRainPcm(bw, sampleCount, volume);
                break;
            case SoundscapePreset.DeepBrownNoise:
                GenerateBrownNoisePcm(bw, sampleCount, volume);
                break;
            case SoundscapePreset.CoffeeShopVinyl:
                GenerateVinylAmbiencePcm(bw, sampleCount, volume);
                break;
            default:
                GenerateBrownNoisePcm(bw, sampleCount, volume);
                break;
        }

        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Binaural Beat: Left Ear 432 Hz, Right Ear 442 Hz (10 Hz difference for Alpha wave neuro-stimulation).
    /// Plus warm pink noise backdrop for relaxation.
    /// </summary>
    private static void GenerateAlphaBinauralPcm(BinaryWriter bw, int sampleCount, double volume)
    {
        double freqLeft = 432.0;
        double freqRight = 442.0;
        double pinkB0 = 0, pinkB1 = 0, pinkB2 = 0;
        var rng = new Random(1337);

        for (int i = 0; i < sampleCount; i++)
        {
            double t = (double)i / SampleRate;

            // Smooth cross-fade near boundaries to ensure seamless loop
            double envelope = GetLoopEnvelope(i, sampleCount);

            // Sine carrier
            double leftSine = Math.Sin(2.0 * Math.PI * freqLeft * t) * 0.42;
            double rightSine = Math.Sin(2.0 * Math.PI * freqRight * t) * 0.42;

            // Soft pink noise floor
            double white = rng.NextDouble() * 2.0 - 1.0;
            pinkB0 = 0.99765 * pinkB0 + white * 0.0990460;
            pinkB1 = 0.96300 * pinkB1 + white * 0.2965164;
            pinkB2 = 0.57000 * pinkB2 + white * 1.0526913;
            double pink = (pinkB0 + pinkB1 + pinkB2 + white * 0.1848) * 0.04;

            double leftSample = (leftSine + pink) * volume * envelope;
            double rightSample = (rightSine + pink) * volume * envelope;

            short leftShort = (short)Math.Clamp(leftSample * 32767.0, -32768.0, 32767.0);
            short rightShort = (short)Math.Clamp(rightSample * 32767.0, -32768.0, 32767.0);

            bw.Write(leftShort);
            bw.Write(rightShort);
        }
    }

    /// <summary>
    /// Procedural Rain: Multiband filtered noise + randomized micro-droplets with exponential decay.
    /// </summary>
    private static void GenerateRainPcm(BinaryWriter bw, int sampleCount, double volume)
    {
        var rng = new Random(4242);
        double filterL = 0, filterR = 0;
        double dropletEnergyL = 0, dropletEnergyR = 0;

        for (int i = 0; i < sampleCount; i++)
        {
            double envelope = GetLoopEnvelope(i, sampleCount);

            // Random micro-droplet clicks
            if (rng.NextDouble() < 0.0035) dropletEnergyL += rng.NextDouble() * 0.35;
            if (rng.NextDouble() < 0.0035) dropletEnergyR += rng.NextDouble() * 0.35;

            dropletEnergyL *= 0.985; // fast decay
            dropletEnergyR *= 0.985;

            double whiteL = rng.NextDouble() * 2.0 - 1.0;
            double whiteR = rng.NextDouble() * 2.0 - 1.0;

            // Low-pass / bandpass rain filter
            filterL = filterL * 0.88 + whiteL * 0.12;
            filterR = filterR * 0.88 + whiteR * 0.12;

            double left = (filterL * 0.5 + dropletEnergyL * (rng.NextDouble() * 0.4 - 0.2)) * volume * envelope;
            double right = (filterR * 0.5 + dropletEnergyR * (rng.NextDouble() * 0.4 - 0.2)) * volume * envelope;

            short leftShort = (short)Math.Clamp(left * 32767.0, -32768.0, 32767.0);
            short rightShort = (short)Math.Clamp(right * 32767.0, -32768.0, 32767.0);

            bw.Write(leftShort);
            bw.Write(rightShort);
        }
    }

    /// <summary>
    /// Brown Noise: Integrated random walk with leaktight dampening (1/f² power dropoff).
    /// </summary>
    private static void GenerateBrownNoisePcm(BinaryWriter bw, int sampleCount, double volume)
    {
        var rng = new Random(777);
        double brownL = 0.0;
        double brownR = 0.0;

        for (int i = 0; i < sampleCount; i++)
        {
            double envelope = GetLoopEnvelope(i, sampleCount);

            double whiteL = (rng.NextDouble() * 2.0 - 1.0);
            double whiteR = (rng.NextDouble() * 2.0 - 1.0);

            // Leaky integration
            brownL = (brownL + (0.02 * whiteL)) / 1.02;
            brownR = (brownR + (0.02 * whiteR)) / 1.02;

            double left = brownL * 3.5 * volume * envelope;
            double right = brownR * 3.5 * volume * envelope;

            short leftShort = (short)Math.Clamp(left * 32767.0, -32768.0, 32767.0);
            short rightShort = (short)Math.Clamp(right * 32767.0, -32768.0, 32767.0);

            bw.Write(leftShort);
            bw.Write(rightShort);
        }
    }

    /// <summary>
    /// Coffee Shop & Vinyl Ambience: Subtle mid rumble + randomized vinyl crackles.
    /// </summary>
    private static void GenerateVinylAmbiencePcm(BinaryWriter bw, int sampleCount, double volume)
    {
        var rng = new Random(999);
        double rumbleL = 0, rumbleR = 0;

        for (int i = 0; i < sampleCount; i++)
        {
            double envelope = GetLoopEnvelope(i, sampleCount);

            double whiteL = rng.NextDouble() * 2.0 - 1.0;
            double whiteR = rng.NextDouble() * 2.0 - 1.0;

            rumbleL = rumbleL * 0.95 + whiteL * 0.05;
            rumbleR = rumbleR * 0.95 + whiteR * 0.05;

            // Vinyl crackle impulses
            double crackle = 0;
            if (rng.NextDouble() < 0.0009)
            {
                crackle = (rng.NextDouble() * 2.0 - 1.0) * 0.75;
            }

            double left = (rumbleL * 0.35 + crackle) * volume * envelope;
            double right = (rumbleR * 0.35 + crackle * 0.8) * volume * envelope;

            short leftShort = (short)Math.Clamp(left * 32767.0, -32768.0, 32767.0);
            short rightShort = (short)Math.Clamp(right * 32767.0, -32768.0, 32767.0);

            bw.Write(leftShort);
            bw.Write(rightShort);
        }
    }

    /// <summary>
    /// Cross-fades the first and last 0.05 seconds of the loop to eliminate any clicking/pops.
    /// </summary>
    private static double GetLoopEnvelope(int sampleIndex, int totalSamples)
    {
        int fadeSamples = (int)(SampleRate * 0.05); // 50ms fade
        if (sampleIndex < fadeSamples)
        {
            return (double)sampleIndex / fadeSamples;
        }
        if (sampleIndex > totalSamples - fadeSamples)
        {
            return (double)(totalSamples - sampleIndex) / fadeSamples;
        }
        return 1.0;
    }

    public void Dispose()
    {
        StopFocusTimer();
        StopInternal();
    }
}
