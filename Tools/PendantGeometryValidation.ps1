param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$motionSource = Get-Content -Raw -LiteralPath (Join-Path $ProjectRoot 'Assets/RoboticsLab/Scripts/Pendant/LabPendantMotionPath.cs')
# Only Vector3 storage is stubbed: the unchanged production source performs all
# geometry. No Unity Editor, scene, model, or IK state is involved in these tests.
$vectorStub = @'
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
}
'@
$checksSource = @'
using System;
using System.Collections.Generic;
using RoboticsLab;
using UnityEngine;

public static class PendantGeometryChecks
{
    static readonly List<string> passed = new List<string>();
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Geometry check failed: " + name);
        passed.Add(name);
    }
    static double Distance(Vector3 a, Vector3 b)
    {
        double x = (double)a.x - b.x, y = (double)a.y - b.y, z = (double)a.z - b.z;
        return Math.Sqrt(x * x + y * y + z * z);
    }
    static bool Near(double actual, double expected, double epsilon = 0.00001)
    { return Math.Abs(actual - expected) <= epsilon; }
    static bool Exact(Vector3 a, Vector3 b)
    { return a.x == b.x && a.y == b.y && a.z == b.z; }
    static Vector3 V(float x, float y, float z = 0f) { return new Vector3(x, y, z); }
    static Vector3 Circle(double degrees, double radius = 1.0)
    {
        double radians = degrees * Math.PI / 180.0;
        return V((float)(radius * Math.Cos(radians)), (float)(radius * Math.Sin(radians)));
    }
    static Vector3 FramePoint(double[] center, double[] u, double[] v, double radius, double angle)
    {
        return V((float)(center[0] + radius * (u[0] * Math.Cos(angle) + v[0] * Math.Sin(angle))),
            (float)(center[1] + radius * (u[1] * Math.Cos(angle) + v[1] * Math.Sin(angle))),
            (float)(center[2] + radius * (u[2] * Math.Cos(angle) + v[2] * Math.Sin(angle))));
    }
    static void RandomizedArcs()
    {
        // Generate circles independently from the three-point fitting algorithm.
        // Their known centres, planes, direction, and sweep are the oracle.
        var random = new Random(24601);
        for (int i = 0; i < 1000; i++)
        {
            double yaw = random.NextDouble() * Math.PI * 2;
            double pitch = random.NextDouble() * Math.PI * 2;
            var u = new[] { Math.Cos(yaw), Math.Sin(yaw), 0.0 };
            var v = new[] { -Math.Sin(yaw) * Math.Cos(pitch), Math.Cos(yaw) * Math.Cos(pitch), Math.Sin(pitch) };
            var center = new[] { random.NextDouble() * 4 - 2, random.NextDouble() * 4 - 2, random.NextDouble() * 4 - 2 };
            double radius = 0.03 + random.NextDouble() * 1.5;
            double startAngle = random.NextDouble() * Math.PI * 2;
            double sweep = (0.2 + random.NextDouble() * 5.8) * (i % 2 == 0 ? 1 : -1);
            double viaFraction = 0.1 + random.NextDouble() * 0.8;
            Vector3 start = FramePoint(center, u, v, radius, startAngle);
            Vector3 via = FramePoint(center, u, v, radius, startAngle + sweep * viaFraction);
            Vector3 end = FramePoint(center, u, v, radius, startAngle + sweep);
            LabPendantMotionPath path;
            string error;
            if (!LabPendantMotionPath.TryArc(start, via, end, out path, out error))
                throw new Exception("Random C " + i + " rejected: " + error);
            if (!Near(path.Length, radius * Math.Abs(sweep), 0.00003)
                || !Exact(path.EvaluateDistance(path.ViaDistance), via))
                throw new Exception("Random C " + i + " has wrong length or via point.");
            Vector3 minimum, maximum;
            if (!path.TryGetBounds(out minimum, out maximum))
                throw new Exception("Random C " + i + " has no finite bounds.");
            for (int j = 1; j < 10; j++)
            {
                Vector3 expected = FramePoint(center, u, v, radius, startAngle + sweep * j / 10);
                Vector3 actual = path.EvaluateDistance(path.Length * j / 10);
                if (Distance(actual, expected) > 0.00003)
                    throw new Exception("Random C " + i + " deviates from its known circle at sample " + j);
                if (actual.x < minimum.x || actual.y < minimum.y || actual.z < minimum.z
                    || actual.x > maximum.x || actual.y > maximum.y || actual.z > maximum.z)
                    throw new Exception("Random C " + i + " leaves its analytic bounds at sample " + j);
            }
        }
        Check(true, "C: 1000 seeded arbitrary-plane circles match independent position and length oracle");
    }
    static LabPendantMotionPath Arc(Vector3 start, Vector3 via, Vector3 end, string name)
    {
        LabPendantMotionPath result;
        string error;
        Check(LabPendantMotionPath.TryArc(start, via, end, out result, out error)
            && result != null && error == null && result.IsArc, name + ": accepted");
        Check(Exact(result.EvaluateDistance(0), start), name + ": exact start");
        Check(Exact(result.EvaluateDistance(result.Length), end), name + ": exact end");
        Check(Exact(result.EvaluateDistance(result.ViaDistance), via), name + ": exact via");
        return result;
    }
    static void Rejected(Vector3 start, Vector3 via, Vector3 end, string name)
    {
        LabPendantMotionPath result;
        string error;
        Check(!LabPendantMotionPath.TryArc(start, via, end, out result, out error)
            && result == null && !string.IsNullOrWhiteSpace(error), name);
    }
    public static string[] Run()
    {
        passed.Clear();
        LabPendantMotionPath path;
        string error;
        Vector3 a = V(1, 2, 3), b = V(4, 6, 3);
        Check(LabPendantMotionPath.TryLine(a, b, out path, out error)
            && error == null && !path.IsArc, "L: valid line");
        Check(Near(path.Length, 5), "L: Euclidean length");
        Check(Distance(path.EvaluateDistance(2.5f), V(2.5f, 4, 3)) < 0.000001,
            "L: constant distance interpolation");
        Check(Exact(path.EvaluateDistance(-1), a) && Exact(path.EvaluateDistance(99), b),
            "L: exact clamped endpoints");
        Check(path.ViaDistance == 0 && Exact(path.Via, a), "L: no via point");
        Check(LabPendantMotionPath.TryLine(a, a, out path, out error) && path.Length == 0
            && Exact(path.EvaluateDistance(10), a), "L: zero length is legal");
        Check(!LabPendantMotionPath.TryLine(V(float.NaN, 0), b, out path, out error)
            && path == null && error != null, "L: reject NaN");
        Check(!LabPendantMotionPath.TryLine(a, V(0, float.PositiveInfinity), out path, out error),
            "L: reject infinity");
        Check(!LabPendantMotionPath.TryLine(V(float.MaxValue, 0), V(-float.MaxValue, 0),
            out path, out error) && error != null, "L: reject unrepresentable length");

        path = Arc(V(1, 0), V(0, 1), V(-1, 0), "C: counterclockwise semicircle");
        Check(Near(path.Length, Math.PI) && Near(path.ViaDistance, Math.PI / 2),
            "C: semicircle length and via distance");
        Check(Distance(path.EvaluateDistance(path.Length / 4), Circle(45)) < 0.000001,
            "C: curved quarter point, not a two-line approximation");
        double firstChord = Distance(path.EvaluateDistance(0), path.EvaluateDistance(path.Length / 32));
        bool onCircle = true, equalSpeed = true;
        for (int i = 1; i <= 32; i++)
        {
            Vector3 previous = path.EvaluateDistance(path.Length * (i - 1) / 32);
            Vector3 current = path.EvaluateDistance(path.Length * i / 32);
            onCircle &= Near(Distance(current, V(0, 0)), 1, 0.000001);
            equalSpeed &= Near(Distance(previous, current), firstChord, 0.000001);
        }
        Check(onCircle, "C: all samples lie on circle");
        Check(equalSpeed, "C: equal arc lengths give equal chords");
        Check(Exact(path.EvaluateDistance(-10), path.Start)
            && Exact(path.EvaluateDistance(100), path.End), "C: exact clamped endpoints");

        path = Arc(V(1, 0), V(0, -1), V(-1, 0), "C: clockwise semicircle");
        Check(Distance(path.EvaluateDistance(path.Length / 4), Circle(-45)) < 0.000001,
            "C: clockwise direction follows via");
        path = Arc(V(1, 0), V(-1, 0), V(0, 1), "C: 270 degree long arc");
        Check(Near(path.Length, Math.PI * 1.5) && Near(path.ViaDistance, Math.PI),
            "C: long arc length and via distance");
        Check(Distance(path.EvaluateDistance(path.Length / 2), Circle(-135)) < 0.000001,
            "C: long arc takes the side containing via");
        path = Arc(Circle(350), Circle(5), Circle(20), "C: crosses zero degrees");
        Check(Near(path.Length, Math.PI / 6) && Near(path.ViaDistance, Math.PI / 12),
            "C: wrapped angle has short 30 degree sweep");
        Vector3 boundsMin, boundsMax;
        Check(path.TryGetBounds(out boundsMin, out boundsMax) && boundsMax.x >= 1
            && boundsMax.x <= 1.000001 && Near(boundsMin.y, Math.Sin(-Math.PI / 18), 0.000001)
            && Near(boundsMax.y, Math.Sin(Math.PI / 9), 0.000001),
            "C: analytic bounds include extremum between taught points");

        double u = 1 / Math.Sqrt(2), v = 1 / Math.Sqrt(6);
        Vector3 tiltedStart = V((float)(2 + 2 * u), (float)(-3 + 2 * u), 4);
        Vector3 tiltedVia = V((float)(2 - 2 * v), (float)(-3 + 2 * v), (float)(4 + 4 * v));
        Vector3 tiltedEnd = V((float)(2 - 2 * u), (float)(-3 - 2 * u), 4);
        path = Arc(tiltedStart, tiltedVia, tiltedEnd, "C: tilted translated 3D plane");
        Check(Near(path.Length, Math.PI * 2, 0.00001), "C: tilted arc radius and length");
        bool inPlane = true;
        for (int i = 0; i <= 20; i++)
        {
            Vector3 p = path.EvaluateDistance(path.Length * i / 20);
            // The normal is (1,-1,1)/sqrt(3), so x-y+z is constant.
            inPlane &= Near(p.x - p.y + p.z, 9, 0.000003);
        }
        Check(inPlane, "C: samples remain in recorded 3D plane");
        path = Arc(V(10020, -20000, 30000), V(10000, -19980, 30000),
            V(9980, -20000, 30000), "C: large translated center");
        Check(Near(path.Length, Math.PI * 20, 0.00001), "C: translated geometry avoids cancellation");
        path = Arc(Circle(0, 0.00002), Circle(90, 0.00002), Circle(180, 0.00002),
            "C: small valid radius");
        Check(Near(path.Length, Math.PI * 0.00002, 0.0000000001), "C: small radius retains scale");
        path = Arc(V(0, 0), V(1, 0.01f), V(2, 0), "C: shallow but stable arc");
        Check(path.Length > 2 && path.Length < 2.001, "C: shallow arc has correct length");

        Rejected(V(0, 0), V(0, 0), V(1, 1), "C: reject repeated start/via");
        Rejected(V(0, 0), V(1, 1), V(1, 1), "C: reject repeated via/end");
        Rejected(V(0, 0), V(1, 1), V(0, 0), "C: full circle needs two arcs");
        Rejected(V(0, 0), V(1, 0), V(2, 0), "C: reject collinear points");
        Rejected(V(0, 0), V(1, 0.0000001f), V(2, 0), "C: reject near-collinear points");
        Rejected(Circle(0, 0.000005), Circle(90, 0.000005), Circle(180, 0.000005),
            "C: reject radius below 0.01 mm");
        Rejected(V(0, 0), V(float.NaN, 1), V(1, 1), "C: reject NaN");
        Rejected(V(0, 0), V(0, 1), V(1, float.NegativeInfinity), "C: reject infinity");
        bool rejectedDistance = false;
        try { path.EvaluateDistance(float.NaN); }
        catch (ArgumentOutOfRangeException) { rejectedDistance = true; }
        Check(rejectedDistance, "sampling: reject invalid distance");
        rejectedDistance = false;
        try { path.EvaluateDistance(float.PositiveInfinity); }
        catch (ArgumentOutOfRangeException) { rejectedDistance = true; }
        Check(rejectedDistance, "sampling: reject infinite distance");
        RandomizedArcs();
        return passed.ToArray();
    }
}
'@

# Put shared using directives first; the production code body remains unchanged.
$sharedUsings = "using System;`nusing System.Collections.Generic;`nusing RoboticsLab;`nusing UnityEngine;`n"
Add-Type -TypeDefinition ($sharedUsings + $vectorStub + "`n" + ($motionSource -replace '(?m)^using (System|UnityEngine);\r?\n', '') + "`n" + ($checksSource -replace '(?m)^using (System|System.Collections.Generic|RoboticsLab|UnityEngine);\r?\n', ''))
$passedChecks = [PendantGeometryChecks]::Run()
[pscustomobject]@{
    passed = $true
    count = $passedChecks.Count
    checks = $passedChecks
} | ConvertTo-Json -Depth 4
