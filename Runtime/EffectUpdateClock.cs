namespace ShanFlyer.UIEffects
{
    // Keep scheduling time separate from simulation time. A consumed delta must
    // never remain in the accumulator and be simulated a second time.
    internal struct EffectUpdateClock
    {
        private float _scaled;
        private float _unscaled;
        private long _lastTick;
        private bool _tickStarted;
        private int _percentage;
        private double _phase;
        private bool _scheduleStarted;

        public bool AdvanceScheduled(float scaled, float unscaled, int frame, int percentage, double phase,
            bool force, out float scaledStep, out float unscaledStep)
        {
            // A new cadence has a different tick domain. Drain accumulated simulation time once.
            if (!_scheduleStarted || _percentage != percentage || _phase != phase)
            {
                _tickStarted = false; _percentage = percentage; _phase = phase; _scheduleStarted = true;
            }
            return AdvanceAtTick(scaled, unscaled, BakeStagger.Tick(frame, percentage, phase), force,
                out scaledStep, out unscaledStep);
        }

        // Tick offsets change eligibility, never the accumulated simulation delta.
        public bool AdvanceAtTick(float scaled, float unscaled, long tick, bool force,
            out float scaledStep, out float unscaledStep)
        {
            _scaled += FiniteDelta(scaled);
            _unscaled += FiniteDelta(unscaled);
            scaledStep = unscaledStep = 0;
            if (_tickStarted && _lastTick == tick && !force) return false;
            _tickStarted = true;
            _lastTick = tick;
            scaledStep = _scaled; unscaledStep = _unscaled;
            _scaled = _unscaled = 0;
            return true;
        }

        public void Reset() { this = default; }

        public void AdvanceEveryFrame(float scaled, float unscaled,
            out float scaledStep, out float unscaledStep)
        {
            // Drain any pending limited-rate time before changing cadence.
            scaledStep = _scaled + FiniteDelta(scaled);
            unscaledStep = _unscaled + FiniteDelta(unscaled);
            this = default;
        }

        private static float FiniteDelta(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0 ? 0 : value;
        }
    }
}
