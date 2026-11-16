using System;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>One explicitly armed assessment. Observes samples; never commands the robot.</summary>
    public sealed class LabTracingAssessment
    {
        public enum Phase { Ready, Waiting, Sampling, Finished, Cancelled }
        public const float HeightLimit = .010f;
        public const float CourseTolerance = .006f;
        public const float FullAccuracyTolerance = .001f;
        public const float ZeroAccuracyTolerance = .006f;
        readonly Vector2[] path;
        readonly float[] distance;
        readonly LabTracingSessionHistory history;
        readonly int shape;
        Vector2 previous;
        bool hasPrevious, reachedStart;
        float progress, travelled, qualityDistance, errorDistance;
        public Phase State { get; private set; }
        public bool Active => State == Phase.Waiting || State == Phase.Sampling;
        public bool Completed { get; private set; }
        public bool HasResult => State == Phase.Finished;
        public float Coverage => Completed ? 100f : distance.Length < 2 || distance[distance.Length - 1] <= 0 ? 0f
            : Mathf.Clamp01(progress / distance[distance.Length - 1]) * 100f;
        public float Accuracy => travelled > .0001f ? Mathf.Clamp01(qualityDistance / travelled) * 100f : 0f;
        public float Score => Coverage * Accuracy * .01f;
        public float MeanErrorMm => travelled > .0001f ? errorDistance / travelled * 1000f : 0f;
        public float CurrentErrorMm { get; private set; }

        public LabTracingAssessment(Vector2[] course, int shape, LabTracingSessionHistory history = null)
        {
            if (course == null || course.Length < 2) throw new ArgumentException("A course needs at least two points.");
            path = (Vector2[])course.Clone();
            distance = new float[path.Length];
            for (int i = 1; i < path.Length; i++) distance[i] = distance[i - 1] + Vector2.Distance(path[i - 1], path[i]);
            this.shape = shape;
            this.history = history;
        }

        public bool Arm()
        {
            if (State != Phase.Ready) return false;
            State = Phase.Waiting;
            return true;
        }

        public void Tick(Vector2 point, float height)
        {
            if (!Active || float.IsNaN(height) || float.IsInfinity(height)
                || float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y)) return;
            if (State == Phase.Waiting)
            {
                if (height >= HeightLimit) return;
                State = Phase.Sampling;
            }
            if (height > HeightLimit) { Finish(false); return; }
            Sample(point);
            if (Coverage >= 95f && reachedStart && travelled >= distance[distance.Length - 1] * .85f
                && Vector2.Distance(point, path[path.Length - 1]) <= CourseTolerance)
                Finish(true);
        }

        void Sample(Vector2 point)
        {
            float error = float.MaxValue;
            for (int i = 1; i < path.Length; i++)
                error = Mathf.Min(error, LabTracingPaths.DistanceToSegment(point, path[i - 1], path[i]));
            CurrentErrorMm = error * 1000f;
            float step = hasPrevious ? Vector2.Distance(previous, point) : 0f;
            if (!reachedStart && Vector2.Distance(point, path[0]) <= CourseTolerance) reachedStart = true;
            if (step >= .00002f)
            {
                travelled += step;
                errorDistance += error * step;
                qualityDistance += Mathf.Clamp01(1f - (error - FullAccuracyTolerance)
                    / (ZeroAccuracyTolerance - FullAccuracyTolerance)) * step;
            }
            // Only a continuous forward portion of the course can earn coverage.
            // Backtracking cannot duplicate it; teleports cannot bridge missing sections.
            if (reachedStart && step <= .018f && (step >= .00002f || !hasPrevious))
            {
                float budget = Mathf.Max(.001f, step * 1.8f);
                float bestProgress = progress, bestError = CourseTolerance;
                for (int i = 1; i < path.Length; i++)
                {
                    if (distance[i] < progress - CourseTolerance || distance[i - 1] > progress + budget) continue;
                    Vector2 segment = path[i] - path[i - 1];
                    float t = segment.sqrMagnitude <= 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(point - path[i - 1], segment) / segment.sqrMagnitude);
                    float along = Mathf.Lerp(distance[i - 1], distance[i], t);
                    float gap = Vector2.Distance(point, path[i - 1] + t * segment);
                    if (along > progress && along <= progress + budget && gap < bestError)
                    { bestError = gap; bestProgress = along; }
                }
                progress = bestProgress;
            }
            // Accumulate tiny movement instead of making frame rate affect accuracy.
            if (!hasPrevious || step >= .00002f) { previous = point; hasPrevious = true; }
        }

        void Finish(bool completed)
        {
            if (!Active) return;
            Completed = completed;
            State = Phase.Finished;
            history?.Record(shape, Score);
        }

        public void Cancel() { if (Active) State = Phase.Cancelled; }
    }

    /// <summary>Session memory only: no PlayerPrefs or files. Clearing an attempt never clears this table.</summary>
    public sealed class LabTracingSessionHistory
    {
        readonly float[] best = new float[5];
        readonly int[] attempts = new int[5];
        public float Best(int shape) => best[shape];
        public int Attempts(int shape) => attempts[shape];
        public void Record(int shape, float score)
        {
            if (shape < 0 || shape >= best.Length || float.IsNaN(score) || float.IsInfinity(score)) return;
            best[shape] = Mathf.Max(best[shape], Mathf.Clamp(score, 0f, 100f));
            attempts[shape]++;
        }
        public void Clear() { Array.Clear(best, 0, best.Length); Array.Clear(attempts, 0, attempts.Length); }
    }
}
