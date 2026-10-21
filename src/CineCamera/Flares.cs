using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CineCamera
{
    /// <summary>
    /// (Since 0.7.0.) Lens flares from lamps shining into the lens: the floodlights on the pad at night, a craft's own lamps, the
    /// lights of a runway. (The sun has its flare already: the game's own, or Scatterer's with its ghosts where that is installed;
    /// TUFX, looked at for this, has none. Nothing is added for it.)
    ///
    /// What a real lens does with a bright light in view: a soft glare round it (light scattered in the glass and off the barrel);
    /// rays out from it, the aperture's diffraction, as many as its blades (an even number of blades: as many rays; an odd number:
    /// twice as many), longer and sharper the more the lens is stopped down and barely there wide open, where the aperture is near
    /// round; and ghosts, faint images of the aperture's shape thrown by reflections between the lens's glasses, along the line from
    /// the light through the middle of the picture, on the far side of it more than the near, each tinted by its coatings. All of it
    /// only while the light itself is in sight: hidden behind something, there is no flare; a spot lamp flares only from inside its
    /// beam.
    ///
    /// Which lamps: only lamps one can see, for a flare from where nothing is drawn would give it away: a craft's own (the game's
    /// lamp parts), and Natural Light's floodlights round a pad or a runway (searchlights: their beam along the line from the glass
    /// through the light it throws, some six degrees each side of it) and its strobes. Not the many lights that only light a place
    /// with nothing drawn where they are: the space centre's, a flame's light thrown on what is round it, the light in at a window.
    ///
    /// How strong, as for a real lamp: by how much of its light reaches the lens. Its strength, gathered into its beam (along its
    /// middle a searchlight's six degrees gather it some 365 times over, a lamp part's wider beam some tens of times, a bare
    /// strobe not at all; from outside the beam, only its glass's own glow), over the square of its distance, and against how
    /// bright the rest of the picture is (a lamp hardly flares in full sun: by night a picture takes some ten thousand times the
    /// light). Taken in tenfolds, as film and the eye take light: each tenfold more grows the glare and the rays by as much, from
    /// nothing at the least that shows to all of it six tenfolds up; a searchlight seen along its beam flares fully from
    /// hundreds of metres, and still shows from kilometres.
    ///
    /// Here: the lamps looked over each second; the brightest sixteen in view each frame, eased in and out as they come into
    /// sight. Each drawn over the finished picture, added to it: its glare, its rays (eight blades: eight rays) by how far the lens
    /// is stopped down, and three ghosts by how much of the lens's character is wanted.
    /// </summary>
    static class Flares
    {
        const int MostLamps = 16, Ghosts = 3;
        static readonly float[] GhostAt = { -0.55f, -0.22f, 0.38f }, GhostSize = { 0.09f, 0.05f, 0.035f };
        static readonly Color[] GhostTint = { new Color(0.45f, 0.75f, 0.5f), new Color(0.75f, 0.45f, 0.85f), new Color(0.9f, 0.7f, 0.4f) };

        sealed class Lamp
        {
            public Light light;
            public Transform glass;                  // (where the light comes from, as drawn)
            public bool beamed;                      // (a beam along its axis, half so many degrees wide; gathered so many times along it)
            public float half, gather;
            public string kind;
            public float seen, shown, light0, off;   // (eased in sight; how much is shown; the light at the lens; degrees off its axis)
            public bool sight;
        }

        // (light at the lens, as a lamp's strength over the distance squared: the least that shows, and where a flare is fullest)
        const float Least = 1e-6f, Fullest = 0.1f;
        static readonly float Span = Mathf.Log(Fullest / Least);
        static readonly List<Lamp> lamps = new List<Lamp>();
        static float lookedAt = -10f, linesAt;
        static Camera on;
        // (when the flares are drawn: into the scene's picture after the see-through things and before the picture's effects, so
        // that a camera's own bloom and tone curve take them as they take the lamps' light. Drawn after the effects, as they were
        // first, nothing of them reached the picture on this game's camera, its anti-aliasing and post-processing between)
        internal static CameraEvent When = CameraEvent.AfterForwardAlpha;
        static CameraEvent drawnAt;
        static CommandBuffer draw;
        static Mesh quads;
        static Material glow, rays, ghost;
        static readonly List<Vector3> v = new List<Vector3>();
        static readonly List<Vector2> uv = new List<Vector2>();
        static readonly List<Color> tint = new List<Color>();
        static readonly List<int> corners = new List<int>();
        static readonly List<KeyValuePair<float, int>> order = new List<KeyValuePair<float, int>>();
        internal static string Said = "";
        // (the flares drawn by an object of their own in front of the lens, its mesh made again each frame before the camera looks
        // over the scene: given to Graphics.DrawMesh from there instead, 36 corners a frame were handed over and nothing of them
        // reached the picture, nor by a pass of the camera's own at any of three points)
        static GameObject holder;
        const string HolderName = "Cinema Camera flares";
        internal static bool ByObject = true;
#if DEV
        internal static bool TestFlare;
        // (development: the flares' material made with this shader instead, to see whether it is the material that draws nothing)
        internal static string Wanted;
        internal static string Remake(string shader)
        {
            Wanted = string.IsNullOrEmpty(shader) ? null : shader;
            Off();
            if (glow != null) Object.Destroy(glow);
            glow = null;
            Shader found = Wanted != null ? Shader.Find(Wanted) : null;
            return "the flares' material to be made with " + (Wanted == null ? "the usual shader" : Wanted + (found == null ? " (not found: the usual one)" : found.isSupported ? "" : " (not supported)"));
        }
        internal static Material Glow => glow;
        // (development: the plane the flares are laid on so many metres before the lens, instead of just past its nearest reach)
        internal static float PlaneAt;
#endif

        /// <summary>Every frame while the camera is running: the flares of the lamps in view drawn over this camera's picture, so strong (0 to 1), the lens at so many f-stops, with so much of its character.</summary>
        public static void Use(Camera camera, float amount, float fNumber, float character)
        {
            if (camera == null || amount <= 0.001f) { Off(); Said = "off"; return; }
            if (on != camera || drawnAt != When) { Off(); Begin(camera); }
            float now = Time.unscaledTime;
            if (now - lookedAt > 1f) { lookedAt = now; Look(); }
            bool lines = now - linesAt > 0.1f;
            if (lines) linesAt = now;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
            Vector3 eye = camera.transform.position;
            float aspect = Mathf.Max(0.2f, camera.aspect);
            // (the rays: barely there wide open, where the blades make a near round hole; long and sharp stopped down)
            float rayShare = Mathf.SmoothStep(0f, 1f, (fNumber - 2.8f) / 8f);
            float scene = SceneLight(eye);
            order.Clear();
            for (int i = 0; i < lamps.Count; i++)
            {
                Lamp l = lamps[i];
                l.shown = 0f;
                if (l.light == null || l.glass == null || !l.light.isActiveAndEnabled || l.light.intensity <= 0f) { l.seen = 0f; continue; }
                Vector3 at = l.glass.position, to = eye - at;
                float d = to.magnitude;
                if (d < 0.3f) continue;
                Vector3 vp = camera.WorldToViewportPoint(at);
                bool inView = vp.z > camera.nearClipPlane && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f;
                // (how much of its light comes this way: along its beam all it gathers, falling away past the beam's edge; from
                // outside it only its glass's own glow, a tenth of a bare lamp's, less the further round to its side; from behind,
                // where its glass cannot be seen, nothing)
                float gathered = 1f;
                if (l.beamed)
                {
                    l.off = Vector3.Angle(Axis(l), to);
                    float past = (l.off - l.half) / (0.4f * l.half + 1f);
                    gathered = Mathf.Max(0.1f * Mathf.SmoothStep(1f, 0f, (l.off - 60f) / 35f), l.gather * (l.off <= l.half ? 1f : Mathf.Exp(-past * past)));
                }
                l.light0 = l.light.intensity * gathered / (d * d);
                if (gathered <= 0f) { l.seen = 0f; continue; }
                float s = Mathf.Clamp01(Mathf.Log(Mathf.Max(l.light0 / scene, Least) / Least) / Span);
                // (in sight: a line to it meets nothing solid but what it is on; looked at ten times a second, eased in and out)
                bool clear = inView && s > 0.02f;
                if (clear && lines)
                {
                    Vector3 from = at + to / d * 0.35f;
                    l.sight = !(Physics.Linecast(eye, from, out RaycastHit hit, (1 << 0) | (1 << 15) | (1 << 28), QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(l.light.transform.root));
                }
                float want = clear && l.sight ? 1f : 0f;
                l.seen += (want - l.seen) * Mathf.Min(1f, 12f * dt);
                l.shown = s * l.seen;
                if (l.shown > 0.01f) order.Add(new KeyValuePair<float, int>(l.shown, i));
            }
            order.Sort((a, b) => b.Key.CompareTo(a.Key));
            // (laid out just before the camera draws, where the lamps then are on the picture: see BeforeDrawing)
            showing.Clear();
            for (int k = 0; k < order.Count && k < MostLamps; k++) showing.Add(order[k]);
            drawAmount = amount; drawRays = rayShare; drawCharacter = character;
            int drawn = showing.Count;
#if DEV
            if (TestFlare) drawn++;
#endif
            Lamp first = order.Count > 0 ? lamps[order[0].Value] : null;
            Said =
#if DEV
                planeSaid + "; " +
#endif
                lamps.Count + " lamps known, " + order.Count + " in sight, " + drawn + " flared, " + cornersDrawn + " corners drawn last (" + (glow != null ? glow.shader.name + (glow.shader.isSupported ? "" : ", not supported") : "no material") + "); rays " + rayShare.ToString("F2") + "; the scene " + Mathf.Log10(scene).ToString("F1") + " tenfolds over night"
                 + (first != null ? "; the brightest a " + first.kind + " " + (first.glass.position - eye).magnitude.ToString("F0") + " m off" + (first.beamed ? ", " + first.off.ToString("F0") + "° off its beam's middle" : "") + ", shown " + first.shown.ToString("F2") : "");
        }

        /// <summary>The line a beamed lamp shines along: a searchlight's from its glass through its light, a lamp part's its light's own.</summary>
        static Vector3 Axis(Lamp l) => l.glass == l.light.transform ? l.light.transform.forward : (l.light.transform.position - l.glass.position).normalized;

        /// <summary>
        /// How bright the scene is, as so many times a floodlit night's: by the sun's height where the camera is, night once it is
        /// six degrees below the horizon (the horizon dips as the camera rises), full day once it is six above, ten thousand times
        /// the light then (a sunlit scene against one lit by floodlights).
        /// </summary>
        static float SceneLight(Vector3 eye)
        {
            CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null, body = FlightGlobals.currentMainBody;
            if (sun == null || body == null || body == sun) return 1f;
            Vector3d from = (Vector3d)eye - body.position;
            double r = from.magnitude;
            float height = Mathf.Rad2Deg * Mathf.Asin((float)Vector3d.Dot(from / r, (sun.position - (Vector3d)eye).normalized));
            float dip = r > body.Radius ? Mathf.Rad2Deg * Mathf.Acos((float)(body.Radius / r)) : 0f;
            return Mathf.Pow(10f, 4f * Mathf.Clamp01((height + dip + 6f) / 12f));
        }

        // (what Use chose to show this frame, and how)
        static readonly List<KeyValuePair<float, int>> showing = new List<KeyValuePair<float, int>>();
        static float drawAmount, drawRays, drawCharacter;
        static readonly List<Vector3> placed = new List<Vector3>();

        /// <summary>
        /// Just before the camera looks over what to draw (the game moves its flight camera after the frame's scripts): each flare
        /// laid out where its lamp then is on the picture, in squares on a plane just in front of the lens, and given to that
        /// camera to draw as it draws anything else in front of it that light passes through. (Until 2026-10-19 they were drawn
        /// over the picture by a pass of their own, after the picture's effects or before them, and nothing of them was ever seen.)
        /// </summary>
        static void BeforeDrawing(Camera cam)
        {
            if (cam == null || cam != on || quads == null) return;
            float aspect = Mathf.Max(0.2f, cam.aspect);
            v.Clear(); uv.Clear(); tint.Clear(); corners.Clear();
#if DEV
            // (development: a white lamp's flare at full strength in the middle of the picture, to see the drawing alone)
            if (TestFlare)
            {
                Quad(Vector2.zero, GlareSize(1f), aspect, 0, Color.white * (drawAmount * GlareStrength(1f)));
                Quad(Vector2.zero, RaysSize(1f), aspect, 1, Color.white * (drawAmount * RaysStrength(1f)));
                for (int g = 0; g < Ghosts; g++) Quad(new Vector2(0.3f, 0.2f) * GhostAt[g], GhostSize[g] * 1.2f, aspect, 2, new Color(GhostTint[g].r, GhostTint[g].g, GhostTint[g].b) * (drawAmount * 0.15f * drawCharacter));
            }
#endif
            foreach (KeyValuePair<float, int> one in showing)
            {
                if (one.Value >= lamps.Count) continue;
                Lamp l = lamps[one.Value];
                if (l.light == null || l.glass == null) continue;
                float s = one.Key;
                Vector3 vp = cam.WorldToViewportPoint(l.glass.position);
                if (vp.z <= 0f) continue;
                Vector2 p = new Vector2(vp.x * 2f - 1f, vp.y * 2f - 1f);
                Color c = l.light.color;
                // (the glare, round and soft, wider the more light; the rays, by how far it is stopped down, longer the more light)
                Quad(p, GlareSize(s), aspect, 0, c * (drawAmount * GlareStrength(s)));
                if (drawRays > 0.01f) Quad(p, RaysSize(s), aspect, 1, c * (drawAmount * RaysStrength(s)));
                // (the ghosts: on the line through the middle, mostly on the far side; only from a strong light)
                if (drawCharacter > 0.01f && s > 0.3f)
                    for (int g = 0; g < Ghosts; g++)
                        Quad(p * GhostAt[g], GhostSize[g] * (0.8f + 0.4f * s), aspect, 2, new Color(c.r * GhostTint[g].r, c.g * GhostTint[g].g, c.b * GhostTint[g].b) * (drawAmount * 0.15f * s * s * drawCharacter));
            }
            // (the picture's measures turned into places on a plane a little in front of the lens)
            Transform eye = cam.transform;
            float d = Mathf.Max(0.05f, cam.nearClipPlane * 1.5f);
#if DEV
            if (PlaneAt > 0f) d = PlaneAt;
            planeSaid = "plane at " + d.ToString("F2") + " m, the camera's near " + cam.nearClipPlane.ToString("F2") + " m";
#endif
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * aspect;
            Vector3 middle = eye.position + eye.forward * d, across = eye.right * (tanX * d), upward = eye.up * (tanY * d);
            placed.Clear();
            foreach (Vector3 q in v) placed.Add(middle + across * q.x + upward * q.y);
            quads.Clear();
            quads.SetVertices(placed);
            quads.SetUVs(0, uv);
            quads.SetColors(tint);
            quads.SetTriangles(corners, 0);
            cornersDrawn = placed.Count;
            if (ByObject) { if (holder != null) holder.SetActive(placed.Count > 0); }
            else if (placed.Count > 0) Graphics.DrawMesh(quads, Matrix4x4.identity, glow, 0, cam, 0, null, false, false);
        }

        static int cornersDrawn;

        // (How big and how bright each part of a lamp's flare is drawn, by how much it shows, 0 to 1: in the picture's own measure,
        // half its height. Until 2026-10-19 the glare was at most a tenth of that across and its bright middle some five pixels,
        // the rays hairlines, and each drawn as faint as its shape squared: nothing of them was seen in five ways of drawing them,
        // and it was taken for the drawing that reached nothing. A floodlight seen along its beam at night fills a fifth of a
        // photograph with its glare and throws its rays further.)
        static float GlareSize(float s) => 0.03f + 0.3f * s;
        static float GlareStrength(float s) => 0.25f + 0.75f * s;
        static float RaysSize(float s) => (0.06f + 0.5f * s) * (0.4f + 0.6f * drawRays);
        static float RaysStrength(float s) => 0.9f * s * drawRays;
#if DEV
        internal static string planeSaid = "";
#endif

        /// <summary>One square on the picture (in the picture's own measures, -1 to 1, kept square however wide the picture), of one of the three shapes: glare, rays, ghost.</summary>
        static void Quad(Vector2 p, float size, float aspect, int shape, Color c)
        {
            int at = v.Count;
            // (at the near end of the depths, so that nothing in the picture is taken to stand in front of a flare: it is the lens's)
            float sx = size / aspect, sy = size, z = -0.999f;
            v.Add(new Vector3(p.x - sx, p.y - sy, z)); v.Add(new Vector3(p.x + sx, p.y - sy, z)); v.Add(new Vector3(p.x - sx, p.y + sy, z)); v.Add(new Vector3(p.x + sx, p.y + sy, z));
            // (the three shapes side by side in one picture: a third of it each)
            float u0 = shape / 3f, u1 = (shape + 1) / 3f;
            uv.Add(new Vector2(u0, 0f)); uv.Add(new Vector2(u1, 0f)); uv.Add(new Vector2(u0, 1f)); uv.Add(new Vector2(u1, 1f));
            c.a = 1f;
            tint.Add(c); tint.Add(c); tint.Add(c); tint.Add(c);
            corners.Add(at); corners.Add(at + 2); corners.Add(at + 1); corners.Add(at + 1); corners.Add(at + 2); corners.Add(at + 3);
        }

        /// <summary>The lamps one can see, looked over: a craft's lamp parts' lights, Natural Light's floodlights and strobes (by their names).</summary>
        static void Look()
        {
            lamps.RemoveAll(l => l.light == null || l.glass == null);
            var known = new HashSet<Light>();
            foreach (Lamp l in lamps) known.Add(l.light);
            foreach (Vessel ship in FlightGlobals.VesselsLoaded)
            {
                if (ship == null) continue;
                foreach (Part part in ship.parts)
                {
                    if (part == null) continue;
                    foreach (PartModule m in part.Modules)
                    {
                        if (!(m is ModuleLight lamp) || lamp.lights == null) continue;
                        foreach (Light light in lamp.lights)
                            if (light != null && known.Add(light))
                                lamps.Add(Of(light, light.transform, light.type == LightType.Spot, 0.5f * light.spotAngle, "lamp part"));
                    }
                }
            }
            foreach (Light light in Object.FindObjectsOfType<Light>())
            {
                if (light == null || known.Contains(light)) continue;
                Transform glass = light.transform.parent;
                if (glass != null && light.name == "light" && glass.name.StartsWith("floodlight lamp "))
                {
                    known.Add(light);
                    lamps.Add(Of(light, glass, (light.transform.position - glass.position).sqrMagnitude > 0.01f, 6f, "floodlight"));
                }
                else if (light.name == "Natural Light strobe's light") { known.Add(light); lamps.Add(Of(light, light.transform, false, 0f, "strobe")); }
            }
        }

        static Lamp Of(Light light, Transform glass, bool beamed, float half, string kind)
        {
            half = Mathf.Clamp(half, 2f, 80f);
            return new Lamp { light = light, glass = glass, beamed = beamed, half = half, gather = beamed ? 2f / (1f - Mathf.Cos(half * Mathf.Deg2Rad)) : 1f, kind = kind };
        }

#if DEV
        /// <summary>(For the development build.) Each lamp known: its kind, where its glass is from the craft flown (east, north, up), which way it shines, how much of it reaches the lens and is shown.</summary>
        internal static string Lamps(Camera camera)
        {
            Vessel ship = FlightGlobals.ActiveVessel;
            if (ship == null) return "no craft";
            Vector3 up = (ship.CoMD - ship.mainBody.position).normalized, north = Vector3.ProjectOnPlane(ship.mainBody.transform.up, up).normalized, east = Vector3.Cross(up, north);
            var say = new System.Text.StringBuilder();
            foreach (Lamp l in lamps)
            {
                if (l.light == null || l.glass == null) continue;
                Vector3 at = l.glass.position - (Vector3)ship.CoMD, axis = l.beamed ? Axis(l) : Vector3.zero;
                say.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0} at {1:F1} {2:F1} {3:F1}, shining {4:F2} {5:F2} {6:F2}, strength {7:F2}{8}, light at the lens {9:E1}, shown {10:F2}, {11:F0} m from the camera\n",
                    l.kind, Vector3.Dot(at, east), Vector3.Dot(at, north), Vector3.Dot(at, up), Vector3.Dot(axis, east), Vector3.Dot(axis, north), Vector3.Dot(axis, up), l.light.isActiveAndEnabled ? l.light.intensity : 0f,
                    l.beamed ? " gathered " + l.gather.ToString("F0") + " in " + l.half.ToString("F0") + "°, " + l.off.ToString("F0") + "° off" : "", l.light0, l.shown, camera != null ? (l.glass.position - camera.transform.position).magnitude : -1f);
            }
            return say.Length > 0 ? say.ToString() : "no lamps known";
        }
#endif

        static void Begin(Camera camera)
        {
            on = camera;
            if (quads == null) { quads = new Mesh { name = "Cinema Camera flares" }; quads.MarkDynamic(); quads.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f); }
            if (glow == null)
            {
                Shader add = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive");
#if DEV
                if (Wanted != null) add = Shader.Find(Wanted) ?? add;
#endif
                glow = new Material(add) { name = "Cinema Camera flares", mainTexture = Picture(false) };
                if (glow.HasProperty("_TintColor")) glow.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }
            if (ByObject && holder == null)
            {
                // (one left by an earlier copy of this code, as a development build swapped in while the game runs leaves one)
                GameObject left = GameObject.Find(HolderName);
                if (left != null) Object.Destroy(left);
                holder = new GameObject(HolderName) { layer = 0 };
                Object.DontDestroyOnLoad(holder);
                holder.AddComponent<MeshFilter>().sharedMesh = quads;
                var r = holder.AddComponent<MeshRenderer>();
                r.sharedMaterial = glow;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                r.allowOcclusionWhenDynamic = false;
                holder.SetActive(false);
            }
            Camera.onPreCull -= BeforeDrawing;
            Camera.onPreCull += BeforeDrawing;
            drawnAt = When;
        }

        public static void Off()
        {
            if (holder != null) Object.Destroy(holder);
            holder = null;
            Camera.onPreCull -= BeforeDrawing;
            if (on != null && draw != null) on.RemoveCommandBuffer(drawnAt, draw);      // (an older copy's pass, if any)
            if (draw != null) draw.Release();
            draw = null; on = null;
        }

        /// <summary>The three shapes, side by side: the glare (a soft round fall-off), the rays (eight, thin, from an eight-bladed aperture, fading outward), a ghost (the aperture's octagon, its edge a little brighter than its middle).</summary>
        // (Without mipmaps. Made with them (the picture is 384 x 128, not a power of two on its long side), it came out black on the
        // card wherever the game's texture quality is below full (QualitySettings.masterTextureLimit 1: the owner's), and the flares
        // were drawn all along, with nothing in them: five ways of drawing them were tried before this was found, 2026-10-19.)
        internal static Texture2D Picture(bool mips = false)
        {
            const int S = 128;
            var t = new Texture2D(3 * S, S, TextureFormat.RGBA32, mips) { name = "Cinema Camera flare shapes", wrapMode = TextureWrapMode.Clamp, filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear };
            var px = new Color[3 * S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, w = (y + 0.5f) / S * 2f - 1f, r = Mathf.Sqrt(u * u + w * w), a = Mathf.Atan2(w, u);
                    // (the glare: the light's own core and a long soft fall-off)
                    // (a bright middle, and the long soft fall of light scattered in the glass, as a lamp's veiling glare falls off)
                    float g = Mathf.Clamp01(Mathf.Exp(-r * r * 45f) + 0.45f * Mathf.Exp(-r * 4.5f) + 0.15f * Mathf.Exp(-r * 1.8f)) * Mathf.Clamp01((1f - r) * 3f);
                    // (the rays: eight, from the blades' edges, thin and fading out, each a little uneven)
                    float spoke = Mathf.Pow(Mathf.Abs(Mathf.Cos(4f * a)), 48f);
                    float ray = Mathf.Clamp01(spoke / (1f + 7f * r) * Mathf.Clamp01((1f - r) * 3f) + 0.3f * Mathf.Exp(-r * r * 40f));
                    // (the ghost: the aperture's octagon, its edge brighter, a soft rim)
                    float oct = r * Mathf.Cos(Mathf.PI / 8f) / Mathf.Cos(Mathf.Repeat(a + Mathf.PI / 8f, Mathf.PI / 4f) - Mathf.PI / 8f);
                    float gh = (0.55f + 0.45f * Mathf.SmoothStep(0.55f, 0.9f, oct)) * (1f - Mathf.SmoothStep(0.88f, 0.98f, oct));
                    px[y * 3 * S + x] = new Color(g, g, g, 1f);
                    px[y * 3 * S + S + x] = new Color(ray, ray, ray, 1f);
                    px[y * 3 * S + 2 * S + x] = new Color(gh, gh, gh, 1f);
                }
            t.SetPixels(px);
            t.Apply(mips, true);
            return t;
        }
    }
}
