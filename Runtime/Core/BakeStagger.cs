using System.Collections.Generic;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    // Stable offsets: 0, 1/2, 1/4, 3/4, ... spread sequential registrations across a period.
    // Shared groups keep an offset across owner changes; bridges retain their instance offset.
    internal static class BakeStagger
    {
        private static uint sequence;
        private static readonly Dictionary<int, double> groups = new Dictionary<int, double>();
        private static readonly HashSet<int> alive = new HashSet<int>();
        private static readonly List<int> retired = new List<int>();
        internal static double Allocate() => Phase(sequence++);
        internal static double Phase(uint index)
        {
            double result = 0, weight = .5;
            while (index != 0) { if ((index & 1) != 0) result += weight; index >>= 1; weight *= .5; }
            return result;
        }
        internal static void Prepare(List<UIEffectRenderer> effects)
        {
            alive.Clear();
            foreach (var effect in effects)
            {
                if (!effect || !effect.isActiveAndEnabled || !effect.canvas) continue;
                effect.particleBakePhase = effect.independentBakePhase;
                if (!effect.useMeshSharing) continue;
                int id = effect.sharingGroup;
                if (!groups.TryGetValue(id, out double phase)) groups.Add(id, phase = Allocate());
                alive.Add(id); effect.particleBakePhase = phase;
            }
            retired.Clear();
            foreach (var pair in groups) if (!alive.Contains(pair.Key)) retired.Add(pair.Key);
            foreach (int id in retired) groups.Remove(id);
        }
        internal static long Tick(int frame, int percentage, double phase) =>
            (long)System.Math.Floor((double)frame * percentage / 100 - phase);
        internal static void Reset() { sequence = 0; groups.Clear(); alive.Clear(); retired.Clear(); }
    }
}
