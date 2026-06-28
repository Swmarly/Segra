using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;
using Serilog;

namespace Segra.Backend.Media
{
    internal static class NativePlaybackAudioService
    {
        private static readonly object Lock = new();
        private static MediaFoundationReader? _reader;
        private static IWavePlayer? _output;
        private static VolumeSampleProvider? _volumeProvider;
        private static string? _currentPath;
        private static double _lastRequestedTime;
        private static DateTime _lastCorrectionUtc = DateTime.MinValue;
        private const int OutputLatencyMs = 150;
        private const double DriftToleranceSeconds = 0.45;
        private static readonly TimeSpan MinCorrectionInterval = TimeSpan.FromSeconds(1);

        public static void Sync(
            string? filePath,
            double timeSeconds,
            bool playing,
            float volume,
            bool muted,
            double playbackRate,
            bool forceSeek = false)
        {
            lock (Lock)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    {
                        StopLocked();
                        return;
                    }

                    if (!string.Equals(_currentPath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        StopLocked();
                        _reader = new MediaFoundationReader(filePath);
                        _volumeProvider = new VolumeSampleProvider(_reader.ToSampleProvider());
                        _output = new WasapiOut(AudioClientShareMode.Shared, true, OutputLatencyMs);
                        _output.Init(_volumeProvider.ToWaveProvider());
                        _currentPath = filePath;
                        forceSeek = true;
                    }

                    if (_reader == null || _output == null) return;

                    if (_volumeProvider != null)
                    {
                        _volumeProvider.Volume = muted ? 0 : Math.Clamp(volume, 0, 1);
                    }

                    // NAudio's MediaFoundationReader path does not provide clean realtime varispeed for MP4.
                    // Keep the native process audio path correct at normal speed and prevent drift/noise at
                    // preview speeds where the WebView's visual clock is intentionally altered.
                    if (Math.Abs(playbackRate - 1) > 0.01)
                    {
                        _output.Pause();
                        if (forceSeek || Math.Abs(_lastRequestedTime - timeSeconds) > 0.25)
                        {
                            SeekLocked(timeSeconds);
                        }
                        _lastRequestedTime = timeSeconds;
                        return;
                    }

                    if (forceSeek)
                    {
                        SeekLocked(timeSeconds);
                    }
                    else if (playing && !muted)
                    {
                        // MediaFoundationReader.CurrentTime reflects decoded/read position, not
                        // exactly what has reached the speakers. Treat it as a coarse health check
                        // and correct only when drift is large and sustained; frequent seeks are
                        // much more audible than small clock error.
                        var heardSeconds = _reader.CurrentTime.TotalSeconds - OutputLatencyMs / 1000.0;
                        var drift = heardSeconds - timeSeconds;
                        var now = DateTime.UtcNow;
                        if (Math.Abs(drift) > DriftToleranceSeconds &&
                            now - _lastCorrectionUtc >= MinCorrectionInterval)
                        {
                            SeekLocked(timeSeconds);
                            _lastCorrectionUtc = now;
                        }
                    }
                    _lastRequestedTime = timeSeconds;

                    if (playing && !muted)
                    {
                        if (_output.PlaybackState != PlaybackState.Playing)
                        {
                            _output.Play();
                        }
                    }
                    else if (_output.PlaybackState == PlaybackState.Playing)
                    {
                        _output.Pause();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Native playback audio sync failed");
                    StopLocked();
                }
            }
        }

        public static void Stop()
        {
            lock (Lock)
            {
                StopLocked();
            }
        }

        private static void SeekLocked(double timeSeconds)
        {
            if (_reader == null) return;

            var clamped = Math.Max(0, Math.Min(timeSeconds, _reader.TotalTime.TotalSeconds));
            _reader.CurrentTime = TimeSpan.FromSeconds(clamped);
        }

        private static void StopLocked()
        {
            try
            {
                _output?.Stop();
            }
            catch
            {
                // ignore
            }

            _output?.Dispose();
            _reader?.Dispose();
            _output = null;
            _volumeProvider = null;
            _reader = null;
            _currentPath = null;
            _lastRequestedTime = 0;
            _lastCorrectionUtc = DateTime.MinValue;
        }
    }
}
