using System;
using System.Collections.Generic;
using System.IO;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

namespace CineCamera
{
    /// <summary>
    /// The picture as a camera records it and shows it on its monitor (2026-10-19; the owner: "let the cinema camera add luts into
    /// the game so you can use camera luts in game in real time"). Two settings on Cinema Camera's page, used while the camera is on:
    /// "Recorded as": as seen, or one of the cameras' log encodings, worked out from the finished picture's light by each maker's
    /// published curve (the flat picture a log camera sends out); and "LUT": a 3D table of colours from a .cube file, laid over that,
    /// as much as "LUT strength" says: Cinema Camera's own (made from the same published curves, and some looks of its own: see
    /// tools/cine/make_luts.py), or any the player puts in GameData/CineCamera/PluginData/LUTs (a camera maker's monitor LUTs,
    /// a colourist's looks). Done at the very end of the near camera's drawing, after the game's and TUFX's own tone (TUFX's profile
    /// is never changed), before the game's interface (docs/cine-luts-cameras-plan.md).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Luts : MonoBehaviour
    {
        // (the log encodings the shader knows, in its order: tools/shaderpack/grade.glsl's `encode`)
        static readonly string[] curves = { "As seen (Rec.709)", "ARRI LogC3", "ARRI LogC4", "Sony S-Log3", "Panasonic V-Log", "Canon Log 3", "RED Log3G10", "Blackmagic Film Gen 5", "Fujifilm F-Log", "Canon Log 2", "Fujifilm F-Log2", "Nikon N-Log" };

        internal static Choice recorded, lutChoice;
        internal static Slider strength;
        static Toggle cameraOn;
        static string[] lutNames = { "None" };

        static Shader shader;
        static bool tried;
        static string why = "";
        static Mesh sheet;
        static readonly int idScene = Shader.PropertyToID("_GradeScene"), idMode = Shader.PropertyToID("_GradeMode"), idLut = Shader.PropertyToID("_GradeLut"),
            idBalance = Shader.PropertyToID("_GradeBalance"), idTool = Shader.PropertyToID("_GradeTool"), idPeak = Shader.PropertyToID("_GradePeak");

        /// <summary>A log curve's name, by the shader's number for it (0: as seen).</summary>
        internal static string CurveName(int i) => curves[Mathf.Clamp(i, 0, curves.Length - 1)];

        // (Where the pass goes on the near camera: after its picture's effects (TUFX's tone and anti-aliasing, the lens's blur), as the
        // lens's own pass is, and the last of what is there then, after the light meters that read the picture as the game made it.
        // At AfterEverything, where it was first put, nothing it drew reached the screen.)
        const CameraEvent At = CameraEvent.AfterImageEffects;
        int buffersSeen = -1;

        Material material;
        CommandBuffer pass;
        Camera on;
        Texture3D lut, blank;
        string lutLoaded = "";
        int lutSize = 2;

        /// <summary>What it did, for the page and the development build.</summary>
        internal static string Said = "";
#if DEV
        public static string Says() => Said;
#endif

        static string Folder => KSPUtil.ApplicationRootPath + "GameData/CineCamera/PluginData/LUTs/";

        /// <summary>Its settings, once, on Cinema Camera's page (the LUTs found in the folder at the game's start are what the list holds).</summary>
        internal static void Setup()
        {
            Mod mod = Kit.Find("Cinema Camera");
            if (mod == null) return;
            foreach (Setting s in mod.Settings)
            {
                if (s.Key == "on" && s is Toggle t) cameraOn = t;
                if (s.Key == "recorded" && s is Choice c1) recorded = c1;
                if (s.Key == "lut" && s is Choice c2) lutChoice = c2;
                if (s.Key == "lutStrength" && s is Slider sl) strength = sl;
            }
            if (recorded != null) return;
            var names = new List<string> { "None" };
            try
            {
                if (Directory.Exists(Folder))
                {
                    var found = new List<string>();
                    foreach (string f in Directory.GetFiles(Folder, "*.cube")) found.Add(Path.GetFileNameWithoutExtension(f));
                    found.Sort(StringComparer.OrdinalIgnoreCase);
                    names.AddRange(found);
                }
            }
            catch (Exception e) { Kit.Log("Cinema Camera", "the LUTs' folder could not be read: " + e.Message); }
            lutNames = names.ToArray();
            recorded = mod.Choice("recorded", "Recorded as", 0, curves, "How the camera records the picture, as its monitor shows it without a LUT: as seen, or one of the cameras' log encodings (each maker's published curve): the flat, wide-range picture a cinema camera sends out, for a LUT made for it to turn into a finished picture.");
            lutChoice = mod.Choice("lut", "LUT", 0, lutNames, "A look-up table laid over the picture as recorded (a .cube file): Cinema Camera's own (\"<camera's log> to Rec.709\" for each log setting above, and looks for the picture as seen), or any put in GameData/CineCamera/PluginData/LUTs, such as a camera maker's monitor LUTs. Read at the game's start.");
            strength = mod.Slider("lutStrength", "LUT strength", 1f, 0f, 1f, "", 2, "How much of the LUT is laid on: 1 all of it, 0 none (the picture as recorded).");
        }

        void Awake()
        {
            Setup();
            Camera.onPreCull += BeforeCamera;
        }

        void OnDestroy()
        {
            Camera.onPreCull -= BeforeCamera;
            Off();
            if (pass != null) { pass.Release(); pass = null; }
            if (material != null) Destroy(material);
            if (lut != null) Destroy(lut);
            if (blank != null) Destroy(blank);
        }

        static void Load()
        {
            tried = true;
            string folder = KSPUtil.ApplicationRootPath + "GameData/CineCamera/PluginData/";
            string path = folder + (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore ? "grade.bundle" : "grade-windows.bundle");
            try
            {
                if (!File.Exists(path)) { why = "its shader's file is not there (" + Path.GetFileName(path) + ")"; return; }
                AssetBundle b = AssetBundle.LoadFromFile(path);
                if (b == null) { why = "its shader's file could not be read"; return; }
                foreach (Shader s in b.LoadAllAssets<Shader>()) if (s != null && s.isSupported) shader = s;
                b.Unload(false);
                if (shader == null) why = "its shader does not run on this computer";
            }
            catch (Exception e) { why = e.Message; }
            sheet = new Mesh { name = "Cinema Camera: grade sheet" };
            sheet.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(3f, -1f, 0f), new Vector3(-1f, 3f, 0f) };
            sheet.triangles = new[] { 0, 1, 2 };
            sheet.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            Kit.Log("Cinema Camera", "the picture's look: " + (shader != null ? "shader loaded" : "not drawn: " + why));
        }

