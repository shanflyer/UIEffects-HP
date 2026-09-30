using System.Diagnostics;

namespace ShanFlyer.UIEffects
{
    // Value-only counters. Sampling never enables Unity's full profiler or allocates per frame.
    public static class UIEffectProfiler
    {
        public const string Implementation = "ui-effects-1.0.0-independent";
        public struct Frame
        {
            public int directBakeOps;
            public int groupPlanRebuilds;
            public int frame, bakeOps, setMeshOps, materialUpdates, meshesCreated;
            public int bridgeCacheHits, maskMeshSubmissions, maskResolveCacheHits;
            public int meshDepthGroups;
            public int mergeLayoutChanges;
            public int activeRenderers, mergedRenderers, fallbackEffects;
            public long bakedVertices;
            public double prepareMs, simulateMs, bakeMs, combineMs, submitMs, bridgeCompareMs;
            public bool detailedTiming;
        }

        public static Frame current;
        public static Frame completed { get; private set; }
        public static bool collecting { get; private set; }
        private static int s_TimingRequests;
        public static bool timingEnabled => collecting && current.detailedTiming;

        /// <summary>
        /// Opt into detailed wall-clock timing on the main thread. Dispose to stop.
        /// Requests are reference counted and take effect at the next particle frame.
        /// Counters remain available without enabling detailed timing.
        /// </summary>
        public static System.IDisposable BeginDetailedTiming() => new TimingRequest();

        private sealed class TimingRequest : System.IDisposable
        {
            private bool _disposed;
            public TimingRequest() { s_TimingRequests++; }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                s_TimingRequests--;
            }
        }

        public static long Timestamp() => timingEnabled ? Stopwatch.GetTimestamp() : 0;
        public static double Milliseconds(long start) => timingEnabled
            ? (Stopwatch.GetTimestamp() - start) * (1000.0 / Stopwatch.Frequency) : 0;
        internal static void BeginFrame(int frame)
        {
            current = new Frame { frame = frame, detailedTiming = s_TimingRequests > 0 };
            collecting = true;
        }
        internal static void EndFrame() { completed = current; collecting = false; }
        internal static Measurement Measure(byte stage) => new Measurement(stage);
        internal readonly struct Measurement : System.IDisposable
        {
            private readonly byte stage;
            private readonly long started;
            internal Measurement(byte stage)
            {
                this.stage = stage;
                started = Timestamp();
                string label = stage switch
                {
                    1 => "UI Effects/Simulation", 2 => "UI Effects/Capture",
                    3 => "UI Effects/Geometry", _ => "UI Effects/Submit"
                };
                UnityEngine.Profiling.Profiler.BeginSample(label);
            }
            public void Dispose()
            {
                EndStage(stage, started);
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }
        internal static void EndStage(byte stage, long started)
        {
            if (!timingEnabled || stage == 0) return;
            var ms = Milliseconds(started);
            switch (stage)
            {
                case 1: current.simulateMs += ms; break;
                case 2: current.bakeMs += ms; break;
                case 3: current.combineMs += ms; break;
                case 4: current.submitMs += ms; break;
            }
        }
    }
}
