using NAudio.Wave;
using Serilog;

namespace Segra.Backend.Media
{
    internal static class NativePlaybackAudioService
    {
        private static readonly object Lock = new();
        private static MediaFoundationReader? _reader;
        private static WaveOutEvent? _output;
        private static string? _currentPath;
        private static double _lastRequestedTime;
        private const int OutputLatencyMs = 80;
        private const double DriftToleranceSeconds = 0.12;

        public static void Sync(
            string? filePath,
            double timeSeconds,
            bool playing,
            float volume,
            bool muted,
            double playbackRate)
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
                        _output = new WaveOutEvent
                        {
                            DesiredLatency = OutputLatencyMs,
                            NumberOfBuffers = 2
                        };
                        _output.Init(_reader);
                        _currentPath = filePath;
                    }

                    if (_reader == null || _output == null) return;

                    _output.Volume = muted ? 0 : Math.Clamp(volume, 0, 1);

                    // NAudio's MediaFoundationReader path does not provide clean realtime varispeed for MP4.
                    // Keep the native process audio path correct at normal speed and prevent drift/noise at
                    // preview speeds where the WebView's visual clock is intentionally altered.
                    if (Math.Abs(playbackRate - 1) > 0.01)
                    {
                        _output.Pause();
                        SeekLocked(timeSeconds);
                        _lastRequestedTime = timeSeconds;
                        return;
                    }

                    // WaveOut buffers audio ahead of the speaker, so seek slightly ahead of the
                    // video clock and keep the correction window tight enough to avoid audible lag.
                    var targetSeconds = timeSeconds + OutputLatencyMs / 1000.0;
                    var currentSeconds = _reader.CurrentTime.TotalSeconds;
                    if (Math.Abs(currentSeconds - targetSeconds) > DriftToleranceSeconds ||
                        Math.Abs(_lastRequestedTime - timeSeconds) > 2.0)
                    {
                        SeekLocked(targetSeconds);
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
            _reader = null;
            _currentPath = null;
            _lastRequestedTime = 0;
        }
    }
}
