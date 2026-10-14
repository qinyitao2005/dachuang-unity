using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoboticsLab.Editor
{
    /// <summary>Deterministic geometry/scoring checks; motion itself is verified in Play Mode.</summary>
    public static class LabTracingValidation
    {
        [MenuItem("Robot Lab/Diagnostics/Validate Tracing Rules")]
        public static void Run() { Debug.Log(Validate()); }

        public static string Validate()
        {
            var report = new List<string>();
            var size = new Vector2(.19f, .27f);
            int failures = 0;
            Action<bool, string> check = (ok, label) => { report.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; };
            for (int shape = 0; shape < 5; shape++)
            {
                var path = LabTracingPaths.Create(shape, size, Vector2.zero);
                bool bounds = path.Length > 20;
                foreach (var p in path) bounds &= Mathf.Abs(p.x) < size.x * .49f && Mathf.Abs(p.y) < size.y * .49f;
                check(bounds, "Shape " + shape + " lies inside paper layout");
                check(shape == 0 || Vector2.Distance(path[0], path[path.Length - 1]) < 1e-6f, "Automatic shape " + shape + " closes");
                var score = new LabTracingScore(path, .003f);
                foreach (var p in path) score.Sample(p, true);
                check(score.Completed && score.Coverage > 99.9f && score.Score > 99,
                    "Ideal path " + shape + " completion=" + score.Coverage.ToString("F1") + " score=" + score.Score.ToString("F1"));
            }
            var circle = LabTracingPaths.Create(1, size, Vector2.zero);
            var center = Vector2.Scale(new Vector2(.04f, .255f), size);
            float rmin = float.MaxValue, rmax = 0;
            foreach (var p in circle) { float r = Vector2.Distance(p, center); rmin = Mathf.Min(rmin, r); rmax = Mathf.Max(rmax, r); }
            check(rmax - rmin < .0001f, "Circle remains circular on rectangular paper");
            var square = LabTracingPaths.Create(3, size, Vector2.zero);
            Vector2 lo = square[0], hi = square[0];
            foreach (var p in square) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
            check(Mathf.Abs((hi.x - lo.x) - (hi.y - lo.y)) < 1e-6f, "Square sides have equal physical lengths");
            var complex = LabTracingPaths.Create(4, size, Vector2.zero);
            lo = hi = complex[0];
            foreach (var p in complex) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
            check((hi.x - lo.x) / (hi.y - lo.y) > 2, "Photo curve is a broad outline rather than a distorted tall contour");

            var j = LabTracingPaths.Create(0, size, Vector2.zero);
            var partial = new LabTracingScore(j, .003f);
            for (int i = 0; i < j.Length / 4; i++) partial.Sample(j[i], true);
            check(partial.Coverage > 15 && partial.Coverage < 35 && partial.Score < 35, "Incomplete work cannot receive a high success score");
            var stationary = new LabTracingScore(j, .003f);
            for (int i = 0; i < 1000; i++) stationary.Sample(j[0], true);
            check(stationary.Coverage == 0 && stationary.Score == 0, "Stationary samples cannot farm coverage or accuracy");
            var airborne = new LabTracingScore(j, .003f);
            foreach (var p in j) airborne.Sample(p, false);
            check(airborne.Coverage == 0 && airborne.Score == 0, "Pen-up movement earns no progress");
            var shortcut = new LabTracingScore(j, .003f);
            shortcut.Sample(j[0], true); shortcut.Sample(j[j.Length - 1], true);
            check(shortcut.Coverage == 0, "Jump from start to finish does not complete ordered checkpoints");
            var reverse = new LabTracingScore(circle, .003f);
            reverse.Sample(circle[0], true);
            for (int i = circle.Length - 1; i >= 0; i--) reverse.Sample(circle[i], true);
            check(reverse.Coverage < 10, "Traversing a closed path in reverse cannot skip its ordered course");
            var off = new LabTracingScore(j, .003f);
            off.Sample(j[0], true);
            foreach (var p in j) off.Sample(p + new Vector2(.012f, 0), true);
            check(off.Score < 35 && off.Accuracy < 50, "Off-path movement lowers accuracy and success");
            var backtrack = new LabTracingScore(j, .003f);
            for (int i = 0; i < j.Length / 3; i++) backtrack.Sample(j[i], true);
            for (int i = j.Length / 3 - 1; i >= 0; i--) backtrack.Sample(j[i], true);
            foreach (var p in j) backtrack.Sample(p, true);
            check(backtrack.Completed && backtrack.Score > 99, "Safe backtracking permits later completion without duplicate coverage");

            var straight = new Vector2[101];
            for (int i = 0; i < straight.Length; i++) straight[i] = new Vector2(i * .001f, 0);
            var errorScore = new LabTracingScore(straight, .003f);
            errorScore.Sample(straight[0], true);
            for (int i = 1; i < straight.Length; i++) errorScore.Sample(straight[i] + Vector2.up * .0015f, true);
            check(Mathf.Abs(errorScore.Accuracy - 50) < .1f && Mathf.Abs(errorScore.MeanErrorMm - 1.5f) < .01f,
                "Known 1.5 mm deviation with 3 mm tolerance yields 50% accuracy");
            float before = errorScore.Accuracy;
            for (int i = 0; i < 1000; i++) errorScore.Sample(straight[100] + Vector2.up * .0015f, true);
            check(Mathf.Abs(before - errorScore.Accuracy) < 1e-6f, "Paused samples do not change distance-weighted accuracy");
            Directory.CreateDirectory("Documentation");
            File.WriteAllLines("Documentation/TracingValidation.txt", report);
            string result = "TRACING_RULES " + (report.Count - failures) + "/" + report.Count + " checks passed";
            if (failures > 0) throw new Exception(result + "\n" + string.Join("\n", report));
            return result;
        }
    }
}
