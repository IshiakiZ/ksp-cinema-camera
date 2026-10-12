using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CineCamera
{
    /// <summary>
    /// The camera's light meter. A camera's meter says how bright the picture will come out against the middle grey that
    /// every meter is made to aim at: so many stops over (+) or under (-) it. Any scene averages out near that grey (that is
    /// what the grey was chosen for), so a picture whose meter reads nought is a picture neither too dark nor too light.
    ///
    /// This one reads the finished picture, after the film and the lens (so after the exposure, whoever set it): the
    /// graphics card shrinks it, in halves, to a few thousand dots a few times a second, and they are read back two frames
    /// later, when the card has long finished with them (read back at once, the game would wait for the card). Brightness
    /// here is light, not what is shown: the game's picture is as it is shown, and as light that is the shown value to the
    /// power 2.2, so middle grey (18% of white, as light) is shown at 0.46.
    ///
    /// Centre-weighted, the whole picture counts and its middle most; spot, only what is inside the focus bracket.
    /// </summary>
    sealed class Meter
    {
        const float Grey = 0.18f;

        Camera on;
        CommandBuffer grab;
        RenderTexture small;
        Texture2D read;
        int wide, high, smallWide, smallHigh;
        int askedAt = -1;                         // the frame the picture was last asked to be shrunk in (-1: not waiting for one)
        float nextAsk, askedExposure, askedShare = 1f, weighedShare = -1f;
        bool weighedSpot;
        float[] weights;
        static float[] light;                     // the shown value (1024ths) as light

        /// <summary>The last reading: stops over (+) or under (-) middle grey. Not a number until there is one.</summary>
        public float Reading = float.NaN;
        /// <summary>The camera's own exposure (in stops) when the picture that reading came from was drawn.</summary>
        public float ReadingExposure;
        /// <summary>Only what is inside the focus bracket, instead of the whole picture with its middle counting most.</summary>
        public bool Spot;

        /// <summary>
        /// While the meter is wanted, every frame (from LateUpdate): the camera whose picture is the finished one, the camera's
        /// own exposure just now (in stops), and how much of the picture's height is not under cinema bars (0 to 1).
        /// </summary>
        public void Use(Camera cam, float exposure, float share)
        {
            if (cam == null) { Off(); return; }
            if (cam != on || cam.pixelWidth != wide || cam.pixelHeight != high) Hook(cam);
            if (askedAt >= 0)
            {
                // (the picture is shrunk in the frame it was asked for in, and not again)
                if (Time.frameCount > askedAt) grab.Clear();
                // (one that is never read, because nothing called Read, is given up after a while)
                if (Time.frameCount - askedAt > 30) askedAt = -1;
                return;
            }
            if (Time.unscaledTime < nextAsk) return;
            Fill();
            askedAt = Time.frameCount;
            askedExposure = exposure;
            askedShare = Mathf.Clamp(share, 0.05f, 1f);
            nextAsk = Time.unscaledTime + 0.2f;
        }

        /// <summary>From Update: the picture asked for two frames ago, read.</summary>
        public void Read()
        {
            if (askedAt < 0 || Time.frameCount < askedAt + 2 || small == null || read == null) return;
            askedAt = -1;
            try
            {
                RenderTexture was = RenderTexture.active;
                RenderTexture.active = small;
                read.ReadPixels(new Rect(0, 0, smallWide, smallHigh), 0, 0, false);
                RenderTexture.active = was;
                Weigh(read.GetPixels());
            }
            catch (Exception) { }
        }

        void Hook(Camera cam)
        {
            Off();
            on = cam; wide = cam.pixelWidth; high = cam.pixelHeight;
            int w = Mathf.Max(1, wide / 2), h = Mathf.Max(1, high / 2);
            while (w > 96 || h > 54) { w = Mathf.Max(1, w / 2); h = Mathf.Max(1, h / 2); }
            smallWide = w; smallHigh = h;
            small = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf) { filterMode = FilterMode.Bilinear, name = "Cinema Camera: the meter's picture" };
            small.Create();
            read = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true);
            grab = new CommandBuffer { name = "Cinema Camera: the meter" };
            cam.AddCommandBuffer(CameraEvent.AfterImageEffects, grab);
            askedAt = -1;
            Reading = float.NaN;
        }

        /// <summary>The finished picture shrunk, in halves (each dot of a half the mean of four of the whole), to the small one.</summary>
        void Fill()
        {
            grab.Clear();
            // (Copied whole first. Shrunk straight from the camera's own target, the first half came out as the lower left quarter of the
            // picture whenever something drawn before it in the frame had drawn at another size (on the launch pad, not in space): the
            // copy at the camera's own size is the one that always comes out right, and the rest are shrunk from that.)
            int whole = Shader.PropertyToID("_CineMeterWhole");
            grab.GetTemporaryRT(whole, wide, high, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
            grab.Blit(BuiltinRenderTextureType.CameraTarget, whole);
            int w = Mathf.Max(1, wide / 2), h = Mathf.Max(1, high / 2), n = 0, last = whole;
            RenderTargetIdentifier from = whole;
            while (w > 96 || h > 54)
            {
                int id = Shader.PropertyToID("_CineMeter" + n++);
                grab.GetTemporaryRT(id, w, h, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
                grab.Blit(from, id);
                if (last != -1) grab.ReleaseTemporaryRT(last);
                from = id; last = id;
                w = Mathf.Max(1, w / 2); h = Mathf.Max(1, h / 2);
            }
            grab.Blit(from, small);
            if (last != -1) grab.ReleaseTemporaryRT(last);
        }

        /// <summary>How much each dot counts: centre-weighted, a broad hump over the middle on a little for everything; spot, the focus bracket alone. Nothing under the bars.</summary>
        void Weights()
        {
            int w = smallWide, h = smallHigh;
            if (weights == null || weights.Length != w * h) weights = new float[w * h];
            float aspect = w / (float)h;
            bool any = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // (in half the picture's height from its middle, both ways)
                    float dx = ((x + 0.5f) / w * 2f - 1f) * aspect, dy = (y + 0.5f) / h * 2f - 1f;
                    float weight;
                    if (Mathf.Abs(dy) > askedShare) weight = 0f;
                    else if (Spot) weight = Mathf.Abs(dx) <= 0.1f && Mathf.Abs(dy) <= 0.1f ? 1f : 0f;
                    else weight = 0.15f + Mathf.Exp(-(dx * dx + dy * dy) / 0.3f);
                    weights[x + y * w] = weight;
                    any |= weight > 0f;
                }
            // (a picture too small to have a dot inside the bracket: the middle one)
            if (!any) weights[w / 2 + (h / 2) * w] = 1f;
            weighedSpot = Spot; weighedShare = askedShare;
        }

        void Weigh(Color[] dots)
        {
            if (light == null)
            {
                light = new float[1025];
                for (int i = 0; i <= 1024; i++) light[i] = Mathf.Pow(i / 1024f, 2.2f);
            }
            if (weights == null || weights.Length != dots.Length || weighedSpot != Spot || weighedShare != askedShare) Weights();
            double sum = 0, all = 0;
            for (int i = 0; i < dots.Length; i++)
            {
                float weight = weights[i];
                if (weight <= 0f) continue;
                Color c = dots[i];
                float v = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                // (Some of the game's own shaders give the odd pixel that is not a number at all; such a dot is left out. One brighter
                // than white is shown white, and that is what it counts as.)
                if (!(v >= 0f)) { if (!(v < 0f)) continue; v = 0f; }
                if (v > 1f) v = 1f;
                sum += weight * light[(int)(v * 1024f)];
                all += weight;
            }
            Reading = all > 0 ? Mathf.Log(Mathf.Max((float)(sum / all), 1e-5f) / Grey, 2f) : float.NaN;
            ReadingExposure = askedExposure;
        }

        public void Off()
        {
            if (on == null && grab == null && small == null) return;
            if (on != null && grab != null) on.RemoveCommandBuffer(CameraEvent.AfterImageEffects, grab);
            if (grab != null) grab.Release();
            if (small != null) { small.Release(); UnityEngine.Object.Destroy(small); }
            if (read != null) UnityEngine.Object.Destroy(read);
            on = null; grab = null; small = null; read = null;
            askedAt = -1;
            Reading = float.NaN;
        }

#if DEV
        /// <summary>(For the development build: the small picture last read, written out as it was read, brighter than white cut off.)</summary>
        public string Save(string path)
        {
            if (read == null) return "nothing read yet";
            Color[] dots = read.GetPixels();
            var copy = new Texture2D(smallWide, smallHigh, TextureFormat.RGBA32, false);
            for (int i = 0; i < dots.Length; i++) { Color c = dots[i]; dots[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f); }
            copy.SetPixels(dots);
            copy.Apply();
            System.IO.File.WriteAllBytes(path, copy.EncodeToPNG());
            UnityEngine.Object.Destroy(copy);
            return "written " + path + " from " + (on != null ? on.name : "?") + ", hdr " + (on != null && on.allowHDR);
        }

        public string Said => (on != null ? "metering " + on.name + " (" + smallWide + " x " + smallHigh + " dots)" : "not metering") + ", reading " + (float.IsNaN(Reading) ? "none" : Reading.ToString("F2")) + (Spot ? " (spot)" : "");
#endif
    }
}
