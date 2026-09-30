using System.Collections.Generic;
using UnityEngine;

namespace ShanFlyer.UIEffects
{
    // Managed validation is separate from BakeMesh so corrupt native snapshots are never
    // indexed or submitted, and an empty/teleported trail cannot freeze an old picture.
    internal sealed class LineSnapshotValidator
    {
        internal enum Result { Valid, Empty, Corrupt, Discontinuity }
        private bool _hasPrevious;
        private Bounds _previousBounds;
        private float _previousMaxEdge;
        private int _rejected;
        internal bool expired => _rejected >= 3;

        internal void Reset() { _hasPrevious = false; _rejected = 0; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal Result Evaluate(List<Vector3> vertices, List<int> indices, bool checkDiscontinuity = true)
        {
            if (vertices.Count == 0 || indices.Count == 0) { Reset(); return Result.Empty; }
            if (vertices.Count < 3 || indices.Count < 3 || indices.Count % 3 != 0)
                return Reject(Result.Corrupt);
            var bounds = new Bounds(vertices[0], Vector3.zero);
            for (var i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z)) return Reject(Result.Corrupt);
                bounds.Encapsulate(v);
            }
            float maxEdge = 0;
            for (var i = 0; i < indices.Count; i += 3)
            {
                var a = indices[i]; var b = indices[i + 1]; var c = indices[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Count || b >= vertices.Count || c >= vertices.Count)
                    return Reject(Result.Corrupt);
                maxEdge = Mathf.Max(maxEdge, (vertices[a] - vertices[b]).sqrMagnitude);
                maxEdge = Mathf.Max(maxEdge, (vertices[b] - vertices[c]).sqrMagnitude);
                maxEdge = Mathf.Max(maxEdge, (vertices[c] - vertices[a]).sqrMagnitude);
            }
            maxEdge = Mathf.Sqrt(maxEdge);
            if (!Finite(maxEdge) || !Finite(bounds.size.magnitude)) return Reject(Result.Corrupt);
            var reference = Mathf.Max(0.001f, Mathf.Max(_previousBounds.size.magnitude, bounds.size.magnitude));
            if (checkDiscontinuity && _hasPrevious && _rejected < 3 &&
                ((bounds.center - _previousBounds.center).magnitude > reference * 3
                 || (bounds.size - _previousBounds.size).magnitude > reference * 3
                 || maxEdge > Mathf.Max(0.001f, _previousMaxEdge * 4)))
                return Reject(Result.Discontinuity);
            _previousBounds = bounds;
            _previousMaxEdge = maxEdge;
            _hasPrevious = true;
            _rejected = 0;
            return Result.Valid;
        }

        private Result Reject(Result result) { _rejected = Mathf.Min(3, _rejected + 1); return result; }
    }
}
