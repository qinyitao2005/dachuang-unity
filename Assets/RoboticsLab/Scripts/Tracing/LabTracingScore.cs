using UnityEngine;

namespace RoboticsLab
{
    /// <summary>Ordered checkpoint coverage plus distance-weighted path accuracy. No robot or UI dependencies.</summary>
    public sealed class LabTracingScore
    {
        readonly Vector2[] path;
        readonly float[] lengths;
        readonly float tolerance;
        int next;
        bool hasPrevious;
        Vector2 previous;
        float travelled, weightedError, accurateDistance;
        public float Coverage => path.Length < 2 ? 0 : next >= path.Length ? 100 : 100 * lengths[Mathf.Max(0, next - 1)] / lengths[path.Length - 1];
        public float Accuracy => travelled > .0001f ? Mathf.Clamp01(accurateDistance / travelled) * 100 : 0;
        public float Score => Coverage * Accuracy * .01f;
        public float MeanErrorMm => travelled > .0001f ? weightedError / travelled * 1000 : 0;
        public float CurrentErrorMm { get; private set; }
        public bool Started => next > 0;
        public bool Completed => next >= path.Length && path.Length > 1;
        public int NextCheckpoint => Mathf.Clamp(next, 0, Mathf.Max(0, path.Length - 1));

        public LabTracingScore(Vector2[] source, float toleranceMetres)
        {
            path = source ?? new Vector2[0];
            tolerance = Mathf.Max(.0001f, toleranceMetres);
            lengths = new float[path.Length];
            for (int i = 1; i < path.Length; i++) lengths[i] = lengths[i - 1] + Vector2.Distance(path[i - 1], path[i]);
        }

        public void Sample(Vector2 point, bool onPaper)
        {
            if (path.Length < 2) return;
            float error = float.PositiveInfinity;
            for (int i = 1; i < path.Length; i++) error = Mathf.Min(error, LabTracingPaths.DistanceToSegment(point, path[i - 1], path[i]));
            CurrentErrorMm = error * 1000;
            if (!onPaper) { hasPrevious = false; return; }
            if (!Started)
            {
                if (Vector2.Distance(point, path[0]) > tolerance) { previous = point; hasPrevious = true; return; }
                next = 1;
                previous = point;
                hasPrevious = true;
                return;
            }
            if (!hasPrevious) { previous = point; hasPrevious = true; return; }
            float step = Vector2.Distance(previous, point);
            if (step < .00002f) return; // Holding still cannot improve either score or coverage.
            travelled += step;
            weightedError += error * step;
            accurateDistance += Mathf.Clamp01(1 - error / tolerance) * step;
            // A discontinuity (teleport/large frame jump) cannot sweep through or skip checkpoints.
            if (step <= .018f)
            {
                float budget = step * 1.4f + .0011f;
                while (next < path.Length && budget > 0 &&
                    LabTracingPaths.DistanceToSegment(path[next], previous, point) <= tolerance)
                {
                    float length = Vector2.Distance(path[next - 1], path[next]);
                    if (length > budget + .000001f) break;
                    budget -= length;
                    next++;
                }
            }
            previous = point;
        }
    }
}