        void Off()
        {
            if (on != null && pass != null) on.RemoveCommandBuffer(At, pass);
            on = null;
            buffersSeen = -1;
        }

        void BeforeCamera(Camera cam)
        {
            if (!Names.Is(cam, "Camera 00")) return;
            int curve = recorded != null ? recorded.Value : 0;
            int which = lutChoice != null ? lutChoice.Value : 0;
            float much = strength != null ? strength.Value : 1f;
            // (and the camera's white balance and exposure tool, from the camera chosen: Bodies.cs)
            Vector4 balance = Bodies.Gains;
            int tool = Bodies.Tool;
            Vector4 peak = Bodies.Peak;
            bool want = cameraOn != null && cameraOn.Value && !MapView.MapIsEnabled && (curve > 0 || (which > 0 && much > 0.001f) || balance.w > 0.5f || tool > 0 || peak.w > 0.5f);
            if (!want) { if (on != null) Off(); Said = cameraOn != null && cameraOn.Value ? "the picture as seen" : "off (the cinema camera is off)"; return; }
            if (!tried) Load();
            if (shader == null) { Said = "not drawn: " + why; return; }
            if (material == null) material = new Material(shader) { name = "Cinema Camera: grade" };
            if (pass == null) pass = new CommandBuffer { name = "Cinema Camera: the picture's look" };
            if (on != cam)
            {
                Off();
                cam.AddCommandBuffer(At, pass);
                on = cam;
            }
            // (kept the last: put back at the end whenever another pass has come since)
            if (cam.commandBufferCount != buffersSeen)
            {
                CommandBuffer[] there = cam.GetCommandBuffers(At);
                if (there.Length == 0 || there[there.Length - 1] != pass) { cam.RemoveCommandBuffer(At, pass); cam.AddCommandBuffer(At, pass); }
                buffersSeen = cam.commandBufferCount;
            }
            string name = which > 0 && which < lutNames.Length ? lutNames[which] : "";
            if (name != lutLoaded) { ReadLut(name); lutLoaded = name; }
            if (blank == null) { blank = new Texture3D(2, 2, 2, TextureFormat.RGBA32, false) { name = "Cinema Camera: no LUT" }; blank.Apply(false); }
            bool laid = lut != null && which > 0;
            material.SetVector(idMode, new Vector4(curve, laid ? 1f : 0f, much, laid ? lutSize : 2f));
            material.SetTexture(idLut, laid ? lut : blank);
            material.SetVector(idBalance, balance);
            material.SetVector(idTool, new Vector4(tool, Bodies.ZebraLevel, Time.unscaledTime * 0.8f % 1f, 0f));
            material.SetVector(idPeak, peak);
            pass.Clear();
            // (filtered: the tools take samples between the dots; the picture's own, at the dots' middles, comes through unblurred)
            pass.GetTemporaryRT(idScene, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            pass.Blit(BuiltinRenderTextureType.CameraTarget, idScene);
            pass.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            pass.DrawMesh(sheet, Matrix4x4.identity, material, 0, 0);
            pass.ReleaseTemporaryRT(idScene);
            Said = "recorded as " + curves[Mathf.Clamp(curve, 0, curves.Length - 1)] + (laid ? "; LUT " + name + " (" + lutSize + " dots each way), " + Mathf.RoundToInt(100f * much) + "%" : which > 0 ? "; LUT " + name + " could not be read" : "")
                   + (balance.w > 0.5f ? "; white balance gains " + balance.x.ToString("0.000") + ", " + balance.y.ToString("0.000") + ", " + balance.z.ToString("0.000") : "")
                   + (tool > 0 ? "; exposure tool " + (tool == 4 ? "zebras" : "false colour " + tool) : "") + (peak.w > 0.5f ? "; focus peaking" : "");
        }

        /// <summary>A .cube file into a 3D texture: its header (LUT_3D_SIZE; DOMAIN_MIN/MAX or LUT_3D_INPUT_RANGE, taken as 0 to 1 if absent), then its rows, red fastest.</summary>
        void ReadLut(string name)
        {
            if (lut != null) { Destroy(lut); lut = null; }
            if (string.IsNullOrEmpty(name)) return;
            string path = Folder + name + ".cube";
            try
            {
                int n = 0, at = 0;
                Color[] cells = null;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                Vector3 min = Vector3.zero, max = Vector3.one;
                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] w = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (w[0] == "LUT_3D_SIZE") { n = int.Parse(w[1], inv); cells = new Color[n * n * n]; continue; }
                    if (w[0] == "DOMAIN_MIN" && w.Length >= 4) { min = new Vector3(float.Parse(w[1], inv), float.Parse(w[2], inv), float.Parse(w[3], inv)); continue; }
                    if (w[0] == "DOMAIN_MAX" && w.Length >= 4) { max = new Vector3(float.Parse(w[1], inv), float.Parse(w[2], inv), float.Parse(w[3], inv)); continue; }
                    if (w[0] == "LUT_3D_INPUT_RANGE" && w.Length >= 3) { float a = float.Parse(w[1], inv), b = float.Parse(w[2], inv); min = new Vector3(a, a, a); max = new Vector3(b, b, b); continue; }
                    if (char.IsLetter(w[0][0])) continue;                    // (TITLE, LUT_1D_SIZE and others: a 1D table is not read)
                    if (cells == null || at >= cells.Length || w.Length < 3) continue;
                    cells[at++] = new Color(float.Parse(w[0], inv), float.Parse(w[1], inv), float.Parse(w[2], inv), 1f);
                }
                if (cells == null || n < 2 || at < cells.Length) { Kit.Log("Cinema Camera", "the LUT " + name + " is not a 3D table it can read (" + at + " of " + (cells != null ? cells.Length : 0) + " rows)"); return; }
                if (min != Vector3.zero || max != Vector3.one) Kit.Log("Cinema Camera", "the LUT " + name + " has its own input range; read as 0 to 1");
                lut = new Texture3D(n, n, n, TextureFormat.RGBAHalf, false) { name = "Cinema Camera: LUT " + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                lut.SetPixels(cells);
                lut.Apply(false);
                lutSize = n;
            }
            catch (Exception e) { Kit.Log("Cinema Camera", "the LUT " + name + " could not be read: " + e.Message); lut = null; }
        }
    }
}
