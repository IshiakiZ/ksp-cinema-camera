using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace CineCamera
{
    /// <summary>
    /// A path for the camera to fly: a few points, each a place, a way of looking and a lens, set by the player with the
    /// camera where the shot should pass. They are kept relative to the ship, in the frame the game's own camera was keeping
    /// to when the first of them was set (see Turn), so a path goes along with the ship wherever it is flown: round a rocket
    /// on the pad, beside a plane in flight, past a station in orbit.
    ///
    /// The camera goes through the points in order, the same time from each to the next, starting and stopping gently. The
    /// line it takes is a smooth curve through them (a centripetal Catmull-Rom spline, which neither overshoots a point nor
    /// loops between two close ones); the way it looks is the same curve taken through which way each point looks and which
    /// way is up for it; the lens is drawn in or out evenly in stops of the angle of view.
    /// </summary>
    sealed class CinePath
    {
        public sealed class Point
        {
            public Vector3 At;          // where, from the ship's middle, in the path's frame (metres)
            public Quaternion Aim;      // which way the camera looks, in the path's frame
            public float Lens;          // the focal length, millimetres
        }

        /// <summary>
        /// How the points turn with the ship, as the game's own camera turns in the mode it was in when the path was begun:
        /// the free camera (the usual one near the ground) keeps north north and up up; the orbital one keeps to the stars;
        /// the locked one turns with the ship itself; the chase one turns with the way the ship is heading.
        /// </summary>
        public enum Turn { North, Stars, Ship, Heading }

        public static readonly string[] TurnSaid =
        {
            "north stays north (as the game's free camera keeps it)",
            "the stars stay where they are (as the game's orbital camera keeps it)",
            "they turn with the ship (as the game's locked camera does)",
            "they turn with the way the ship heads (as the game's chase camera does)",
        };

        public const int Most = 12;

        public readonly List<Point> Points = new List<Point>();
        public Turn Turns;

        /// <summary>The frame the game's camera is keeping to now.</summary>
        public static Turn TurnOf(FlightCamera camera)
        {
            FlightCamera.Modes mode = camera.mode == FlightCamera.Modes.AUTO ? camera.autoMode : camera.mode;
            switch (mode)
            {
                case FlightCamera.Modes.ORBITAL: return Turn.Stars;
                case FlightCamera.Modes.LOCKED: return Turn.Ship;
                case FlightCamera.Modes.CHASE: return Turn.Heading;
                default: return Turn.North;
            }
        }

        /// <summary>The path's frame as it is now, for this ship: as the game works out its own camera's (FlightGlobals.getFoR), without the smoothing of the chase camera.</summary>
        public static Quaternion Frame(Turn turn, Vessel ship)
        {
            Transform t = ship.ReferenceTransform != null ? ship.ReferenceTransform : ship.transform;
            Vector3 up = ((Vector3)FlightGlobals.getUpAxis(ship.mainBody, t.position)).normalized;
            switch (turn)
            {
                case Turn.Stars:
                    return Quaternion.identity;
                case Turn.Ship:
                    return Quaternion.LookRotation(t.up, -t.forward);
                case Turn.Heading:
                    {
                        Vector3 ahead = Vector3.ProjectOnPlane((t.up + t.forward * 0.1f).normalized, up);
                        if (ahead.sqrMagnitude < 1e-8f) ahead = Vector3.ProjectOnPlane(t.forward, up);
                        if (ahead.sqrMagnitude < 1e-8f) return North(up);
                        return Quaternion.LookRotation(ahead.normalized, up);
                    }
                default:
                    return North(up);
            }
        }

        /// <summary>Level, facing north (the world's own axis is the game's "up" for the whole scene).</summary>
        static Quaternion North(Vector3 up)
        {
            Vector3 north = Vector3.ProjectOnPlane(Vector3.up, up);
            // (at the very poles there is no north: any level way will do)
            if (north.sqrMagnitude < 1e-8f) north = Vector3.ProjectOnPlane(Vector3.forward, up);
            return Quaternion.LookRotation(north.normalized, up);
        }

        // ---- flying it

        /// <summary>
        /// How far along the path the camera is after this share of the path's time (0 to 1): it gathers speed over the first
        /// fifth, keeps it, and slows to a stop over the last fifth, as a camera on a crane or a dolly is moved.
        /// </summary>
        static float Ease(float t)
        {
            const float A = 0.2f, Top = 1f / (1f - A);
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            if (t < A) return Top * t * t / (2f * A);
            if (t > 1f - A) return 1f - Top * (1f - t) * (1f - t) / (2f * A);
            return Top * (0.5f * A + (t - A));
        }

        /// <summary>The place, the aim and the lens at this share of the path's time (0 to 1), in the path's frame.</summary>
        public void At(float done, out Vector3 at, out Quaternion aim, out float lens)
        {
            int n = Points.Count;
            if (n == 1) { at = Points[0].At; aim = Points[0].Aim; lens = Points[0].Lens; return; }
            float s = Ease(Mathf.Clamp01(done)) * (n - 1);
            int i = Mathf.Min((int)s, n - 2);
            float u = Mathf.Clamp01(s - i);
            Point b = Points[i], c = Points[i + 1];
            Point a = i > 0 ? Points[i - 1] : null, d = i + 2 < n ? Points[i + 2] : null;

            // (past either end: as far again, the way the path was going there)
            at = Spline(a != null ? a.At : 2f * b.At - c.At, b.At, c.At, d != null ? d.At : 2f * c.At - b.At, u);
            // Each point's aim is taken against looking at the ship's middle from that point, and that is what goes along the curve: so
            // a path whose points all look at the ship looks at it all the way, however it swings round it, and one that looks beside
            // the ship at a point keeps it as far to one side there. (Taken through the curve as they are, aims a quarter turn apart
            // round the ship let it drift halfway out of the picture between two points.)
            Quaternion qb = Against(b), qc = Against(c);
            Quaternion qa = a != null ? Against(a) : qb, qd = d != null ? Against(d) : qc;
            Vector3 fb = qb * Vector3.forward, fc = qc * Vector3.forward, ub = qb * Vector3.up, uc = qc * Vector3.up;
            Vector3 ahead = Spline(a != null ? qa * Vector3.forward : 2f * fb - fc, fb, fc, d != null ? qd * Vector3.forward : 2f * fc - fb, u);
            Vector3 up = Spline(a != null ? qa * Vector3.up : 2f * ub - uc, ub, uc, d != null ? qd * Vector3.up : 2f * uc - ub, u);
            Quaternion off = ahead.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(ahead.normalized, up.sqrMagnitude > 1e-8f ? up.normalized : ub) : Quaternion.Slerp(qb, qc, u);
            aim = Looking(at) * off;

            // (in stops of the angle of view, which is evenly in the logarithm of the focal length; never beyond the two it is between)
            float lb = Mathf.Log(b.Lens), lc = Mathf.Log(c.Lens);
            float la = a != null ? Mathf.Log(a.Lens) : 2f * lb - lc, ld = d != null ? Mathf.Log(d.Lens) : 2f * lc - lb;
            float l = Hermite(la, lb, lc, ld, u);
            lens = Mathf.Exp(Mathf.Clamp(l, Mathf.Min(lb, lc), Mathf.Max(lb, lc)));
        }

        /// <summary>Looking at the ship's middle from a place in the path's frame, level in it: the way the game's own camera looks from there. (Straight over or under the ship, where there is no level, with its top towards the frame's "ahead".)</summary>
        static Quaternion Looking(Vector3 at)
        {
            if (at.sqrMagnitude < 1e-8f) return Quaternion.identity;
            Vector3 to = -at.normalized;
            return Quaternion.LookRotation(to, Mathf.Abs(to.y) < 0.999f ? Vector3.up : Vector3.forward);
        }

        /// <summary>A point's aim, against looking at the ship from where it is.</summary>
        static Quaternion Against(Point p) => Quaternion.Inverse(Looking(p.At)) * p.Aim;

        /// <summary>A centripetal Catmull-Rom spline through b and c (a and d the points either side), at u from b (0) to c (1).</summary>
        static Vector3 Spline(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u)
        {
            float t0 = 0f, t1 = t0 + Knot(a, b), t2 = t1 + Knot(b, c), t3 = t2 + Knot(c, d);
            float t = Mathf.Lerp(t1, t2, u);
            Vector3 a1 = Mix(a, b, t0, t1, t), a2 = Mix(b, c, t1, t2, t), a3 = Mix(c, d, t2, t3, t);
            Vector3 b1 = Mix(a1, a2, t0, t2, t), b2 = Mix(a2, a3, t1, t3, t);
            return Mix(b1, b2, t1, t2, t);
        }

        // (the square root of the distance: what makes it centripetal; never quite nothing, or two points in one place divide by it)
        static float Knot(Vector3 p, Vector3 q) => Mathf.Max(Mathf.Sqrt((q - p).magnitude), 1e-3f);

        static Vector3 Mix(Vector3 p, Vector3 q, float tp, float tq, float t) => (tq - t) / (tq - tp) * p + (t - tp) / (tq - tp) * q;

        /// <summary>The same for one number, evenly spaced (the lens).</summary>
        static float Hermite(float a, float b, float c, float d, float u)
        {
            float mb = 0.5f * (c - a), mc = 0.5f * (d - b), u2 = u * u, u3 = u2 * u;
            return (2f * u3 - 3f * u2 + 1f) * b + (u3 - 2f * u2 + u) * mb + (-2f * u3 + 3f * u2) * c + (u3 - u2) * mc;
        }

        // ---- kept as text (in the camera's settings file, under the craft's name)

        static readonly CultureInfo Plain = CultureInfo.InvariantCulture;

        /// <summary>One line: the frame, then each point as eight numbers (place, aim, lens).</summary>
        public string Write()
        {
            var text = new StringBuilder();
            text.Append(((int)Turns).ToString(Plain));
            foreach (Point p in Points)
            {
                text.Append(';');
                text.Append(p.At.x.ToString("0.###", Plain)).Append(',').Append(p.At.y.ToString("0.###", Plain)).Append(',').Append(p.At.z.ToString("0.###", Plain)).Append(',');
                text.Append(p.Aim.x.ToString("0.#####", Plain)).Append(',').Append(p.Aim.y.ToString("0.#####", Plain)).Append(',').Append(p.Aim.z.ToString("0.#####", Plain)).Append(',').Append(p.Aim.w.ToString("0.#####", Plain)).Append(',');
                text.Append(p.Lens.ToString("0.##", Plain));
            }
            return text.ToString();
        }

        /// <summary>A path as Write put it (nothing, or anything that cannot be read, is an empty path).</summary>
        public void Read(string text)
        {
            Points.Clear();
            Turns = Turn.North;
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                string[] parts = text.Split(';');
                int turn = int.Parse(parts[0], Plain);
                Turns = turn >= 0 && turn <= 3 ? (Turn)turn : Turn.North;
                for (int n = 1; n < parts.Length && Points.Count < Most; n++)
                {
                    string[] f = parts[n].Split(',');
                    if (f.Length < 8) continue;
                    var v = new float[8];
                    for (int k = 0; k < 8; k++) v[k] = float.Parse(f[k], NumberStyles.Float, Plain);
                    var aim = new Quaternion(v[3], v[4], v[5], v[6]);
                    float size = Mathf.Sqrt(aim.x * aim.x + aim.y * aim.y + aim.z * aim.z + aim.w * aim.w);
                    if (!(size > 0.5f) || float.IsNaN(v[0] + v[1] + v[2]) || !(v[7] > 0f)) continue;
                    Points.Add(new Point { At = new Vector3(v[0], v[1], v[2]), Aim = new Quaternion(aim.x / size, aim.y / size, aim.z / size, aim.w / size), Lens = v[7] });
                }
            }
            catch (Exception) { Points.Clear(); }
        }

        /// <summary>The name a craft's path is kept under: its name, with anything but letters and figures made an underscore.</summary>
        public static string KeyFor(string craft)
        {
            var key = new StringBuilder("path_");
            foreach (char ch in craft ?? "")
            {
                if (key.Length >= 64) break;
                key.Append(ch < 128 && char.IsLetterOrDigit(ch) ? ch : '_');
            }
            return key.ToString();
        }
    }
}
