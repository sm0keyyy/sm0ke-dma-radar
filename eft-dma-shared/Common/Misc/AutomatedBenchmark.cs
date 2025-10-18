using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace eft_dma_shared.Common.Misc;

/// <summary>
/// Automated performance benchmark system that tests all LOD levels systematically.
/// Captures average, 0.1%, and 1% low frame times for detailed performance analysis.
/// </summary>
public class AutomatedBenchmark
{
    private static readonly Lazy<AutomatedBenchmark> _instance = new(() => new AutomatedBenchmark());
    public static AutomatedBenchmark Instance => _instance.Value;

    private readonly object _lock = new();
    private BenchmarkState _state = BenchmarkState.Idle;
    private int _currentLODLevel = 0;
    private List<double> _frameTimesMs = new();
    private Stopwatch _phaseSw = new();
    private Dictionary<int, LODResult> _results = new();

    // Benchmark configuration
    private const int WARMUP_FRAMES = 120; // 2 seconds at 60fps
    private const int SAMPLE_FRAMES = 600; // 10 seconds at 60fps
    private const double ZOOM_SETTLE_TIME_MS = 500; // Wait for zoom to settle

    public bool IsRunning => _state != BenchmarkState.Idle && _state != BenchmarkState.Complete;
    public BenchmarkState State => _state;
    public int CurrentLOD => _currentLODLevel;
    public int Progress => _state switch
    {
        BenchmarkState.WarmingUp => (int)((_frameTimesMs.Count / (double)WARMUP_FRAMES) * 100),
        BenchmarkState.Sampling => (int)((_frameTimesMs.Count / (double)SAMPLE_FRAMES) * 100),
        _ => 0
    };

    private AutomatedBenchmark() { }

    /// <summary>
    /// Starts the automated benchmark. Will test LOD 0, 1, and 2.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning)
            {
                LoneLogging.WriteLine("Benchmark already running!");
                return;
            }

            // Enable profiler during benchmark to capture DMA thread metrics
            // We'll use this data but exclude profiler overhead from frame time calculations
            if (!PerformanceProfiler.Instance.Enabled)
            {
                LoneLogging.WriteLine("[Benchmark] Enabling performance profiler to capture DMA thread metrics");
                PerformanceProfiler.Instance.Enabled = true;
                PerformanceProfiler.Instance.Reset();
            }

            LoneLogging.WriteLine("=== STARTING AUTOMATED BENCHMARK ===");
            LoneLogging.WriteLine("This will test LOD 0, 1, and 2 sequentially.");
            LoneLogging.WriteLine($"Each LOD: {WARMUP_FRAMES} warmup frames + {SAMPLE_FRAMES} sample frames");

