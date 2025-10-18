using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace eft_dma_shared.Common.Misc;

/// <summary>
/// Lightweight production profiler for identifying performance bottlenecks in real-time.
/// Low overhead design suitable for continuous production use.
/// </summary>
public class PerformanceProfiler
{
    private static readonly Lazy<PerformanceProfiler> _instance = new(() => new PerformanceProfiler());
    public static PerformanceProfiler Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, ProfileSection> _sections = new();
    private readonly Stopwatch _frameSw = Stopwatch.StartNew();
    private readonly object _lock = new();

    private bool _enabled = false;
    private int _frameCount = 0;
    private long _lastFrameTime = 0;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
            {
                Reset();
            }
        }
    }

    public int FrameCount => _frameCount;
    public double LastFrameMs => (_frameSw.ElapsedTicks - _lastFrameTime) * 1000.0 / Stopwatch.Frequency;

    private PerformanceProfiler() { }

    /// <summary>
    /// Marks the start of a new frame. Call this at the beginning of each render loop.
    /// </summary>
    public void BeginFrame()
    {
        if (!_enabled) return;

        lock (_lock)
        {
            _lastFrameTime = _frameSw.ElapsedTicks;
            _frameCount++;
        }
    }

    /// <summary>
    /// Begins timing a named section. Returns a disposable token that automatically ends timing.
    /// </summary>
    public ProfileToken BeginSection(string name)
    {
        if (!_enabled) return ProfileToken.Empty;

        return new ProfileToken(this, name);
    }

    private void RecordSectionTime(string sectionName, long elapsedTicks)
    {
        if (!_enabled) return;

        var section = _sections.GetOrAdd(sectionName, name => new ProfileSection(name));
        section.RecordSample(elapsedTicks);
    }

    private void EndSection(ProfileSection section, long elapsedTicks)
    {
        if (!_enabled) return;

        section.RecordSample(elapsedTicks);
    }

    /// <summary>
    /// Gets current performance statistics for all profiled sections.
    /// </summary>
    public ProfileStats GetStats()
    {
        if (!_enabled) return new ProfileStats();

        var sectionStats = _sections.Values
            .Select(s => s.GetStats())
            .OrderByDescending(s => s.AverageMs)
            .ToList();

        return new ProfileStats
        {
            TotalFrames = _frameCount,
            LastFrameMs = LastFrameMs,
            Sections = sectionStats
        };
    }

    /// <summary>
    /// Resets all profiling data.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _sections.Clear();
            _frameCount = 0;
            _lastFrameTime = _frameSw.ElapsedTicks;
        }
    }

    /// <summary>
    /// Exports profiling data to JSON for offline analysis.
    /// </summary>
    public string ExportJson()
    {
        var stats = GetStats();
        return JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true });
    }

    public readonly struct ProfileToken : IDisposable
    {
        private readonly PerformanceProfiler _profiler;
        private readonly string _sectionName;
        private readonly long _startTicks;

        public static readonly ProfileToken Empty = default;

        internal ProfileToken(PerformanceProfiler profiler, string sectionName)
        {
            _profiler = profiler;
            _sectionName = sectionName;
            _startTicks = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_profiler == null || string.IsNullOrEmpty(_sectionName)) return;

            var elapsed = Stopwatch.GetTimestamp() - _startTicks;
            _profiler.RecordSectionTime(_sectionName, elapsed);
        }
    }

    private class ProfileSection
    {
        private readonly string _name;
        private readonly object _lock = new();
        private long _totalTicks = 0;
        private long _minTicks = long.MaxValue;
        private long _maxTicks = 0;
        private int _sampleCount = 0;
        private const int ROLLING_WINDOW_SIZE = 60; // Track last 60 frames
        private readonly Queue<long> _recentSamples = new(ROLLING_WINDOW_SIZE);

        public ProfileSection(string name)
        {
            _name = name;
        }

        public void RecordSample(long ticks)
        {
            lock (_lock)
            {
                _totalTicks += ticks;
                _sampleCount++;

                if (ticks < _minTicks) _minTicks = ticks;
                if (ticks > _maxTicks) _maxTicks = ticks;

                _recentSamples.Enqueue(ticks);
                if (_recentSamples.Count > ROLLING_WINDOW_SIZE)
                {
                    _recentSamples.Dequeue();
                }
            }
        }

        public SectionStats GetStats()
        {
            lock (_lock)
            {
                if (_sampleCount == 0)
                {
                    return new SectionStats { Name = _name };
                }

                var avgTicks = _totalTicks / _sampleCount;
                var recentAvgTicks = _recentSamples.Count > 0
                    ? (long)_recentSamples.Average()
                    : avgTicks;

                return new SectionStats
                {
                    Name = _name,
                    SampleCount = _sampleCount,
                    AverageMs = avgTicks * 1000.0 / Stopwatch.Frequency,
                    RecentAverageMs = recentAvgTicks * 1000.0 / Stopwatch.Frequency,
                    MinMs = _minTicks * 1000.0 / Stopwatch.Frequency,
                    MaxMs = _maxTicks * 1000.0 / Stopwatch.Frequency
                };
            }
        }
    }

    public class ProfileStats
    {
        public int TotalFrames { get; set; }
        public double LastFrameMs { get; set; }
        public List<SectionStats> Sections { get; set; } = new();
    }

    public class SectionStats
    {
        public string Name { get; set; } = "";
        public int SampleCount { get; set; }
        public double AverageMs { get; set; }
        public double RecentAverageMs { get; set; }
        public double MinMs { get; set; }
        public double MaxMs { get; set; }
    }
}