            _state = BenchmarkState.ZoomingToLOD0;
            _currentLODLevel = 0;
            _frameTimesMs.Clear();
            _results.Clear();
            _phaseSw = Stopwatch.StartNew();
        }
    }

    /// <summary>
    /// Call this every frame with the current frame time and LOD level.
    /// Returns the target zoom level the benchmark wants, or null if not running.
    /// </summary>
    public int? Update(double frameTimeMs, int currentLOD)
    {
        lock (_lock)
        {
            if (!IsRunning) return null;

            switch (_state)
            {
                case BenchmarkState.ZoomingToLOD0:
                case BenchmarkState.ZoomingToLOD1:
                case BenchmarkState.ZoomingToLOD2:
                    // Wait for zoom to settle
                    if (_phaseSw.ElapsedMilliseconds < ZOOM_SETTLE_TIME_MS)
                        return _currentLODLevel;

                    // Check if we're at the target LOD
                    if (currentLOD != _currentLODLevel)
                        return _currentLODLevel;

                    // Zoom settled, start warmup
                    LoneLogging.WriteLine($"LOD {_currentLODLevel} reached, warming up...");
                    _state = BenchmarkState.WarmingUp;
                    _frameTimesMs.Clear();
                    _phaseSw.Restart();
                    return _currentLODLevel;

                case BenchmarkState.WarmingUp:
                    _frameTimesMs.Add(frameTimeMs);
                    if (_frameTimesMs.Count >= WARMUP_FRAMES)
                    {
                        LoneLogging.WriteLine($"LOD {_currentLODLevel} warmup complete, sampling...");
                        _state = BenchmarkState.Sampling;
                        _frameTimesMs.Clear();
                        _phaseSw.Restart();
                    }
                    return _currentLODLevel;

                case BenchmarkState.Sampling:
                    _frameTimesMs.Add(frameTimeMs);
                    if (_frameTimesMs.Count >= SAMPLE_FRAMES)
                    {
                        // Calculate results for this LOD
                        var result = CalculateLODResult(_currentLODLevel);
                        _results[_currentLODLevel] = result;

                        LoneLogging.WriteLine($"LOD {_currentLODLevel} complete: Avg={result.AverageMs:F2}ms, 1%={result.OnePercentLowMs:F2}ms, 0.1%={result.ZeroPointOnePercentLowMs:F2}ms");

                        // Move to next LOD or finish
                        _currentLODLevel++;
                        if (_currentLODLevel > 2)
                        {
                            FinishBenchmark();
                            return null;
                        }
                        else
                        {
                            _state = (BenchmarkState)((int)BenchmarkState.ZoomingToLOD0 + _currentLODLevel);
                            _frameTimesMs.Clear();
                            _phaseSw.Restart();
                            LoneLogging.WriteLine($"Moving to LOD {_currentLODLevel}...");
                            return _currentLODLevel;
                        }
                    }
                    return _currentLODLevel;
            }

            return null;
        }
    }

    private LODResult CalculateLODResult(int lodLevel)
    {
        var sorted = _frameTimesMs.OrderBy(x => x).ToList();
        var count = sorted.Count;

        var average = sorted.Average();
        var min = sorted.First();
        var max = sorted.Last();

        // Calculate percentiles
        var onePercentIndex = (int)(count * 0.01);
        var zeroPointOnePercentIndex = (int)(count * 0.001);

        var onePercentLow = sorted.Skip(0).Take(Math.Max(1, onePercentIndex)).Average();
        var zeroPointOnePercentLow = sorted.Skip(0).Take(Math.Max(1, zeroPointOnePercentIndex)).Average();

        // Capture DMA thread profiling stats
        var dmaThreadStats = new Dictionary<string, ThreadTimingStats>();
        var profilerStats = PerformanceProfiler.Instance.GetStats();

        // Filter for DMA worker threads (T1, T2, T4) and their sub-sections
        var dmaThreadNames = new[] { "T1 Realtime Loop", "T2 Misc Loop", "T4 Fast Loop",
                                      "  T2 Loot", "  T2 Gear", "  T2 Exfils", "  T2 ValidateTransforms",
                                      "  T2 Wishlist", "  T2 Quests", "  T4 Hands (Batched)", "  T4 Firearm" };

        foreach (var section in profilerStats.Sections.Where(s => dmaThreadNames.Any(n => s.Name.Contains(n))))
        {
            dmaThreadStats[section.Name] = new ThreadTimingStats
            {
                ThreadName = section.Name,
                AverageMs = section.AverageMs,
                RecentAverageMs = section.RecentAverageMs,
                MinMs = section.MinMs,
                MaxMs = section.MaxMs,
                SampleCount = section.SampleCount,
                PercentageOfFrame = (section.AverageMs / average) * 100.0
            };
        }

        return new LODResult
        {
            LODLevel = lodLevel,
            SampleCount = count,
            AverageMs = average,
            MinMs = min,
            MaxMs = max,
            OnePercentLowMs = onePercentLow,
            ZeroPointOnePercentLowMs = zeroPointOnePercentLow,
            AverageFPS = 1000.0 / average,
            OnePercentLowFPS = 1000.0 / onePercentLow,
            ZeroPointOnePercentLowFPS = 1000.0 / zeroPointOnePercentLow,
            DMAThreadStats = dmaThreadStats
        };
    }

    private void FinishBenchmark()
    {
        _state = BenchmarkState.Complete;

        LoneLogging.WriteLine("\n=== BENCHMARK COMPLETE ===");
        LoneLogging.WriteLine($"{"LOD",-4} {"Avg FPS",8} {"1% Low",8} {"0.1% Low",8} | {"Avg ms",8} {"1% ms",8} {"0.1% ms",8} | {"Min ms",8} {"Max ms",8}");
        LoneLogging.WriteLine(new string('-', 110));

        foreach (var result in _results.Values.OrderBy(r => r.LODLevel))
        {
            LoneLogging.WriteLine($"{result.LODLevel,-4} {result.AverageFPS,8:F1} {result.OnePercentLowFPS,8:F1} {result.ZeroPointOnePercentLowFPS,8:F1} | " +
                                 $"{result.AverageMs,8:F2} {result.OnePercentLowMs,8:F2} {result.ZeroPointOnePercentLowMs,8:F2} | " +
                                 $"{result.MinMs,8:F2} {result.MaxMs,8:F2}");

            // Display DMA thread stats for this LOD
            if (result.DMAThreadStats.Any())
            {
                LoneLogging.WriteLine($"\n  DMA Thread Performance (LOD {result.LODLevel}):");
                LoneLogging.WriteLine($"  {"Thread",-30} {"Avg ms",10} {"Recent",10} {"Min ms",10} {"Max ms",10} {"Samples",10} {"% Frame",8}");
                LoneLogging.WriteLine($"  {new string('-', 95)}");

                var orderedStats = result.DMAThreadStats.Values
                    .OrderByDescending(s => s.AverageMs);

                foreach (var stat in orderedStats)
                {
                    LoneLogging.WriteLine($"  {stat.ThreadName,-30} {stat.AverageMs,10:F2} {stat.RecentAverageMs,10:F2} " +
                                         $"{stat.MinMs,10:F2} {stat.MaxMs,10:F2} {stat.SampleCount,10} {stat.PercentageOfFrame,7:F1}%");
                }
                LoneLogging.WriteLine(""); // Empty line separator
            }
        }

        // Export to JSON
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var benchmarkPath = System.IO.Path.Combine(desktopPath, "radar_benchmark_automated.json");

            var exportData = new
            {
                BenchmarkDate = DateTime.Now,
                WarmupFrames = WARMUP_FRAMES,
                SampleFrames = SAMPLE_FRAMES,
                Results = _results.Values.OrderBy(r => r.LODLevel).ToList()
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(benchmarkPath, json);

            LoneLogging.WriteLine($"\nDetailed results exported to: {benchmarkPath}");
        }
        catch (Exception ex)
        {
            LoneLogging.WriteLine($"Error exporting benchmark: {ex}");
        }

        _state = BenchmarkState.Idle;
    }

    public void Cancel()
    {
        lock (_lock)
        {
            if (IsRunning)
            {
                LoneLogging.WriteLine("Benchmark cancelled by user.");
                _state = BenchmarkState.Idle;
            }
        }
    }

    public enum BenchmarkState
    {
        Idle,
        ZoomingToLOD0,
        ZoomingToLOD1,
        ZoomingToLOD2,
        WarmingUp,
        Sampling,
        Complete
    }

    public class LODResult
    {
        public int LODLevel { get; set; }
        public int SampleCount { get; set; }
        public double AverageMs { get; set; }
        public double MinMs { get; set; }
        public double MaxMs { get; set; }
        public double OnePercentLowMs { get; set; }
        public double ZeroPointOnePercentLowMs { get; set; }
        public double AverageFPS { get; set; }
        public double OnePercentLowFPS { get; set; }
        public double ZeroPointOnePercentLowFPS { get; set; }

        // DMA Thread Performance Metrics
        public Dictionary<string, ThreadTimingStats> DMAThreadStats { get; set; } = new();
    }

    public class ThreadTimingStats
    {
        public string ThreadName { get; set; } = "";
        public double AverageMs { get; set; }
        public double RecentAverageMs { get; set; }
        public double MinMs { get; set; }
        public double MaxMs { get; set; }
        public int SampleCount { get; set; }
        public double PercentageOfFrame { get; set; }
    }
}
