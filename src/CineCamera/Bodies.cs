using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Keystone;
using UnityEngine;

namespace CineCamera
{
    /// <summary>
    /// Real cinema cameras: their screens and their settings (2026-10-19; the owner: "research a whole ton of cameras and add their
    /// ui and settings so people familiar with certain cameras can use that"). "Camera" on Cinema Camera's page picks one. With it,
    /// the viewfinder is that camera's own, laid out, worded and coloured as its maker's (looked up in the makers' manuals and
    /// simulators: docs/cine-luts-cameras-plan.md); the lens sees as it does on that camera's sensor, by the width of the format it
    /// records; and it is set as the camera is, by clicking a value on its screen and picking from the steps the camera itself
    /// offers: the frame rate, the shutter (an angle at the frame rate, or a speed where the camera gives one), the exposure index,
    /// its own grey filters, the iris, the white balance and tint, the format, its monitoring LUT on or off, and its exposure tool
    /// (its maker's false colour, or zebras). Picking a camera sets what it records (its maker's log curve), its monitoring LUT and
    /// its own exposure index. Underneath they are Cinema Camera's own settings (the shutter, the film speed, the grey filter, the
    /// aperture), shown in the camera's words; the white balance is laid on by the picture's look (Luts.cs).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Bodies : MonoBehaviour
    {
        // ---- the cameras

        enum Maker { Arri, Venice, SonyFx, Red, Blackmagic, Canon, Panasonic, Fujifilm, Nikon }

        sealed class Format
        {
            public readonly string Name;
            public readonly float Wide;                           // the width of the sensor it uses, mm
            public Format(string name, float wide) { Name = name; Wide = wide; }
        }

        sealed class Body
        {
            public string Name, Codec;
            public Maker Maker;
            public int Curve;                                     // its maker's log, by the look's list (Luts.cs)
            public float Base;                                    // its own exposure index (its native, or first base, ISO)
            public float[] Speeds;                                // the EI or ISO it can be set to (up to 6400: the film speed's top)
            public float[] Nd;                                    // its own grey filters, in stops, clear first; null where it has none inside
            public float Fastest;                                 // its highest frame rate, at its biggest picture
            public float DataRate;                                // what it writes, MB a second at 24 frames a second (for the media's time left)
            public Format[] Formats;
        }

        static readonly float[] Thirds = { 100, 125, 160, 200, 250, 320, 400, 500, 640, 800, 1000, 1250, 1600, 2000, 2500, 3200, 4000, 5000, 6400 };
        static readonly float[] ArriEi = { 160, 200, 250, 320, 400, 500, 640, 800, 1000, 1280, 1600, 2000, 2560, 3200 };
        static readonly float[] ArriEi35 = { 160, 200, 250, 320, 400, 500, 640, 800, 1000, 1280, 1600, 2000, 2560, 3200, 4000, 5120, 6400 };
        static readonly float[] RedIso = { 250, 320, 400, 500, 640, 800, 1000, 1280, 1600, 2000, 2560, 3200, 4000, 5120, 6400 };
        static float[] From(float least) => Array.FindAll(Thirds, v => v >= least);
        static readonly float[] ArriNd = { 0, 2, 4, 6 }, ArriNdMini = { 0, 2, 4, 7 }, VeniceNd = { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, VariableNd = { 0, 2, 3, 4, 5, 6, 7 },
            TwoFourSix = { 0, 2, 4, 6 }, CanonNd = { 0, 2, 4, 6, 8, 10 };
        static Format[] F(params object[] pairs)
        {
            var list = new Format[pairs.Length / 2];
            for (int i = 0; i < list.Length; i++) list[i] = new Format((string)pairs[2 * i], Convert.ToSingle(pairs[2 * i + 1], CultureInfo.InvariantCulture));
            return list;
        }

        // (Their makers' published figures: the sensor's width in each format, the exposure indices, the filters inside them. The
        // names are what the setting is kept by: never renamed. Curves: 1 LogC3, 2 LogC4, 3 S-Log3, 4 V-Log, 5 Canon Log 3, 6 Log3G10,
        // 7 Film Gen 5, 9 Canon Log 2, 10 F-Log2, 11 N-Log. F-Log2's least ISO is 1250 on the X-H2S, and taken so on Fujifilm's others;
        // N-Log's base is 800, with 200 to 640 below it at less latitude.)
        static readonly Body[] All =
        {
            new Body { Name = "ARRI ALEXA 35", Maker = Maker.Arri, Curve = 2, Codec = "ARRIRAW", Base = 800, Speeds = ArriEi35, Nd = ArriNd, Fastest = 120, DataRate = 290,
                       Formats = F("4.6K 3:2 Open Gate", 27.99, "4.6K 16:9", 27.99, "4K 16:9", 24.88) },
            new Body { Name = "ARRI ALEXA Mini LF", Maker = Maker.Arri, Curve = 1, Codec = "ARRIRAW", Base = 800, Speeds = ArriEi, Nd = ArriNd, Fastest = 60, DataRate = 330,
                       Formats = F("4.5K LF 3:2 Open Gate", 36.70, "3.8K LF 16:9 UHD", 31.68) },
            new Body { Name = "ARRI ALEXA Mini", Maker = Maker.Arri, Curve = 1, Codec = "ARRIRAW", Base = 800, Speeds = ArriEi, Nd = ArriNdMini, Fastest = 200, DataRate = 170,
                       Formats = F("3.4K 3:2 Open Gate", 28.25, "3.2K 16:9", 26.40, "HD 16:9", 23.76) },
            new Body { Name = "ARRI AMIRA", Maker = Maker.Arri, Curve = 1, Codec = "ProRes 4444", Base = 800, Speeds = ArriEi, Nd = ArriNdMini, Fastest = 200, DataRate = 110,
                       Formats = F("3.2K 16:9", 26.40, "HD 16:9", 23.76) },
            new Body { Name = "ARRI ALEXA 65", Maker = Maker.Arri, Curve = 1, Codec = "ARRIRAW", Base = 800, Speeds = ArriEi, Nd = null, Fastest = 60, DataRate = 790,
                       Formats = F("6.5K Open Gate", 54.12) },

            new Body { Name = "Sony VENICE 2 8K", Maker = Maker.Venice, Curve = 3, Codec = "X-OCN ST", Base = 800, Speeds = From(200), Nd = VeniceNd, Fastest = 60, DataRate = 240,
                       Formats = F("8.6K 3:2", 35.9, "8.2K 17:9", 34.0, "5.8K 17:9", 24.1) },
            new Body { Name = "Sony VENICE 2 6K", Maker = Maker.Venice, Curve = 3, Codec = "X-OCN ST", Base = 500, Speeds = From(125), Nd = VeniceNd, Fastest = 60, DataRate = 150,
                       Formats = F("6K 3:2", 36.2, "4K 17:9", 24.3) },

            new Body { Name = "Sony BURANO", Maker = Maker.SonyFx, Curve = 3, Codec = "X-OCN LT", Base = 800, Speeds = From(200), Nd = VariableNd, Fastest = 60, DataRate = 120,
                       Formats = F("8.6K 3:2", 35.9, "8.2K 17:9", 34.0, "5.8K 17:9", 24.1) },
            new Body { Name = "Sony FX9", Maker = Maker.SonyFx, Curve = 3, Codec = "XAVC-I", Base = 800, Speeds = From(200), Nd = VariableNd, Fastest = 60, DataRate = 75,
                       Formats = F("FF 6K", 35.7, "S35 4K", 24.6) },
            new Body { Name = "Sony FX6", Maker = Maker.SonyFx, Curve = 3, Codec = "XAVC-I", Base = 800, Speeds = From(200), Nd = VariableNd, Fastest = 120, DataRate = 75,
                       Formats = F("FF 4K", 35.6, "S35", 23.7) },
            new Body { Name = "Sony FX3", Maker = Maker.SonyFx, Curve = 3, Codec = "XAVC S-I", Base = 800, Speeds = From(100), Nd = null, Fastest = 120, DataRate = 75,
                       Formats = F("FF 4K", 35.6, "APS-C", 23.5) },
            new Body { Name = "Sony FX30", Maker = Maker.SonyFx, Curve = 3, Codec = "XAVC S-I", Base = 800, Speeds = From(100), Nd = null, Fastest = 120, DataRate = 75,
                       Formats = F("APS-C 4K", 23.3) },

            new Body { Name = "RED V-RAPTOR [X] 8K VV", Maker = Maker.Red, Curve = 6, Codec = "R3D HQ", Base = 800, Speeds = RedIso, Nd = null, Fastest = 120, DataRate = 280,
                       Formats = F("8K VV 17:9", 40.96, "6K 17:9", 30.72, "4K 17:9", 20.48) },
            new Body { Name = "RED V-RAPTOR XL [X] 8K VV", Maker = Maker.Red, Curve = 6, Codec = "R3D HQ", Base = 800, Speeds = RedIso, Nd = VariableNd, Fastest = 120, DataRate = 280,
                       Formats = F("8K VV 17:9", 40.96, "6K 17:9", 30.72, "4K 17:9", 20.48) },
            new Body { Name = "RED V-RAPTOR 8K S35", Maker = Maker.Red, Curve = 6, Codec = "R3D HQ", Base = 800, Speeds = RedIso, Nd = null, Fastest = 120, DataRate = 280,
                       Formats = F("8K 17:9", 26.21, "6K 17:9", 19.66, "4K 17:9", 13.11) },
            new Body { Name = "RED KOMODO-X 6K S35", Maker = Maker.Red, Curve = 6, Codec = "R3D HQ", Base = 800, Speeds = RedIso, Nd = null, Fastest = 80, DataRate = 160,
                       Formats = F("6K 17:9", 27.03, "4K 17:9", 18.02) },
            new Body { Name = "RED KOMODO 6K S35", Maker = Maker.Red, Curve = 6, Codec = "R3D HQ", Base = 800, Speeds = RedIso, Nd = null, Fastest = 40, DataRate = 160,
                       Formats = F("6K 17:9", 27.03, "4K 17:9", 18.02) },

            new Body { Name = "Blackmagic URSA Cine 12K LF", Maker = Maker.Blackmagic, Curve = 7, Codec = "BRAW 5:1", Base = 800, Speeds = From(200), Nd = TwoFourSix, Fastest = 60, DataRate = 450,
                       Formats = F("12K 3:2 Open Gate", 35.64, "9K Super 35", 26.73) },
            new Body { Name = "Blackmagic URSA Mini Pro 12K", Maker = Maker.Blackmagic, Curve = 7, Codec = "BRAW 8:1", Base = 800, Speeds = From(200), Nd = TwoFourSix, Fastest = 60, DataRate = 240,
                       Formats = F("12K 17:9", 27.03, "6K Super 16", 13.51) },
            new Body { Name = "Blackmagic PYXIS 6K", Maker = Maker.Blackmagic, Curve = 7, Codec = "BRAW 5:1", Base = 400, Speeds = From(100), Nd = null, Fastest = 36, DataRate = 160,
                       Formats = F("6K 3:2 Open Gate", 36.0, "4K DCI", 24.2) },
            new Body { Name = "Blackmagic Cinema Camera 6K", Maker = Maker.Blackmagic, Curve = 7, Codec = "BRAW 5:1", Base = 400, Speeds = From(100), Nd = null, Fastest = 36, DataRate = 160,
                       Formats = F("6K 3:2 Open Gate", 36.0, "4K DCI", 24.2) },
            new Body { Name = "Blackmagic Pocket Cinema Camera 6K Pro", Maker = Maker.Blackmagic, Curve = 7, Codec = "BRAW 5:1", Base = 400, Speeds = From(100), Nd = TwoFourSix, Fastest = 60, DataRate = 120,
                       Formats = F("6K 16:9", 23.10) },
            new Body { Name = "Blackmagic Pocket Cinema Camera 4K", Maker = Maker.Blackmagic, Curve = 7, Codec = "BRAW 5:1", Base = 400, Speeds = From(100), Nd = null, Fastest = 60, DataRate = 60,
                       Formats = F("4K DCI", 18.96) },

            new Body { Name = "Canon EOS C400", Maker = Maker.Canon, Curve = 9, Codec = "Cinema RAW Light", Base = 800, Speeds = From(200), Nd = CanonNd, Fastest = 60, DataRate = 260,
                       Formats = F("6000x3164", 36.0, "4096x2160 S35", 24.6) },
            new Body { Name = "Canon EOS C80", Maker = Maker.Canon, Curve = 9, Codec = "XF-AVC", Base = 800, Speeds = From(200), Nd = CanonNd, Fastest = 60, DataRate = 100,
                       Formats = F("6000x3164", 36.0, "4096x2160 S35", 24.6) },
            new Body { Name = "Canon EOS C500 Mark II", Maker = Maker.Canon, Curve = 9, Codec = "Cinema RAW Light", Base = 800, Speeds = From(200), Nd = CanonNd, Fastest = 60, DataRate = 260,
                       Formats = F("5952x3140", 38.1, "4096x2160 S35", 26.2) },
            new Body { Name = "Canon EOS C300 Mark III", Maker = Maker.Canon, Curve = 9, Codec = "XF-AVC", Base = 800, Speeds = From(200), Nd = CanonNd, Fastest = 120, DataRate = 100,
                       Formats = F("4096x2160", 26.2, "3840x2160", 24.6) },
            new Body { Name = "Canon EOS C70", Maker = Maker.Canon, Curve = 5, Codec = "XF-AVC", Base = 800, Speeds = From(200), Nd = CanonNd, Fastest = 120, DataRate = 100,
                       Formats = F("4096x2160", 26.2, "3840x2160", 24.6) },
            new Body { Name = "Canon EOS R5 C", Maker = Maker.Canon, Curve = 5, Codec = "Cinema RAW Light", Base = 800, Speeds = From(100), Nd = null, Fastest = 60, DataRate = 330,
                       Formats = F("8192x4320", 36.0, "5952x3140 S35", 22.5) },

            new Body { Name = "Panasonic VariCam 35", Maker = Maker.Panasonic, Curve = 4, Codec = "AVC-I 4:4:4", Base = 800, Speeds = From(200), Nd = TwoFourSix, Fastest = 120, DataRate = 130,
                       Formats = F("4096 x 2160", 24.58, "3840 x 2160", 23.04) },
            new Body { Name = "Panasonic VariCam LT", Maker = Maker.Panasonic, Curve = 4, Codec = "AVC-I 4:2:2", Base = 800, Speeds = From(200), Nd = TwoFourSix, Fastest = 60, DataRate = 65,
                       Formats = F("4096 x 2160", 24.58, "3840 x 2160", 23.04) },
            new Body { Name = "Panasonic AU-EVA1", Maker = Maker.Panasonic, Curve = 4, Codec = "MOV-422L", Base = 800, Speeds = From(200), Nd = TwoFourSix, Fastest = 60, DataRate = 50,
                       Formats = F("4096 x 2160", 24.60, "3840 x 2160", 23.06) },

            new Body { Name = "Fujifilm GFX100 II", Maker = Maker.Fujifilm, Curve = 10, Codec = "ProRes 422 HQ", Base = 1250, Speeds = From(1250), Nd = null, Fastest = 60, DataRate = 90,
                       Formats = F("GF 4K", 43.8, "35mm 8K", 35.9) },
            new Body { Name = "Fujifilm X-H2S", Maker = Maker.Fujifilm, Curve = 10, Codec = "ProRes 422 HQ", Base = 1250, Speeds = From(1250), Nd = null, Fastest = 120, DataRate = 70,
                       Formats = F("6.2K", 23.5, "4K HQ", 23.5, "4K 120P", 18.2) },
            new Body { Name = "Fujifilm X-H2", Maker = Maker.Fujifilm, Curve = 10, Codec = "ProRes 422 HQ", Base = 1250, Speeds = From(1250), Nd = null, Fastest = 60, DataRate = 70,
                       Formats = F("8K", 23.5, "4K HQ", 23.5) },
            new Body { Name = "Fujifilm X-T5", Maker = Maker.Fujifilm, Curve = 10, Codec = "ALL-Intra", Base = 1250, Speeds = From(1250), Nd = null, Fastest = 60, DataRate = 45,
                       Formats = F("6.2K", 23.5, "4K HQ", 23.5) },

            new Body { Name = "Nikon Z9", Maker = Maker.Nikon, Curve = 11, Codec = "N-RAW", Base = 800, Speeds = From(200), Nd = null, Fastest = 60, DataRate = 290,
                       Formats = F("FX 8K", 35.9, "FX 4K", 35.9, "DX 4K", 23.5) },
            new Body { Name = "Nikon Z8", Maker = Maker.Nikon, Curve = 11, Codec = "N-RAW", Base = 800, Speeds = From(200), Nd = null, Fastest = 60, DataRate = 290,
                       Formats = F("FX 8K", 35.9, "FX 4K", 35.9, "DX 4K", 23.5) },
            new Body { Name = "Nikon Z6III", Maker = Maker.Nikon, Curve = 11, Codec = "N-RAW", Base = 800, Speeds = From(200), Nd = null, Fastest = 60, DataRate = 190,
                       Formats = F("FX 6K", 35.9, "DX 4K", 23.5) },
        };

        static readonly float[] Rates = { 23.976f, 24f, 25f, 29.97f, 30f, 48f, 50f, 59.94f, 60f, 96f, 100f, 120f };
        static readonly string[] RateNames = { "23.976", "24", "25", "29.97", "30", "48", "50", "59.94", "60", "96", "100", "120" };
        static readonly float[] ArriAngles = { 11.2f, 22.5f, 45f, 90f, 144f, 172.8f, 180f, 270f, 358f };
        static readonly float[] SonyAngles = { 11.25f, 22.5f, 45f, 90f, 144f, 172.8f, 180f, 216f, 270f, 360f };
        static readonly float[] BmdAngles = { 11.2f, 22.5f, 45f, 60f, 72f, 90f, 120f, 144f, 172.8f, 180f, 216f, 270f, 360f };
        static readonly float[] CanonAngles = { 11.25f, 15f, 22.5f, 30f, 45f, 60f, 90f, 120f, 172.8f, 180f, 240f, 360f };
        static readonly float[] FxSpeeds = { 24, 25, 30, 48, 50, 60, 96, 100, 120, 125, 250, 500, 1000, 2000 };
        static readonly float[] Stops = { 1.4f, 1.6f, 1.8f, 2f, 2.2f, 2.5f, 2.8f, 3.2f, 3.5f, 4f, 4.5f, 5f, 5.6f, 6.3f, 7.1f, 8f, 9f, 10f, 11f, 13f, 14f, 16f, 18f, 20f, 22f };
        static readonly float[] Kelvins = { 2800, 3200, 3600, 4300, 5000, 5600, 6500, 7500, 9000 };
        static readonly float[] Tints = { -4, -3, -2, -1, 0, 1, 2, 3, 4 };

        // ---- its settings (on Cinema Camera's page, after the look's), and Cinema Camera's own that the cameras set

        static Choice body, rate, tool, finder, focusMode, meterMode;
        static Slider balance, tint, zebra, shutter, speed, filter, stop, focal, hand;
        static Toggle cameraOn, peaking;
        static Mod mod;
        static Setting hooked;

        internal static void Setup()
        {
            Luts.Setup();
            mod = Kit.Find("Cinema Camera");
            if (mod == null) return;
            foreach (Setting s in mod.Settings)
            {
                switch (s.Key)
                {
                    case "on": cameraOn = s as Toggle; break;
                    case "shutter": shutter = s as Slider; break;
                    case "iso": speed = s as Slider; break;
                    case "filter": filter = s as Slider; break;
                    case "stop": stop = s as Slider; break;
                    case "focal": focal = s as Slider; break;
                    case "hand": hand = s as Slider; break;
                    case "finder": finder = s as Choice; break;
                    case "focus": focusMode = s as Choice; break;
                    case "meter": meterMode = s as Choice; break;
                    case "body": body = s as Choice; break;
                    case "fps": rate = s as Choice; break;
                    case "wb": balance = s as Slider; break;
                    case "tint": tint = s as Slider; break;
                    case "tool": tool = s as Choice; break;
                    case "zebra": zebra = s as Slider; break;
                    case "peaking": peaking = s as Toggle; break;
                }
            }
            if (body == null)
            {
                var names = new string[All.Length + 1];
                names[0] = "Cinema Camera's own";
                for (int i = 0; i < All.Length; i++) names[i + 1] = All[i].Name;
                body = mod.Choice("body", "Camera", 0, names, "A real cinema camera: its viewfinder becomes that camera's screen, laid out and worded as its maker's, and its lens sees as on that camera's sensor. Click a value on its screen (the frame rate, shutter, EI or ISO, ND, iris, white balance, format, LUT, false colour or zebras) to set it from the steps the camera offers. Picking one sets its log, its monitoring LUT and its own exposure index. Cinema Camera's own: the viewfinder as it was.");
                rate = mod.Choice("fps", "Frame rate", 1, RateNames, "The camera's frame rate: what its shutter angle is worked out at (180° at 24 frames a second is 1/48 s), and what its timecode counts in.");
                balance = mod.Slider("wb", "White balance", 5600f, 2000f, 11000f, " K", 0, "What colour of light the camera takes as white. The game's own light is daylight, 5600 K: set lower (tungsten, 3200 K) it comes out blue, higher, warm. Laid on by the picture's look, while the camera is on.");
                tint = mod.Slider("tint", "Tint", 0f, -10f, 10f, "", 1, "Green (-) or magenta (+), as a camera's tint or CC.");
                tool = mod.Choice("tool", "Exposure tool", 0, new[] { "none", "false colour", "zebras" }, "False colour: the picture in grey, with the camera maker's colours where it is too dark, at middle grey, at a stop over (skin), near clipping and clipped (ARRI's zones, RED's purple and red, Blackmagic's; cameras without one show zebras). Zebras: stripes where the picture is at or over the level below.");
                zebra = mod.Slider("zebra", "Zebras from", 95f, 50f, 109f, " %", 0, "How bright the picture must be for the zebras' stripes.");
            }
            if (peaking == null) peaking = mod.Toggle("peaking", "Focus peaking", false, "Edges that are in focus lit in a colour (the camera maker's: ARRI's and RED's red, Sony's, Canon's and Panasonic's white, Blackmagic's green), to see where the focus is.");
            // (told of changes once for each setting there is: Keystone makes them anew if Cinema Camera has to register again)
            if (hooked != body)
            {
                hooked = body;
                body.Changed += Picked;
                balance.Changed += Rebalance;
                tint.Changed += Rebalance;
            }
            Rebalance();
        }

        static Body Chosen => body != null && body.Value > 0 && body.Value <= All.Length ? All[body.Value - 1] : null;

        /// <summary>A camera picked: its log, its monitoring LUT, its own exposure index, the nearest of its filters; its exposure tool if it has no false colour.</summary>
        static void Picked()
        {
            Body b = Chosen;
            if (b == null) return;
            if (Luts.recorded != null) Luts.recorded.Value = b.Curve;
            if (Luts.lutChoice != null)
            {
                int at = Array.IndexOf(Luts.lutChoice.Options, Luts.CurveName(b.Curve) + " to Rec.709");
                if (at > 0) Luts.lutChoice.Value = at;
            }
            if (Luts.strength != null) Luts.strength.Value = 1f;
            if (speed != null) speed.Value = b.Base;
            if (filter != null && b.Nd != null) filter.Value = Nearest(b.Nd, filter.Value);
            if (tool != null && tool.Value == 1 && FalseColour(b) == 4) tool.Value = 2;
        }

        static float Nearest(float[] steps, float v)
        {
            float best = steps[0];
            foreach (float s in steps) if (Mathf.Abs(s - v) < Mathf.Abs(best - v)) best = s;
            return best;
        }

        // ---- what the picture's look takes from it (Luts.cs)

        static Vector4 gains = new Vector4(1f, 1f, 1f, 0f);

        /// <summary>The white balance, as gains on the light: 1, 1, 1 (and w 0: not laid on) at 5600 K with no tint.</summary>
        internal static Vector4 Gains => gains;

        /// <summary>The exposure tool for the look's shader: 0 none, 1 ARRI's false colour, 2 RED's exposure check, 3 Blackmagic's, 4 zebras.</summary>
        internal static int Tool => tool == null || tool.Value == 0 ? 0 : tool.Value == 2 ? 4 : FalseColour(Chosen);

        /// <summary>The zebras' level, as the picture's shown brightness (0 to 1).</summary>
        internal static float ZebraLevel => zebra != null ? zebra.Value / 100f : 0.95f;

        /// <summary>Focus peaking for the look's shader: its colour, the chosen camera's maker's, and w 1 while it is on.</summary>
        internal static Vector4 Peak
        {
            get
            {
                if (peaking == null || !peaking.Value) return Vector4.zero;
                Maker m = Chosen != null ? Chosen.Maker : Maker.Arri;
                Color c = m == Maker.Arri || m == Maker.Red || m == Maker.Fujifilm || m == Maker.Nikon ? new Color(1f, 0.12f, 0.1f) : m == Maker.Blackmagic ? new Color(0.2f, 1f, 0.25f) : Color.white;
                return new Vector4(c.r, c.g, c.b, 1f);
            }
        }

        static int FalseColour(Body b) => b == null || b.Maker == Maker.Arri ? 1 : b.Maker == Maker.Red ? 2 : b.Maker == Maker.Blackmagic ? 3 : 4;

        static void Rebalance()
        {
            float k = balance != null ? balance.Value : 5600f, t = tint != null ? tint.Value : 0f;
            if (Mathf.Abs(k - 5600f) < 1f && Mathf.Abs(t) < 0.05f) { gains = new Vector4(1f, 1f, 1f, 0f); return; }
            Vector3 day = Planck(5600f), set = Planck(k);
            var g = new Vector3(day.x / set.x, day.y / set.y, day.z / set.z);
            g.y *= Mathf.Pow(2f, -0.045f * t);
            float lum = 0.2126f * g.x + 0.7152f * g.y + 0.0722f * g.z;
            gains = new Vector4(g.x / lum, g.y / lum, g.z / lum, 1f);
        }

        /// <summary>The colour of a black body's light at so many kelvin, as linear sRGB (Kim and others' fit to the Planckian locus in CIE 1931 xy).</summary>
        static Vector3 Planck(float kelvin)
        {
            double t = Mathf.Clamp(kelvin, 1667f, 25000f), x, y;
            if (t <= 4000.0) x = -0.2661239e9 / (t * t * t) - 0.2343589e6 / (t * t) + 0.8776956e3 / t + 0.179910;
            else x = -3.0258469e9 / (t * t * t) + 2.1070379e6 / (t * t) + 0.2226347e3 / t + 0.240390;
            if (t <= 2222.0) y = -1.1063814 * x * x * x - 1.34811020 * x * x + 2.18555832 * x - 0.20219683;
            else if (t <= 4000.0) y = -0.9549476 * x * x * x - 1.37418593 * x * x + 2.09137015 * x - 0.16748867;
            else y = 3.0817580 * x * x * x - 5.87338670 * x * x + 3.75112997 * x - 0.37001483;
            double X = x / y, Z = (1.0 - x - y) / y;
            double r = 3.2406 * X - 1.5372 - 0.4986 * Z, g = -0.9689 * X + 1.8758 + 0.0415 * Z, b = 0.0557 * X - 0.2040 + 1.0570 * Z;
            return new Vector3((float)Math.Max(r, 1e-3), (float)Math.Max(g, 1e-3), (float)Math.Max(b, 1e-3));
        }

        // ---- what the lens takes from it (Cine.cs)

        /// <summary>The height of sensor the lens sees through, mm, for its angle of view: the chosen camera's format's width, as the picture's shape takes it. Less than nought where Cinema Camera's own is chosen.</summary>
        public static float High()
        {
            Body b = Chosen;
            if (b == null) return -1f;
            return FormatOf(b).Wide * Screen.height / Mathf.Max(1f, Screen.width);
        }

        /// <summary>For the camera's page: the sensor the lens is on, where a camera is chosen ("the ARRI ALEXA 35's 4.6K 3:2 Open Gate"); null where Cinema Camera's own is.</summary>
        public static string SensorSaid()
        {
            Body b = Chosen;
            return b == null ? null : "the " + b.Name + "'s " + FormatOf(b).Name;
        }

        static string KeyFor(Body b)
        {
            var key = new System.Text.StringBuilder("format");
            foreach (char c in b.Name) if (char.IsLetterOrDigit(c)) key.Append(c);
            return key.ToString();
        }

        static Format FormatOf(Body b)
        {
            string kept = mod != null ? mod.Kept(KeyFor(b)) : null;
            foreach (Format f in b.Formats) if (f.Name == kept) return f;
            return b.Formats[0];
        }

        // ---- running

        void Awake() => Setup();

        readonly Meter scope = new Meter();
        static Bodies running;
        static int drawnAt = -100;
        float chargeAt, freeAt;
        static float charge = -1f;
        static double free = -1.0;

        void Start() => running = this;

        void OnDestroy()
        {
            scope.Off();
            if (running == this) running = null;
            open = What.None;
        }

        void Update()
        {
            scope.Read();
            if (Time.unscaledTime > chargeAt)
            {
                chargeAt = Time.unscaledTime + 0.5f;
                charge = -1f;
                Vessel ship = FlightGlobals.ActiveVessel;
                if (ship != null)
                {
                    ship.GetConnectedResourceTotals(PartResourceLibrary.ElectricityHashcode, out double have, out double holds);
                    if (holds > 0.0) charge = (float)(have / holds);
                }
            }
            if (Time.unscaledTime > freeAt)
            {
                freeAt = Time.unscaledTime + 5f;
                try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(KSPUtil.ApplicationRootPath))).AvailableFreeSpace; }
                catch (Exception) { free = -1.0; }
            }
        }

        void LateUpdate()
        {
            // (the picture's spread, for the screens that show it, while one is drawn)
            bool drawn = Time.frameCount - drawnAt <= 3;
            if (!drawn) { scope.Off(); if (open != What.None && !testing) open = What.None; return; }
            Camera near = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            scope.Spot = Chosen != null && Chosen.Maker == Maker.Panasonic;
            scope.Use(near, 0f, 1f);
        }

        // ---- the screens

        enum What { None, Rate, Shutter, Speed, Nd, Iris, Balance, Tint, Format, Tool, Look, Peaking }
        struct Hot { public Rect Where; public What What; }
        static readonly List<Hot> hots = new List<Hot>();
        static readonly List<Rect> rows = new List<Rect>();
        static What open = What.None;
        static Rect openFrom;
        static Rect pickerBox;

        /// <summary>
        /// The chosen camera's screen, in place of Cinema Camera's own viewfinder (Cine.cs, on each repaint while it is up): false
        /// where Cinema Camera's own is chosen. top and bottom: where the picture is between cinema bars; lens: the focal length now,
        /// mm; focus: where the focus is now, m, and sharp: whether it has come to rest on what it was asked for; doing: what the
        /// camera is doing, in Cinema Camera's words (a move, a path), or nothing.
        /// </summary>
        public static bool Draw(float top, float bottom, float lens, float focus, bool sharp, string doing)
        {
            Body b = Chosen;
            if (b == null || Event.current.type != EventType.Repaint) return b != null;
            drawnAt = Time.frameCount;
            hots.Clear();
            Ready();
            var s = new Shown(b, top, bottom, lens, focus, sharp, doing);
            Marks(s);
            switch (b.Maker)
            {
                case Maker.Arri: Arri(s); break;
                case Maker.Venice: Venice(s); break;
                case Maker.SonyFx: SonyFx(s); break;
                case Maker.Red: Red(s); break;
                case Maker.Blackmagic: Blackmagic(s); break;
                case Maker.Canon: Canon(s); break;
                case Maker.Panasonic: Panasonic(s); break;
                case Maker.Fujifilm: Fujifilm(s); break;
                case Maker.Nikon: Nikon(s); break;
            }
            Picker(s);
            return true;
        }

        /// <summary>Everything a screen shows, worked out once a drawing.</summary>
        sealed class Shown
        {
            public readonly Body Body;
            public readonly Maker Maker;
            public readonly float Top, Bottom, Wide, K, Lens, Focus, Rate, Angle, Speed, Nd, Iris, Kelvin, Tint;
            public readonly bool Sharp, Rec, Laid, Thirds, Manual;
            public readonly string Doing, Clock, Format;
            public readonly double Seconds;
            public Shown(Body b, float top, float bottom, float lens, float focus, bool sharp, string doing)
            {
                Body = b; Maker = b.Maker; Top = top; Bottom = bottom; Wide = Screen.width; K = Screen.height / 720f;
                Lens = lens; Focus = focus; Sharp = sharp; Doing = doing ?? "";
                Rate = Rates[Mathf.Clamp(rate != null ? rate.Value : 1, 0, Rates.Length - 1)];
                float x = shutter != null ? shutter.Value : 48f;
                Angle = 360f * Rate / Mathf.Max(x, 1f);
                Speed = speed != null ? speed.Value : b.Base;
                Nd = filter != null ? filter.Value : 0f;
                Iris = stop != null ? stop.Value : 2.8f;
                Kelvin = balance != null ? balance.Value : 5600f;
                Tint = tint != null ? tint.Value : 0f;
                Rec = !FlightDriver.Pause;
                Laid = Luts.lutChoice != null && Luts.lutChoice.Value > 0 && Luts.strength != null && Luts.strength.Value > 0.001f;
                Thirds = finder != null && finder.Value == 2;
                Manual = focusMode != null && focusMode.Value == 2;
                Format = FormatOf(b).Name;
                Vessel ship = FlightGlobals.ActiveVessel;
                double t = ship != null ? Math.Max(0.0, ship.missionTime) : 0.0;
                Seconds = t;
                int whole = (int)t, frames = Mathf.RoundToInt(Rate) <= 0 ? 24 : Mathf.RoundToInt(Rate);
                Clock = (whole / 3600 % 24).ToString("00") + ":" + (whole / 60 % 60).ToString("00") + ":" + (whole % 60).ToString("00") + ":" + Mathf.Min((int)((t - whole) * frames), frames - 1).ToString("00");
            }
            /// <summary>The media's time left, minutes, at what the camera writes and the frame rate: from the room left on this computer's disk.</summary>
            public float MediaMinutes => free < 0.0 ? -1f : (float)(free / (Body.DataRate * 1048576.0 * Rate / 24.0) / 60.0);
        }

        static Texture2D white, round;
        static GUIStyle style;

        static void Ready()
        {
            if (white == null)
            {
                white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Cinema Camera: white" };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (round == null)
            {
                round = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Cinema Camera: dot", wrapMode = TextureWrapMode.Clamp };
                for (int j = 0; j < 32; j++)
                    for (int i = 0; i < 32; i++)
                        round.SetPixel(i, j, new Color(1f, 1f, 1f, Mathf.Clamp01(15.5f - Mathf.Sqrt((i - 15.5f) * (i - 15.5f) + (j - 15.5f) * (j - 15.5f)))));
                round.Apply();
            }
        }

        static void Box(Rect r, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = was;
        }

        static void Frame(Rect r, float thick, Color c)
        {
            Box(new Rect(r.x, r.y, r.width, thick), c); Box(new Rect(r.x, r.yMax - thick, r.width, thick), c);
            Box(new Rect(r.x, r.y, thick, r.height), c); Box(new Rect(r.xMax - thick, r.y, thick, r.height), c);
        }

        static void Dot(float x, float y, float size, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x - 0.5f * size, y - 0.5f * size, size, size), round);
            GUI.color = was;
        }

        static GUIStyle Style(float size, bool bold)
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { wordWrap = false, richText = false, clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
            style.fontSize = Mathf.Max(7, Mathf.RoundToInt(size));
            style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            style.normal.textColor = Color.white;
            return style;
        }

        static float Width(string text, float size, bool bold = true) => Style(size, bold).CalcSize(new GUIContent(text)).x;

        enum Edge { None, Shadow, Outline }

        /// <summary>Words on the screen; side -1 puts their left end at x, 0 their middle, 1 their right end; y is their top. Their width.</summary>
        static float Say(float x, float y, string text, float size, Color colour, int side = -1, Edge edge = Edge.None, bool bold = true)
        {
            GUIStyle st = Style(size, bold);
            var content = new GUIContent(text);
            Vector2 takes = st.CalcSize(content);
            var where = new Rect(side < 0 ? x : side == 0 ? x - 0.5f * takes.x : x - takes.x, y, takes.x + 2f, takes.y);
            Color was = GUI.color;
            if (edge != Edge.None)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.85f * colour.a);
                if (edge == Edge.Shadow) GUI.Label(new Rect(where.x + 1f, where.y + 1f, where.width, where.height), content, st);
                else
                {
                    GUI.Label(new Rect(where.x - 1f, where.y, where.width, where.height), content, st);
                    GUI.Label(new Rect(where.x + 1f, where.y, where.width, where.height), content, st);
                    GUI.Label(new Rect(where.x, where.y - 1f, where.width, where.height), content, st);
                    GUI.Label(new Rect(where.x, where.y + 1f, where.width, where.height), content, st);
                }
            }
            GUI.color = colour;
            GUI.Label(where, content, st);
            GUI.color = was;
            return takes.x;
        }

        /// <summary>A value that is set by clicking it: said, and its place kept for the click (and marked while its list is open).</summary>
        static float Value(float x, float y, string text, float size, Color colour, What what, int side = -1, Edge edge = Edge.None, bool bold = true)
        {
            float w = Say(x, y, text, size, colour, side, edge, bold);
            var r = new Rect(side < 0 ? x : side == 0 ? x - 0.5f * w : x - w, y, w, size * 1.2f);
            hots.Add(new Hot { Where = new Rect(r.x - 4f, r.y - 3f, r.width + 8f, r.height + 6f), What = what });
            if (open == what) Box(new Rect(r.x, r.yMax + 1f, r.width, Mathf.Max(1f, size * 0.12f)), colour);
            return w;
        }

        /// <summary>A label (small, dim) with its value under it, the value clickable; their width.</summary>
        static float Pair(float x, float y, string label, string value, What what, float k, Color dim, Color bright, float labelSize = 10.5f, float valueSize = 17f, Edge edge = Edge.None)
        {
            float a = Say(x, y, label, labelSize * k, dim, -1, edge, false);
            float b = what == What.None ? Say(x, y + labelSize * 1.15f * k, value, valueSize * k, bright, -1, edge) : Value(x, y + labelSize * 1.15f * k, value, valueSize * k, bright, what, -1, edge);
            return Mathf.Max(a, b);
        }

        /// <summary>The marks every screen has: a small cross in the middle, and the thirds if the viewfinder is set to show them.</summary>
        static void Marks(Shown s)
        {
            float k = s.K, midX = 0.5f * s.Wide, midY = 0.5f * (s.Top + s.Bottom), thin = Mathf.Max(1f, Mathf.Round(k));
            var c = new Color(1f, 1f, 1f, 0.7f);
            Box(new Rect(midX - 9f * k, midY - 0.5f * thin, 18f * k, thin), c);
            Box(new Rect(midX - 0.5f * thin, midY - 9f * k, thin, 18f * k), c);
            if (!s.Thirds) return;
            var faint = new Color(1f, 1f, 1f, 0.22f);
            for (int n = 1; n <= 2; n++)
            {
                Box(new Rect(s.Wide * n / 3f, s.Top, thin, s.Bottom - s.Top), faint);
                Box(new Rect(0f, s.Top + (s.Bottom - s.Top) * n / 3f, s.Wide, thin), faint);
            }
        }

        // ---- the values in each maker's words

        static float[] AnglesOf(Maker m) => m == Maker.Arri || m == Maker.Panasonic ? ArriAngles : m == Maker.Blackmagic ? BmdAngles : m == Maker.Canon ? CanonAngles : SonyAngles;

        /// <summary>The shutter as the camera sets it: an angle (snapped to the nearest of its own within a hair: the shutter's own setting is kept in whole fractions of a second) or, on Sony's FX cameras, a speed.</summary>
        static bool BySpeed(Maker m) => m == Maker.SonyFx || m == Maker.Fujifilm || m == Maker.Nikon;

        static string SpeedSaid(Maker m, float x) => (m == Maker.Fujifilm ? "" : "1/") + Mathf.RoundToInt(x).ToString(CultureInfo.InvariantCulture);

        static string ShutterSaid(Shown s)
        {
            if (BySpeed(s.Maker)) return SpeedSaid(s.Maker, shutter != null ? shutter.Value : 48f);
            float a = s.Angle;
            foreach (float own in AnglesOf(s.Maker)) if (Mathf.Abs(own - a) <= own * 0.006f) { a = own; break; }
            switch (s.Maker)
            {
                case Maker.Canon: return a.ToString("0.00", CultureInfo.InvariantCulture) + "°";
                case Maker.Panasonic: return a.ToString("0.0", CultureInfo.InvariantCulture) + "deg";
                case Maker.Red:
                case Maker.Blackmagic: return a.ToString("0.#", CultureInfo.InvariantCulture) + "°";
                default: return a.ToString("0.0", CultureInfo.InvariantCulture) + "°";
            }
        }

        static string RateSaid(Maker m, float r)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (m)
            {
                case Maker.Arri: return r.ToString("0.000", inv);
                case Maker.Blackmagic:
                case Maker.Fujifilm: return Mathf.Abs(r - Mathf.Round(r)) < 0.001f ? Mathf.RoundToInt(r).ToString(inv) : r.ToString("0.00", inv);
                case Maker.Nikon: return Mathf.RoundToInt(r).ToString(inv) + "p";
                default: return r.ToString("0.00", inv);
            }
        }

        /// <summary>The grey filter as the camera names it: an optical density (ARRI, Sony's VENICE, Panasonic), a fraction of the light (Sony's FX), or stops.</summary>
        static string NdSaid(Maker m, float stops)
        {
            var inv = CultureInfo.InvariantCulture;
            bool clear = stops < 0.05f;
            switch (m)
            {
                case Maker.Arri: return clear ? "Clear" : (stops * 0.3f).ToString("0.0", inv);
                case Maker.Venice: return clear ? "Clear" : (stops * 0.3f).ToString("0.0", inv);
                case Maker.SonyFx: return clear ? "ND Clear" : "ND 1/" + Mathf.RoundToInt(Mathf.Pow(2f, stops)).ToString(inv);
                case Maker.Red: return clear ? "Clear" : stops.ToString("0.0", inv);
                case Maker.Blackmagic: return clear ? "CLEAR" : Mathf.RoundToInt(stops) + " STOP";
                case Maker.Canon: return clear ? "ND OFF" : "ND " + stops.ToString("0.#", inv) + " stops";
                default: return clear ? "ND:CLEAR" : "ND:" + (stops * 0.3f).ToString("0.0", inv);
            }
        }

        static string IrisSaid(Maker m, float f)
        {
            var inv = CultureInfo.InvariantCulture;
            string n = f.ToString(f < 10f ? "0.0" : "0", inv);
            switch (m)
            {
                case Maker.Arri: return "T " + n;
                case Maker.Venice: return "T" + n;
                case Maker.Blackmagic: return "f" + n;
                case Maker.Panasonic: return "T" + f.ToString("0.0", inv);
                default: return "F" + n;
            }
        }

        static string TintSaid(Maker m, float t)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (m)
            {
                case Maker.Venice: return (t >= 0f ? "+" : "-") + Mathf.Abs(Mathf.RoundToInt(t * 10f)).ToString("00", inv);
                case Maker.Blackmagic: return Mathf.RoundToInt(t * 5f).ToString(inv);
                case Maker.Canon: return Mathf.Abs(t) < 0.05f ? "±0" : (t > 0f ? "+" : "-") + Mathf.Abs(Mathf.RoundToInt(t)).ToString(inv);
                case Maker.Red: return t.ToString("0.0", inv);
                default: return (t >= 0f ? "+" : "-") + Mathf.Abs(t).ToString("0.0", inv);
            }
        }

        static string Kelvin(float k) => Mathf.RoundToInt(k).ToString(CultureInfo.InvariantCulture) + "K";

        static string Metres(float m) => m >= 999.5f ? "∞" : m >= 10f ? m.ToString("F0", CultureInfo.InvariantCulture) + " m" : m.ToString("F1", CultureInfo.InvariantCulture) + " m";

        static string Minutes(float m) => m < 0f ? "--min" : Mathf.Min(Mathf.FloorToInt(m), 9999) + "min";

        static string LookName(Shown s)
        {
            if (Luts.lutChoice == null || Luts.lutChoice.Value <= 0) return "No LUT";
            string n = Luts.lutChoice.Options[Luts.lutChoice.Value];
            return n.Length > 26 ? n.Substring(0, 25) + "…" : n;
        }

        static bool Peaking => peaking != null && peaking.Value;

        static string Curve => Luts.recorded != null ? Luts.CurveName(Luts.recorded.Value) : "As seen (Rec.709)";

        static readonly Color White = new Color(1f, 1f, 1f, 1f), Dim = new Color(0.62f, 0.62f, 0.62f, 1f), Green = new Color(0.3f, 0.85f, 0.35f, 1f),
            RecRed = new Color(0.95f, 0.15f, 0.12f, 1f), Orange = new Color(1f, 0.6f, 0.15f, 1f), Amber = new Color(1f, 0.78f, 0.2f, 1f);

        // ---- ARRI (ALEXA 35, Mini LF, Mini, AMIRA, 65): the live view's strip under the picture (small grey labels, white values, the
        // status in green or red), and a thin one over it for the format, the look, the media and the power

        static void Arri(Shown s)
        {
            float k = s.K, W = s.Wide, strip = 32f * k, y0 = s.Bottom - strip, size = 15.5f * k, label = 10.5f * k;
            Box(new Rect(0f, y0, W, strip), new Color(0f, 0f, 0f, 0.78f));
            float ty = y0 + 0.5f * (strip - size * 1.2f), ly = ty + 0.32f * (size - label);
            float x = 14f * k;
            Dot(x + 5f * k, y0 + 0.5f * strip, 10f * k, s.Rec ? RecRed : Green);
            x += 14f * k;
            x += Say(x, ty, s.Rec ? "REC" : "STBY", size, s.Rec ? RecRed : Green) + 22f * k;
            void Inline(string l, string v, What w)
            {
                x += Say(x, ly, l, label, Dim, -1, Edge.None, false) + 5f * k;
                x += Value(x, ty, v, size, White, w) + 18f * k;
            }
            Inline("FPS", RateSaid(s.Maker, s.Rate), What.Rate);
            Inline("SHTR", ShutterSaid(s), What.Shutter);
            Inline("EI", Mathf.RoundToInt(s.Speed).ToString(CultureInfo.InvariantCulture), What.Speed);
            if (s.Body.Nd != null) Inline("ND", NdSaid(s.Maker, s.Nd), What.Nd);
            else if (s.Nd > 0.05f) Inline("ND", "EXT " + NdSaid(s.Maker, s.Nd), What.None);
            Inline("WB", Kelvin(s.Kelvin), What.Balance);
            Inline("CC", TintSaid(s.Maker, s.Tint), What.Tint);
            // (from the right: the timecode, then the lens's data)
            float r = W - 14f * k;
            r -= Say(r, ty, s.Clock, size, White, 1) + 5f * k;
            r -= Say(r, ly, "TC", label, Dim, 1, Edge.None, false) + 20f * k;
            r -= Say(r, ty, Mathf.RoundToInt(s.Lens) + " mm", size, White, 1) + 14f * k;
            r -= Say(r, ty, Metres(s.Focus), size, s.Sharp ? White : Amber, 1) + 14f * k;
            Value(r, ty, IrisSaid(s.Maker, s.Iris), size, White, What.Iris, 1);

            float band = 24f * k, small = 12f * k, by = s.Top + 0.5f * (band - small * 1.2f);
            Box(new Rect(0f, s.Top, W, band), new Color(0f, 0f, 0f, 0.62f));
            float tx = 14f * k;
            tx += Value(tx, by, s.Format, small, White, What.Format) + 8f * k;
            tx += Say(tx, by, "·  " + s.Body.Codec, small, Dim) + 18f * k;
            Value(0.5f * W, by, "LOOK  " + (s.Laid ? LookName(s) : Curve), small, s.Laid ? White : Dim, What.Look, 0);
            float rr = W - 14f * k;
            rr -= Say(rr, by, charge >= 0f ? (21f + 8.4f * charge).ToString("0.0", CultureInfo.InvariantCulture) + "V" : "DC 24.0V", small, charge >= 0f && charge < 0.15f ? RecRed : White, 1) + 16f * k;
            rr -= Say(rr, by, "MEDIA " + Minutes(s.MediaMinutes), small, White, 1) + 16f * k;
            int t = Tool;
            rr -= Value(rr, by, t == 0 ? "FALSE COLOR" : t == 4 ? "ZEBRA " + Mathf.RoundToInt(ZebraLevel * 100f) + "%" : "FALSE COLOR", small, t == 0 ? Dim : Amber, What.Tool, 1) + 16f * k;
            Value(rr, by, "PEAKING", small, Peaking ? Amber : Dim, What.Peaking, 1);
            if (s.Doing.Length > 0) Say(0.5f * W, s.Top + band + 8f * k, s.Doing, small, new Color(1f, 1f, 1f, 0.75f), 0, Edge.Shadow);
        }

        // ---- Sony VENICE 2: the home screen's six tiles (white labels, orange values) along the foot, and its status line over them

        static void Venice(Shown s)
        {
            float k = s.K, W = s.Wide, margin = 10f * k, gap = 3f * k, tileH = 58f * k;
            float tileW = (W - 2f * margin - 5f * gap) / 6f, y0 = s.Bottom - margin - tileH;
            string[] labels = { "FPS", "EI", "Shutter", "ND", "Look", "WB" };
            for (int i = 0; i < 6; i++)
            {
                var r = new Rect(margin + i * (tileW + gap), y0, tileW, tileH);
                Box(r, new Color(0.05f, 0.05f, 0.05f, 0.86f));
                Say(r.x + 9f * k, r.y + 6f * k, labels[i], 12.5f * k, White, -1, Edge.None, false);
                float vy = r.y + 24f * k, vs = 22f * k;
                switch (i)
                {
                    case 0: Value(r.x + 9f * k, vy, RateSaid(s.Maker, s.Rate), vs, Orange, What.Rate); break;
                    case 1: Value(r.x + 9f * k, vy, Mathf.RoundToInt(s.Speed).ToString(CultureInfo.InvariantCulture), vs, Orange, What.Speed); break;
                    case 2: Value(r.x + 9f * k, vy, ShutterSaid(s), vs, Orange, What.Shutter); break;
                    case 3: Value(r.x + 9f * k, vy, NdSaid(s.Maker, s.Nd), vs, Orange, What.Nd); break;
                    case 4: Value(r.x + 9f * k, vy, s.Laid ? "s709" : "S-Log3", vs, Orange, What.Look); break;
                    case 5:
                        float w = Value(r.x + 9f * k, vy, Mathf.RoundToInt(s.Kelvin).ToString(CultureInfo.InvariantCulture), vs, Orange, What.Balance);
                        Value(r.x + 9f * k + w + 8f * k, vy + 0.3f * vs, TintSaid(s.Maker, s.Tint), 0.62f * vs, Orange, What.Tint);
                        break;
                }
            }
            float line = 24f * k, ly = y0 - gap - line, small = 13f * k, ty = ly + 0.5f * (line - small * 1.2f);
            Box(new Rect(margin, ly, W - 2f * margin, line), new Color(0.05f, 0.05f, 0.05f, 0.78f));
            float x = margin + 8f * k;
            var tag = new Rect(x, ly + 4f * k, Width(s.Rec ? "Rec" : "Stby", small) + 12f * k, line - 8f * k);
            Box(tag, s.Rec ? RecRed : new Color(0.15f, 0.55f, 0.2f, 1f));
            Say(tag.x + 6f * k, ty, s.Rec ? "Rec" : "Stby", small, White);
            x = tag.xMax + 14f * k;
            x += Say(x, ty, "TCG " + s.Clock, small, White) + 22f * k;
            x += Value(x, ty, s.Format, small, White, What.Format) + 16f * k;
            x += Say(x, ty, s.Body.Codec, small, Dim) + 16f * k;
            x += Value(x, ty, Tool == 4 ? "Zebra " + Mathf.RoundToInt(ZebraLevel * 100f) : "Zebra Off", small, Tool == 4 ? Amber : Dim, What.Tool) + 16f * k;
            Value(x, ty, Peaking ? "Peaking On" : "Peaking Off", small, Peaking ? Amber : Dim, What.Peaking);
            float r2 = W - margin - 8f * k;
            r2 -= Say(r2, ty, charge >= 0f ? (12f + 4.8f * charge).ToString("0.0", CultureInfo.InvariantCulture) + "V" : "DC IN", small, White, 1) + 18f * k;
            r2 -= Say(r2, ty, "A: " + Minutes(s.MediaMinutes), small, White, 1) + 18f * k;
            Value(r2, ty, IrisSaid(s.Maker, s.Iris), small, White, What.Iris, 1);
            if (s.Doing.Length > 0) Say(0.5f * W, s.Top + 12f * k, s.Doing, small, new Color(1f, 1f, 1f, 0.75f), 0, Edge.Shadow);
        }

        // ---- Sony's FX cameras (FX9, FX6, FX3, FX30) and BURANO: white words with a dark edge round the picture, where the FX6's
        // manual puts them (its "Screen Display": the status at the top, the format down the right, the exposure along the foot)

        static void SonyFx(Shown s)
        {
            float k = s.K, W = s.Wide, size = 15f * k, L = 18f * k, R = W - 18f * k, top = s.Top + 10f * k;
            Edge e = Edge.Outline;
            // (top: the recording state in the middle, the battery at the right; under them the focus)
            float w = Width("A", size * 0.8f) + 6f * k;
            float cx = 0.5f * W - 0.5f * (w + 6f * k + Width(s.Rec ? "●Rec" : "Stby", size));
            Frame(new Rect(cx, top + 1f * k, w, size * 1.1f), Mathf.Max(1f, k), White);
            Say(cx + 3f * k, top + 2f * k, "A", size * 0.8f, White, -1, e);
            Say(cx + w + 6f * k, top, s.Rec ? "●Rec" : "Stby", size, s.Rec ? RecRed : White, -1, e);
            Say(R, top, charge >= 0f ? Mathf.RoundToInt(charge * 100f) + "%" : "DC IN", size, charge >= 0f && charge < 0.15f ? RecRed : White, 1, e);
            Say(L, top + 26f * k, (s.Manual ? "MF " : "AF ") + Metres(s.Focus).Replace(" ", ""), size, s.Sharp ? White : Amber, -1, e);
            // (down the right: the format, frame rate, codec, the look, the LUT, the base ISO)
            float y = top + 26f * k, step = 24f * k;
            Value(R, y, s.Format, size, White, What.Format, 1, e); y += step;
            Value(R, y, RateSaid(s.Maker, s.Rate) + "P", size, White, What.Rate, 1, e); y += step;
            Say(R, y, s.Body.Codec, size, White, 1, e); y += step;
            bool log = Luts.recorded != null && Luts.recorded.Value == 3;
            Say(R, y, log ? "S-Log3" : "S-Cinetone", size, White, 1, e); y += step;
            Value(R, y, s.Laid ? "LUT s709" : "LUT Off", size, White, What.Look, 1, e); y += step;
            Say(R, y, "Base " + Mathf.RoundToInt(s.Body.Base), size, White, 1, e); y += step;
            Value(R, y, Tool == 4 ? "Zebra " + Mathf.RoundToInt(ZebraLevel * 100f) : "Zebra Off", size, Tool == 4 ? Amber : White, What.Tool, 1, e); y += step;
            Value(R, y, Peaking ? "Peaking" : "Peaking Off", size, Peaking ? Amber : White, What.Peaking, 1, e);
            // (the left, at the middle: the media's time left)
            Say(L, 0.5f * (s.Top + s.Bottom) + 20f * k, "A  " + Minutes(s.MediaMinutes), size, White, -1, e);
            // (the histogram over the foot's right)
            float hw = 150f * k, hh = 80f * k;
            Histogram(new Rect(R - hw, s.Bottom - 64f * k - hh, hw, hh), new Color(0f, 0f, 0f, 0.35f), new Color(1f, 1f, 1f, 0.75f), White, false);
            // (above the foot: the white balance; along it: the timecode, ND, iris, ISO or EI, shutter, a level and the sound)
            float fy = s.Bottom - 30f * k, ay = fy - 26f * k;
            float bw = Value(L, ay, "W:P " + Kelvin(s.Kelvin), size, White, What.Balance, -1, e);
            Value(L + bw + 10f * k, ay, TintSaid(s.Maker, s.Tint), size * 0.85f, White, What.Tint, -1, e);
            Say(L, fy, "TCG " + s.Clock, size, White, -1, e);
            if (s.Body.Nd != null) Value(0.22f * W, fy, NdSaid(s.Maker, s.Nd), size, White, What.Nd, -1, e);
            Value(0.33f * W, fy, IrisSaid(s.Maker, s.Iris), size, White, What.Iris, -1, e);
            Value(0.43f * W, fy, (log ? "EI " : "ISO ") + Mathf.RoundToInt(s.Speed), size, White, What.Speed, -1, e);
            Value(0.58f * W, fy, ShutterSaid(s), size, White, What.Shutter, -1, e);
            float lx = 0.72f * W, lw = 60f * k, mid = fy + 0.6f * size;
            Box(new Rect(lx, mid - 1f * k, lw, 2f * k), White);
            Box(new Rect(lx + 0.5f * lw - 1.5f * k, mid - 6f * k, 3f * k, 12f * k), White);
            Meters(R - 110f * k, fy - 2f * k, 110f * k, 20f * k, k);
            if (s.Doing.Length > 0) Say(0.5f * W, top + 26f * k, s.Doing, 12.5f * k, new Color(1f, 1f, 1f, 0.75f), 0, e);
        }

        // ---- RED (V-RAPTOR, KOMODO): the rows over and under the picture: frame rate, ISO, iris, shutter, white balance, the format,
        // codec and look above; the histogram with its goal posts and traffic lights, the timecode, the record state, media and
        // battery below (its operation guides' upper and lower status rows)

        static void Red(Shown s)
        {
            float k = s.K, W = s.Wide, row = 42f * k;
            Color bar = new Color(0.02f, 0.02f, 0.02f, 0.86f), dim = new Color(0.6f, 0.6f, 0.6f, 1f);
            Box(new Rect(0f, s.Top, W, row), bar);
            var cells = new List<KeyValuePair<string, KeyValuePair<string, What>>>
            {
                Cell("FPS", RateSaid(s.Maker, s.Rate), What.Rate),
                Cell("ISO", Mathf.RoundToInt(s.Speed).ToString(CultureInfo.InvariantCulture), What.Speed),
                Cell("IRIS", IrisSaid(s.Maker, s.Iris), What.Iris),
                Cell("SHUTTER", ShutterSaid(s), What.Shutter),
                Cell("WB", Kelvin(s.Kelvin), What.Balance),
                Cell("TINT", TintSaid(s.Maker, s.Tint), What.Tint),
            };
            if (s.Body.Nd != null) cells.Add(Cell("ND", NdSaid(s.Maker, s.Nd), What.Nd));
            cells.Add(Cell("RES", s.Format, What.Format));
            cells.Add(Cell("CODEC", s.Body.Codec, What.None));
            cells.Add(Cell("LOOK", s.Laid ? "IPP2 709" : "Log3G10", What.Look));
            float each = (W - 20f * k) / cells.Count;
            for (int i = 0; i < cells.Count; i++)
            {
                float x = 10f * k + i * each;
                if (i > 0) Box(new Rect(x - 1f, s.Top + 8f * k, 1f, row - 16f * k), new Color(1f, 1f, 1f, 0.18f));
                Pair(x + 8f * k, s.Top + 5f * k, cells[i].Key, cells[i].Value.Key, cells[i].Value.Value, k, dim, White, 10f, 16.5f);
            }
            float low = 58f * k, y0 = s.Bottom - low;
            Box(new Rect(0f, y0, W, low), bar);
            var hist = new Rect(14f * k, y0 + 8f * k, 230f * k, low - 16f * k);
            Histogram(hist, new Color(0.1f, 0.1f, 0.1f, 1f), new Color(0.85f, 0.85f, 0.85f, 0.9f), White, true);
            // (the traffic lights: red, green and blue, each lit while its colour clips in more than a little of the picture)
            float lx = hist.xMax + 16f * k, ly = y0 + 0.5f * low;
            Color[] lit = { new Color(1f, 0.2f, 0.2f, 1f), new Color(0.25f, 1f, 0.3f, 1f), new Color(0.3f, 0.5f, 1f, 1f) };
            float[] share = { running != null ? running.scope.ClipRed : 0f, running != null ? running.scope.ClipGreen : 0f, running != null ? running.scope.ClipBlue : 0f };
            for (int i = 0; i < 3; i++) Dot(lx + 6f * k, ly - 14f * k + i * 14f * k, 10f * k, share[i] > 0.02f ? lit[i] : new Color(lit[i].r * 0.25f, lit[i].g * 0.25f, lit[i].b * 0.25f, 1f));
            float x2 = lx + 30f * k;
            x2 += Pair(x2, y0 + 10f * k, "TC", s.Clock, What.None, k, dim, White, 10f, 18f) + 26f * k;
            Say(x2, y0 + 10f * k + 11.5f * k, s.Rec ? "REC" : "STBY", 18f * k, s.Rec ? RecRed : dim);
            x2 += 70f * k;
            int t = Tool;
            x2 += Pair(x2, y0 + 10f * k, "TOOL", t == 0 ? "OFF" : t == 4 ? "ZEBRA " + Mathf.RoundToInt(ZebraLevel * 100f) : "EXPOSURE", What.Tool, k, dim, t == 0 ? White : Amber, 10f, 18f) + 26f * k;
            Pair(x2, y0 + 10f * k, "FOCUS", Peaking ? "PEAKING" : "OFF", What.Peaking, k, dim, Peaking ? Amber : White, 10f, 18f);
            float r = W - 16f * k;
            r -= Pair(r - 70f * k, y0 + 10f * k, "BATTERY", charge >= 0f ? Mathf.RoundToInt(charge * 100f) + "%" : "DC", What.None, k, dim, charge >= 0f && charge < 0.15f ? RecRed : White, 10f, 18f) + 28f * k;
            float m = s.MediaMinutes;
            Pair(r - 110f * k, y0 + 10f * k, "MEDIA", m < 0f ? "--" : Mathf.FloorToInt(m / 60f) + "h " + Mathf.FloorToInt(m % 60f) + "m", What.None, k, dim, White, 10f, 18f);
            if (s.Doing.Length > 0) Say(0.5f * W, s.Top + row + 8f * k, s.Doing, 12.5f * k, new Color(1f, 1f, 1f, 0.75f), 0, Edge.Shadow);
        }

        static KeyValuePair<string, KeyValuePair<string, What>> Cell(string label, string value, What what) =>
            new KeyValuePair<string, KeyValuePair<string, What>>(label, new KeyValuePair<string, What>(value, what));

        // ---- Blackmagic (URSA Cine, URSA Mini Pro, PYXIS, Pocket): Blackmagic OS's see-through strip across the top (frame rate,
        // shutter, iris, the timecode in the middle, red while recording, ISO, white balance, tint, battery), and along the foot the
        // histogram, the media's time left, the big record button and the sound

        static void Blackmagic(Shown s)
        {
            float k = s.K, W = s.Wide, strip = 48f * k;
            Box(new Rect(0f, s.Top, W, strip), new Color(0f, 0f, 0f, 0.55f));
            Color dim = new Color(0.72f, 0.72f, 0.72f, 1f);
            float y = s.Top + 6f * k, x = 22f * k, gap = 26f * k;
            x += Pair(x, y, "FPS", RateSaid(s.Maker, s.Rate), What.Rate, k, dim, White, 10.5f, 19f) + gap;
            x += Pair(x, y, "SHUTTER", ShutterSaid(s), What.Shutter, k, dim, White, 10.5f, 19f) + gap;
            x += Pair(x, y, "IRIS", IrisSaid(s.Maker, s.Iris), What.Iris, k, dim, White, 10.5f, 19f) + gap;
            if (s.Body.Nd != null) Pair(x, y, "ND", NdSaid(s.Maker, s.Nd), What.Nd, k, dim, White, 10.5f, 19f);
            Say(0.5f * W, s.Top + 0.5f * (strip - 24f * k * 1.2f), s.Clock, 24f * k, s.Rec ? RecRed : White, 0);
            float r = W - 22f * k;
            float bw = 30f * k, bh = 15f * k, by = s.Top + 0.5f * strip - 0.5f * bh;
            Frame(new Rect(r - bw, by, bw, bh), Mathf.Max(1f, k), White);
            Box(new Rect(r, by + 4f * k, 3f * k, bh - 8f * k), White);
            if (charge >= 0f) Box(new Rect(r - bw + 3f * k, by + 3f * k, (bw - 6f * k) * Mathf.Clamp01(charge), bh - 6f * k), charge < 0.15f ? RecRed : White);
            r -= bw + 8f * k;
            r -= Say(r, s.Top + 0.5f * (strip - 15f * k * 1.2f), charge >= 0f ? Mathf.RoundToInt(charge * 100f) + "%" : "DC", 15f * k, White, 1) + gap;
            float tw = Mathf.Max(Width("TINT", 10.5f * k, false), Width(TintSaid(s.Maker, s.Tint), 19f * k));
            Pair(r - tw, y, "TINT", TintSaid(s.Maker, s.Tint), What.Tint, k, dim, White, 10.5f, 19f); r -= tw + gap;
            float ww = Mathf.Max(Width("WB", 10.5f * k, false), Width(Kelvin(s.Kelvin), 19f * k));
            Pair(r - ww, y, "WB", Kelvin(s.Kelvin), What.Balance, k, dim, White, 10.5f, 19f); r -= ww + gap;
            float iw = Mathf.Max(Width("ISO", 10.5f * k, false), Width(Mathf.RoundToInt(s.Speed).ToString(CultureInfo.InvariantCulture), 19f * k));
            Pair(r - iw, y, "ISO", Mathf.RoundToInt(s.Speed).ToString(CultureInfo.InvariantCulture), What.Speed, k, dim, White, 10.5f, 19f);
            // (the foot)
            var hist = new Rect(18f * k, s.Bottom - 74f * k, 150f * k, 56f * k);
            Histogram(hist, new Color(0f, 0f, 0f, 0.55f), new Color(1f, 1f, 1f, 0.85f), White, false);
            float mx = hist.xMax + 16f * k, my = s.Bottom - 58f * k;
            Value(mx, my - 16f * k, s.Format + "  ·  " + s.Body.Codec, 11.5f * k, dim, What.Format, -1, Edge.Shadow, false);
            float m = s.MediaMinutes;
            int sec = m < 0f ? 0 : (int)Mathf.Min(m * 60f, 359999f);
            Say(mx, my + 2f * k, "CFE 1   " + (m < 0f ? "--:--:--" : (sec / 3600) + ":" + (sec / 60 % 60).ToString("00") + ":" + (sec % 60).ToString("00")), 17f * k, White, -1, Edge.Shadow);
            float bx = 0.5f * W, byy = s.Bottom - 40f * k;
            Dot(bx, byy, 52f * k, new Color(0f, 0f, 0f, 0.55f));
            Dot(bx, byy, 46f * k, s.Rec ? RecRed : new Color(0.85f, 0.85f, 0.85f, 1f));
            Dot(bx, byy, 40f * k, s.Rec ? new Color(0.75f, 0.08f, 0.06f, 1f) : new Color(0.12f, 0.12f, 0.12f, 1f));
            if (!s.Rec) Dot(bx, byy, 18f * k, RecRed);
            // (chips over the sound, at the right: the LUT and the exposure tool)
            float cr = W - 18f * k, cy = s.Bottom - 74f * k;
            int t = Tool;
            cr -= Chip(cr, cy, t == 0 ? "FALSE COLOR" : t == 4 ? "ZEBRA " + Mathf.RoundToInt(ZebraLevel * 100f) + "%" : "FALSE COLOR", t != 0, What.Tool, k) + 8f * k;
            cr -= Chip(cr, cy, "LUT", s.Laid, What.Look, k) + 8f * k;
            Chip(cr, cy, "FOCUS", Peaking, What.Peaking, k);
            Meters(W - 18f * k - 150f * k, s.Bottom - 40f * k, 150f * k, 22f * k, k);
            if (s.Doing.Length > 0) Say(0.5f * W, s.Top + strip + 8f * k, s.Doing, 12.5f * k, new Color(1f, 1f, 1f, 0.75f), 0, Edge.Shadow);
        }

        /// <summary>A small rounded-off tab with words, lit while on, clicked to set; drawn leftwards from its right end. Its width.</summary>
        static float Chip(float right, float y, string text, bool on, What what, float k)
        {
            float size = 11.5f * k, w = Width(text, size) + 14f * k, h = size * 1.2f + 8f * k;
            var r = new Rect(right - w, y, w, h);
            Box(r, on ? new Color(1f, 1f, 1f, 0.92f) : new Color(0f, 0f, 0f, 0.55f));
            if (!on) Frame(r, Mathf.Max(1f, k), new Color(1f, 1f, 1f, 0.6f));
            Say(r.x + 7f * k, r.y + 4f * k, text, size, on ? Color.black : White);
            hots.Add(new Hot { Where = r, What = what });
            return w;
        }

        // ---- Canon (C400, C80, C500 Mark II, C300 Mark III, C70, R5 C): white words with a dark edge, as the C70's manual's
        // "Onscreen Displays" put them: the media at the top left, the record state at the top, the frame rate, timecode and battery
        // at the top right; the lens down the left, the format down the right; the exposure bar and, along the foot, the white
        // balance, ND, iris, ISO and the shutter

        static void Canon(Shown s)
        {
            float k = s.K, W = s.Wide, size = 15f * k, L = 18f * k, R = W - 18f * k, top = s.Top + 10f * k;
            Edge e = Edge.Outline;
            var media = new Rect(L, top + 1f * k, Width("A", size * 0.85f) + 8f * k, size * 1.1f);
            Box(media, new Color(0.2f, 0.75f, 0.3f, 1f));
            Say(media.x + 4f * k, top + 1.5f * k, "A", size * 0.85f, Color.black);
            float x = media.xMax + 6f * k;
            x += Say(x, top, Minutes(s.MediaMinutes).Replace("min", " min"), size, White, -1, e) + 12f * k;
            Say(x, top + 2f * k, s.Body.Codec, size * 0.75f, White, -1, e);
            Say(0.5f * W, top, s.Rec ? "● REC" : "STBY", size, s.Rec ? RecRed : White, 0, e);
            float r = R;
            r -= Say(r, top, charge >= 0f ? Mathf.RoundToInt(charge * 200f) + " min" : "DC IN", size, charge >= 0f && charge < 0.15f ? RecRed : White, 1, e) + 16f * k;
            r -= Say(r, top, s.Clock + (s.Rec ? " R" : " P"), size, White, 1, e) + 16f * k;
            Value(r, top, RateSaid(s.Maker, s.Rate) + "P", size, White, What.Rate, 1, e);
            float y = top + 26f * k, step = 23f * k;
            Say(L, y, Mathf.RoundToInt(s.Lens) + " mm", size, White, -1, e); y += step;
            Say(L, y, Metres(s.Focus), size, s.Sharp ? White : Amber, -1, e); y += step;
            Say(L, y, s.Manual ? "MF" : "AF", size, White, -1, e); y += step;
            bool log2 = Luts.recorded != null && Luts.recorded.Value == 9, log3 = Luts.recorded != null && Luts.recorded.Value == 5;
            Say(L, y, log2 ? "C.Log2" : log3 ? "C.Log3" : "BT.709", size, White, -1, e); y += step;
            Value(L, y, s.Laid ? "LUT BT.709" : "LUT OFF", size, White, What.Look, -1, e); y += step;
            Value(L, y, Tool == 4 ? "ZEBRA " + Mathf.RoundToInt(ZebraLevel * 100f) + "%" : "ZEBRA OFF", size, Tool == 4 ? Amber : White, What.Tool, -1, e); y += step;
            Value(L, y, Peaking ? "PEAKING 1" : "PEAKING OFF", size, Peaking ? Amber : White, What.Peaking, -1, e);
            Value(R, top + 26f * k, s.Format, size, White, What.Format, 1, e);
            Say(R, top + 49f * k, "YCC422 10 bit", size, White, 1, e);
            // (the exposure bar: two stops either way, a tick each third; the mark where the picture's light comes against middle grey)
            float fy = s.Bottom - 28f * k, barY = fy - 26f * k, half = 120f * k, mid = 0.5f * W, thin = Mathf.Max(1f, Mathf.Round(k));
            Say(L, fy - 26f * k, "A001C001", size, White, -1, e);
            Box(new Rect(mid - half, barY + 8f * k, 2f * half, thin), White);
            for (int i = -6; i <= 6; i++)
            {
                float tall = (i % 3 == 0 ? 8f : 4f) * k;
                Box(new Rect(mid + i * half / 6f - 0.5f * thin, barY + 8f * k - tall, thin, tall), White);
            }
            Say(mid - half - 6f * k, barY, "-", size, White, 1, e);
            Say(mid + half + 6f * k, barY, "+", size, White, -1, e);
            float reading = running != null ? running.scope.Reading : float.NaN;
            if (!float.IsNaN(reading)) Box(new Rect(mid + Mathf.Clamp(reading, -2.2f, 2.2f) * half / 2f - 2f * k, barY + 11f * k, 4f * k, 9f * k), Mathf.Abs(reading) > 2f ? Amber : White);
            float bw = Value(0.22f * W, fy, Kelvin(s.Kelvin), size, White, What.Balance, -1, e);
            Value(0.22f * W + bw + 8f * k, fy, TintSaid(s.Maker, s.Tint), size, White, What.Tint, -1, e);
            if (s.Body.Nd != null) Value(0.38f * W, fy, NdSaid(s.Maker, s.Nd), size, White, What.Nd, -1, e);
            Value(0.54f * W, fy, IrisSaid(s.Maker, s.Iris), size, White, What.Iris, -1, e);
            Value(0.64f * W, fy, "ISO " + Mathf.RoundToInt(s.Speed), size, White, What.Speed, -1, e);
            Value(R, fy, ShutterSaid(s), size, White, What.Shutter, 1, e);
            Waveform(new Rect(R - 150f * k, fy - 104f * k, 150f * k, 72f * k), new Color(0f, 0f, 0f, 0.45f), new Color(0.75f, 1f, 0.75f, 1f));
            if (s.Doing.Length > 0) Say(0.5f * W, top + 26f * k, s.Doing, 12.5f * k, new Color(1f, 1f, 1f, 0.75f), 0, e);
        }

        // ---- Panasonic (VariCam 35, VariCam LT, AU-EVA1): the status display of their manuals, one line of small white words across
        // the top (clip, format, frame rate, codec, the record state with the media's time left, timecode, power) and one across the
        // foot (frame rate, shutter in degrees, EI, white balance with its tint, iris, focus, focal length, ND), the spot's
        // brightness in stops ("Y GET") over the middle of the foot

        static void Panasonic(Shown s)
        {
            float k = s.K, W = s.Wide, size = 13.5f * k, L = 16f * k, R = W - 16f * k, top = s.Top + 8f * k, gap = 16f * k;
            Edge e = Edge.Outline;
            var inv = CultureInfo.InvariantCulture;
            float x = L;
            x += Say(x, top, "A001C003", size, White, -1, e) + gap;
            x += Value(x, top, s.Format, size, White, What.Format, -1, e) + gap;
            x += Value(x, top, RateSaid(s.Maker, s.Rate) + "p", size, White, What.Rate, -1, e) + gap;
            x += Value(x, top, s.Laid ? "V-709" : "V-Log", size, White, What.Look, -1, e) + gap;
            x += Say(x, top, s.Body.Codec, size, White, -1, e) + gap;
            Dot(x + 4f * k, top + 0.6f * size, 8f * k, s.Rec ? RecRed : White);
            x += 12f * k;
            x += Say(x, top, Minutes(s.MediaMinutes), size, White, -1, e) + gap;
            float r = R;
            r -= Say(r, top, charge >= 0f ? (12f + 4.8f * charge).ToString("0.0", inv) + "V" : "DC IN", size, charge >= 0f && charge < 0.15f ? RecRed : White, 1, e) + gap;
            Say(r, top, "TC " + s.Clock, size, White, 1, e);
            Value(R, top + 24f * k, Tool == 4 ? "ZEBRA " + Mathf.RoundToInt(ZebraLevel * 100f) + "%" : "ZEBRA OFF", size, Tool == 4 ? Amber : White, What.Tool, 1, e);
            Value(R, top + 46f * k, Peaking ? "PEAKING" : "PEAKING OFF", size, Peaking ? Amber : White, What.Peaking, 1, e);
            float fy = s.Bottom - 24f * k;
            x = L;
            x += Value(x, fy, RateSaid(s.Maker, s.Rate) + "fps", size, White, What.Rate, -1, e) + gap;
            x += Value(x, fy, ShutterSaid(s), size, White, What.Shutter, -1, e) + gap;
            x += Value(x, fy, "ISO" + Mathf.RoundToInt(s.Speed), size, White, What.Speed, -1, e) + gap;
            x += Value(x, fy, "A" + Kelvin(s.Kelvin), size, White, What.Balance, -1, e);
            x += Value(x, fy, TintSaid(s.Maker, s.Tint), size, White, What.Tint, -1, e) + gap;
            x += Value(x, fy, IrisSaid(s.Maker, s.Iris), size, White, What.Iris, -1, e) + gap;
            x += Say(x, fy, Metres(s.Focus).Replace(" ", ""), size, s.Sharp ? White : Amber, -1, e) + gap;
            x += Say(x, fy, s.Lens.ToString("0.0", inv) + "mm", size, White, -1, e) + gap;
            Value(R, fy, NdSaid(s.Maker, s.Nd), size, White, What.Nd, 1, e);
            float reading = running != null ? running.scope.Reading : float.NaN;
            if (!float.IsNaN(reading)) Say(0.5f * W, fy - 24f * k, (reading >= 0f ? "+" : "") + reading.ToString("0.0", inv) + "STOP", size, White, 0, e);
            Waveform(new Rect(R - 150f * k, fy - 100f * k, 150f * k, 72f * k), new Color(0f, 0f, 0f, 0.45f), White);
            if (s.Doing.Length > 0) Say(0.5f * W, top + 24f * k, s.Doing, 12f * k, new Color(1f, 1f, 1f, 0.75f), 0, e);
        }

        // ---- Fujifilm (GFX100 II, X-H2S, X-H2, X-T5): the movie screen of its manuals' "Camera Displays": the movie mode (the format over
        // the frame rate) and compression at the top left, the time in the middle of the top, the white balance, film simulation (its
        // log) and view assist down the right; an exposure scale and the sound at the left; the timecode over the foot, and along it
        // the shutter speed, aperture, exposure compensation and ISO; the histogram at the bottom right

        static void Fujifilm(Shown s)
        {
            float k = s.K, W = s.Wide, size = 15f * k, L = 18f * k, R = W - 18f * k, top = s.Top + 10f * k;
            Edge e = Edge.Outline;
            var inv = CultureInfo.InvariantCulture;
            float bw = Mathf.Max(Width(s.Format, 11f * k), Width(RateSaid(s.Maker, s.Rate), 11f * k)) + 12f * k;
            var box = new Rect(L, top, bw, 30f * k);
            Box(box, new Color(0f, 0f, 0f, 0.45f));
            Frame(box, Mathf.Max(1f, k), White);
            Value(box.center.x, top + 2f * k, s.Format, 11f * k, White, What.Format, 0);
            Value(box.center.x, top + 15f * k, RateSaid(s.Maker, s.Rate), 11f * k, White, What.Rate, 0);
            Say(box.xMax + 10f * k, top + 7f * k, s.Body.Codec, size * 0.85f, White, -1, e);
            float m = s.MediaMinutes;
            Say(0.5f * W, top + 4f * k, s.Rec ? "● " + Hms(s.Seconds) : m < 0f ? "--" : Hms(m * 60.0), size, s.Rec ? RecRed : White, 0, e);
            float y = top + 2f * k, step = 26f * k;
            Value(R, y, "WB " + Kelvin(s.Kelvin), size, White, What.Balance, 1, e); y += step;
            bool log2 = Luts.recorded != null && Luts.recorded.Value == 10, log1 = Luts.recorded != null && Luts.recorded.Value == 8;
            Say(R, y, log2 ? "F-Log2" : log1 ? "F-Log" : "PROVIA", size, White, 1, e); y += step;
            Value(R, y, "VIEW ASSIST", size, s.Laid ? Amber : White, What.Look, 1, e); y += step;
            Value(R, y, Tool == 4 ? "ZEBRA " + Mathf.RoundToInt(ZebraLevel * 100f) + "%" : "ZEBRA OFF", size, Tool == 4 ? Amber : White, What.Tool, 1, e); y += step;
            Value(R, y, Peaking ? "PEAK" : "PEAK OFF", size, Peaking ? Amber : White, What.Peaking, 1, e);
            // (the exposure scale, three stops either way, up the left; the mark at the meter's reading; the sound beside it)
            float mid = 0.5f * (s.Top + s.Bottom), span = 0.36f * (s.Bottom - s.Top), thin = Mathf.Max(1f, Mathf.Round(k));
            Box(new Rect(L + 8f * k, mid - span, thin, 2f * span), White);
            for (int i = -9; i <= 9; i++)
            {
                float wide = (i % 3 == 0 ? 8f : 4f) * k;
                Box(new Rect(L + 8f * k, mid - i * span / 9f - 0.5f * thin, wide, thin), White);
                if (i % 3 == 0) Say(L - 2f * k, mid - i * span / 9f - 6f * k, Mathf.Abs(i / 3).ToString(inv), 10f * k, White, 1, e, false);
            }
            float reading = running != null ? running.scope.Reading : float.NaN;
            if (!float.IsNaN(reading)) Box(new Rect(L + 8f * k - 6f * k, mid - Mathf.Clamp(reading, -3.2f, 3.2f) * span / 3f - 1.5f * k, 12f * k, 3f * k), Amber);
            for (int c = 0; c < 2; c++)
            {
                var bar = new Rect(L + 26f * k + c * 9f * k, mid - span, 6f * k, 2f * span);
                Box(bar, new Color(0f, 0f, 0f, 0.35f));
                Frame(bar, thin, new Color(1f, 1f, 1f, 0.5f));
                Say(bar.center.x, bar.yMax + 2f * k, c == 0 ? "L" : "R", 10f * k, White, 0, e, false);
            }
            // (the foot)
            float fy = s.Bottom - 28f * k;
            Say(0.3f * W, fy - 26f * k, "TC:" + s.Clock, size, White, 0, e);
            var mode = new Rect(L, fy, Width("M", size) + 8f * k, size * 1.15f);
            Box(mode, White);
            Say(mode.x + 4f * k, fy, "M", size, Color.black);
            Say(0.22f * W, fy + 3f * k, "SS", 10f * k, White, -1, e, false);
            Value(0.22f * W + 18f * k, fy, ShutterSaid(s), size, White, What.Shutter, -1, e);
            Say(0.34f * W, fy + 3f * k, "F", 10f * k, White, -1, e, false);
            Value(0.34f * W + 10f * k, fy, s.Iris.ToString(s.Iris < 10f ? "0.0" : "0", inv), size, White, What.Iris, -1, e);
            Say(0.44f * W, fy, "±0.0", size, White, -1, e);
            Say(0.53f * W, fy + 3f * k, "ISO", 10f * k, White, -1, e, false);
            Value(0.53f * W + 24f * k, fy, Mathf.RoundToInt(s.Speed).ToString(inv), size, White, What.Speed, -1, e);
            Battery(R - 34f * k, fy + 1f * k, 34f * k, 15f * k, k);
            Histogram(new Rect(R - 130f * k, fy - 82f * k, 130f * k, 66f * k), new Color(0f, 0f, 0f, 0.3f), new Color(1f, 1f, 1f, 0.8f), White, false);
            if (s.Doing.Length > 0) Say(0.5f * W, top + 28f * k, s.Doing, 12.5f * k, new Color(1f, 1f, 1f, 0.75f), 0, e);
        }

        // ---- Nikon (Z9, Z8, Z6III): the video mode screen of its online manual's "The Monitor": a black strip over the picture (the
        // record state, the footage's length, the time left) and one under it (the sound, the shutter speed, aperture, ISO, battery);
        // inside, the shooting mode, focus and white balance at the top left, the frame size and rate, image area and file type at the
        // top right; a red frame round the picture while it records

        static void Nikon(Shown s)
        {
            float k = s.K, W = s.Wide, strip = 30f * k, size = 15f * k, inner = s.Top + strip + 8f * k;
            Edge e = Edge.Outline;
            var inv = CultureInfo.InvariantCulture;
            Box(new Rect(0f, s.Top, W, strip), new Color(0f, 0f, 0f, 0.92f));
            Box(new Rect(0f, s.Bottom - strip, W, strip), new Color(0f, 0f, 0f, 0.92f));
            float ty = s.Top + 0.5f * (strip - size * 1.2f), by = s.Bottom - strip + 0.5f * (strip - size * 1.2f);
            if (s.Rec) Frame(new Rect(0f, s.Top + strip, W, s.Bottom - s.Top - 2f * strip), Mathf.Max(2f, Mathf.Round(4f * k)), new Color(0.9f, 0.1f, 0.1f, 0.9f));
            Dot(18f * k, s.Top + 0.5f * strip, 10f * k, s.Rec ? RecRed : new Color(0.45f, 0.45f, 0.45f, 1f));
            Say(28f * k, ty, s.Rec ? "REC" : "STBY", size, s.Rec ? White : Dim);
            int whole = (int)s.Seconds;
            Say(0.5f * W, ty, (whole / 60).ToString("00", inv) + "m" + (whole % 60).ToString("00", inv) + "s", size, White, 0);
            float m = s.MediaMinutes;
            Say(W - 14f * k, ty, "[" + (m < 0f ? "--" : Mathf.FloorToInt(m / 60f) + "h" + Mathf.FloorToInt(m % 60f).ToString("00", inv) + "m") + "]", size, White, 1);
            // (inside, at the top left and right)
            float x = 14f * k;
            var mode = new Rect(x, inner, Width("M", size) + 8f * k, size * 1.15f);
            Frame(mode, Mathf.Max(1f, k), White);
            Say(mode.x + 4f * k, inner, "M", size, White, -1, e);
            x = mode.xMax + 14f * k;
            x += Say(x, inner, s.Manual ? "MF" : "AF-F", size, White, -1, e) + 14f * k;
            Value(x, inner, "WB K" + Mathf.RoundToInt(s.Kelvin), size, White, What.Balance, -1, e);
            float R = W - 14f * k, y = inner, step = 24f * k;
            float rw = Value(R, y, RateSaid(s.Maker, s.Rate), size, White, What.Rate, 1, e);
            Value(R - rw - 10f * k, y, s.Format, size, White, What.Format, 1, e); y += step;
            Say(R, y, s.Body.Codec, size, White, 1, e); y += step;
            Say(R, y, Luts.recorded != null && Luts.recorded.Value == 11 ? "N-Log" : "SD", size, White, 1, e); y += step;
            Value(R, y, s.Laid ? "VIEW ASSIST" : "VIEW ASSIST OFF", size, s.Laid ? Amber : White, What.Look, 1, e); y += step;
            Value(R, y, Tool == 4 ? "ZEBRA " + Mathf.RoundToInt(ZebraLevel * 100f) : "ZEBRA OFF", size, Tool == 4 ? Amber : White, What.Tool, 1, e); y += step;
            Value(R, y, Peaking ? "PEAKING" : "PEAKING OFF", size, Peaking ? Amber : White, What.Peaking, 1, e);
            // (the strip under the picture)
            Say(14f * k, by, "A", size, White);
            for (int i = 0; i < 12; i++) Box(new Rect(32f * k + i * 7f * k, by + 3f * k, 5f * k, size * 0.8f), new Color(1f, 1f, 1f, i >= 10 ? 0.35f : 0.22f));
            Value(0.40f * W, by, ShutterSaid(s), size, White, What.Shutter, 0);
            Value(0.52f * W, by, IrisSaid(s.Maker, s.Iris), size, White, What.Iris, 0);
            Value(0.63f * W, by, "ISO " + Mathf.RoundToInt(s.Speed), size, White, What.Speed, 0);
            Battery(R - 34f * k, by + 1f * k, 34f * k, 15f * k, k);
            if (s.Doing.Length > 0) Say(0.5f * W, inner + 2f * k, s.Doing, 12.5f * k, new Color(1f, 1f, 1f, 0.75f), 0, e);
        }

        static string Hms(double seconds)
        {
            int t = (int)Math.Max(0.0, Math.Min(seconds, 359999.0));
            return (t >= 3600 ? (t / 3600) + "h" : "") + (t / 60 % 60).ToString("00") + "m" + (t % 60).ToString("00") + "s";
        }

        /// <summary>A battery's outline and its charge (the craft's), as a camera draws it.</summary>
        static void Battery(float x, float y, float w, float h, float k)
        {
            Frame(new Rect(x, y, w, h), Mathf.Max(1f, k), White);
            Box(new Rect(x + w, y + 0.28f * h, 3f * k, 0.44f * h), White);
            if (charge >= 0f) Box(new Rect(x + 3f * k, y + 3f * k, (w - 6f * k) * Mathf.Clamp01(charge), h - 6f * k), charge < 0.15f ? RecRed : White);
        }

        // ---- the parts more than one screen has

        /// <summary>The picture's brightness spread from black (left) to white (right); with RED's goal posts (the share at the floor in purple at the left, the share clipped in red at the right, a post's full height a quarter of the picture).</summary>
        static void Histogram(Rect r, Color back, Color bars, Color edge, bool posts)
        {
            Box(r, back);
            Meter m = running != null ? running.scope : null;
            if (m != null)
            {
                float most = 1e-4f;
                foreach (float v in m.Bins) most = Mathf.Max(most, v);
                float inner = r.width - (posts ? 16f : 4f), x0 = r.x + (posts ? 8f : 2f), each = inner / m.Bins.Length;
                for (int i = 0; i < m.Bins.Length; i++)
                {
                    float h = (r.height - 4f) * Mathf.Clamp01(m.Bins[i] / most);
                    if (h >= 0.5f) Box(new Rect(x0 + i * each, r.yMax - 2f - h, Mathf.Max(1f, each), h), bars);
                }
                if (posts)
                {
                    float clip = Mathf.Max(m.ClipRed, Mathf.Max(m.ClipGreen, m.ClipBlue));
                    float lh = (r.height - 4f) * Mathf.Clamp01(m.Floor / 0.25f), rh = (r.height - 4f) * Mathf.Clamp01(clip / 0.25f);
                    Box(new Rect(r.x + 1f, r.yMax - 2f - lh, 5f, lh), new Color(0.6f, 0.2f, 0.85f, 1f));
                    Box(new Rect(r.xMax - 6f, r.yMax - 2f - rh, 5f, rh), new Color(0.95f, 0.15f, 0.12f, 1f));
                }
            }
            Frame(r, 1f, new Color(edge.r, edge.g, edge.b, 0.5f));
        }

        static Texture2D wave;
        static int waveRead = -1;

        /// <summary>
        /// A waveform monitor: across it the picture's columns, up it their brightness (black at the foot, white at the top, with
        /// lines at every quarter), each column's dots drawn brighter the more of them are at a brightness; made into a small
        /// picture at each of the meter's readings and drawn whole.
        /// </summary>
        static void Waveform(Rect r, Color back, Color trace)
        {
            Box(r, back);
            Meter m = running != null ? running.scope : null;
            var inner = new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f);
            if (m != null && m.WaveWide > 0 && m.Wave.Length == m.WaveWide * Meter.Levels)
            {
                int w = m.WaveWide, h = Meter.Levels;
                if (wave == null || wave.width != w)
                {
                    if (wave != null) Destroy(wave);
                    wave = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Cinema Camera: waveform", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    waveRead = -1;
                }
                if (waveRead != m.Reads)
                {
                    waveRead = m.Reads;
                    var dots = new Color32[w * h];
                    float full = Mathf.Max(1f, m.WaveHigh * 0.25f);
                    for (int i = 0; i < dots.Length; i++)
                    {
                        int n = m.Wave[i];
                        dots[i] = new Color32(255, 255, 255, (byte)(n == 0 ? 0 : Mathf.Clamp(60 + Mathf.RoundToInt(195f * Mathf.Sqrt(n / full)), 0, 255)));
                    }
                    wave.SetPixels32(dots);
                    wave.Apply(false);
                }
                Color was = GUI.color;
                GUI.color = trace;
                GUI.DrawTexture(inner, wave);
                GUI.color = was;
            }
            for (int q = 0; q <= 4; q++) Box(new Rect(inner.x, inner.yMax - inner.height * q / 4f, inner.width, 1f), new Color(1f, 1f, 1f, q == 0 || q == 4 ? 0.35f : 0.18f));
            Frame(r, 1f, new Color(1f, 1f, 1f, 0.45f));
        }

        /// <summary>Two channels' sound meters, empty: the camera hears nothing of the game.</summary>
        static void Meters(float x, float y, float w, float h, float k)
        {
            float each = (h - 2f * k) / 2f;
            for (int c = 0; c < 2; c++)
            {
                float cy = y + c * (each + 2f * k);
                Say(x, cy - 1f * k, (c + 1).ToString(CultureInfo.InvariantCulture), each * 0.95f, White, -1, Edge.Shadow);
                for (int i = 0; i < 16; i++)
                    Box(new Rect(x + 12f * k + i * (w - 12f * k) / 16f, cy + 1f * k, (w - 12f * k) / 16f - 2f * k, each - 2f * k), new Color(1f, 1f, 1f, i >= 13 ? 0.35f : 0.22f));
            }
        }

        // ---- the lists that open from a clicked value

        static readonly List<string> optionNames = new List<string>();
        static readonly List<float> optionValues = new List<float>();

        static void Add(string name, float value) { optionNames.Add(name); optionValues.Add(value); }

        /// <summary>The steps the chosen camera offers for a value, and which of them it is at now (-1: none of them).</summary>
        static int Options(What what, Shown s)
        {
            optionNames.Clear(); optionValues.Clear();
            Body b = s.Body;
            Maker m = b.Maker;
            int current = -1;
            switch (what)
            {
                case What.Rate:
                    for (int i = 0; i < Rates.Length; i++) if (Rates[i] <= b.Fastest + 0.01f) Add(RateSaid(m, Rates[i]), i);
                    current = optionValues.IndexOf(rate != null ? rate.Value : 1);
                    break;
                case What.Shutter:
                    if (BySpeed(m))
                    {
                        foreach (float v in FxSpeeds) Add(SpeedSaid(m, v), v);
                        current = optionValues.IndexOf(Mathf.Round(shutter != null ? shutter.Value : 48f));
                    }
                    else
                    {
                        string now = ShutterSaid(s);
                        foreach (float a in AnglesOf(m))
                        {
                            float x = 360f * s.Rate / a;
                            if (x < 23.5f || x > 2000.5f) continue;
                            Add(a.ToString(m == Maker.Canon ? "0.00" : m == Maker.Red || m == Maker.Blackmagic ? "0.#" : "0.0", CultureInfo.InvariantCulture) + (m == Maker.Panasonic ? "deg" : "°"), a);
                            if (optionNames[optionNames.Count - 1] == now) current = optionNames.Count - 1;
                        }
                    }
                    break;
                case What.Speed:
                    foreach (float v in b.Speeds) Add((m == Maker.Canon || m == Maker.SonyFx || m == Maker.Fujifilm || m == Maker.Nikon ? "ISO " : m == Maker.Arri || m == Maker.Venice ? "EI " : "") + v, v);
                    current = optionValues.IndexOf(Mathf.Round(s.Speed));
                    break;
                case What.Nd:
                    if (b.Nd == null) break;
                    foreach (float v in b.Nd) Add(NdSaid(m, v), v);
                    current = optionValues.IndexOf(Mathf.Round(s.Nd * 10f) / 10f);
                    break;
                case What.Iris:
                    foreach (float v in Stops) Add(IrisSaid(m, v), v);
                    current = optionValues.IndexOf(Mathf.Round(s.Iris * 10f) / 10f);
                    break;
                case What.Balance:
                    foreach (float v in Kelvins) Add(Kelvin(v), v);
                    current = optionValues.IndexOf(Mathf.Round(s.Kelvin));
                    break;
                case What.Tint:
                    foreach (float v in Tints) Add(TintSaid(m, v), v);
                    current = optionValues.IndexOf(Mathf.Round(s.Tint * 10f) / 10f);
                    break;
                case What.Format:
                    for (int i = 0; i < b.Formats.Length; i++) Add(b.Formats[i].Name, i);
                    current = optionNames.IndexOf(s.Format);
                    break;
                case What.Tool:
                    Add(m == Maker.Red ? "Tools off" : "Off", 0);
                    if (FalseColour(b) != 4) Add(m == Maker.Red ? "Exposure (false colour)" : m == Maker.Arri ? "False Color" : "False color", 1);
                    Add(m == Maker.Canon || m == Maker.Panasonic ? "Zebra 1" : "Zebra", 2);
                    current = optionValues.IndexOf(tool != null ? tool.Value : 0);
                    break;
                case What.Look:
                    Add(m == Maker.Venice || m == Maker.SonyFx ? "s709" : m == Maker.Fujifilm || m == Maker.Nikon ? "View assist on" : "LUT on", 1);
                    Add(m == Maker.Venice || m == Maker.SonyFx ? "S-Log3 (LUT off)" : m == Maker.Fujifilm || m == Maker.Nikon ? "View assist off" : "LUT off", 0);
                    current = s.Laid ? 0 : 1;
                    break;
            }
            return current;
        }

        /// <summary>A step picked from a list: set into Cinema Camera's own settings (the shutter from the angle at the frame rate, the film speed, the grey filter, the aperture), or the camera's.</summary>
        static void Pick(What what, int index)
        {
            if (index < 0 || index >= optionValues.Count) return;
            float v = optionValues[index];
            Body b = Chosen;
            if (b == null) return;
            switch (what)
            {
                case What.Rate:
                    if (rate != null)
                    {
                        // (the shutter kept at its angle: a change of frame rate on a camera leaves the angle as it was)
                        float angle = 360f * Rates[Mathf.Clamp(rate.Value, 0, Rates.Length - 1)] / Mathf.Max(shutter != null ? shutter.Value : 48f, 1f);
                        rate.Value = (int)v;
                        if (shutter != null && !BySpeed(b.Maker)) shutter.Value = 360f * Rates[(int)v] / Mathf.Max(angle, 1f);
                    }
                    break;
                case What.Shutter:
                    if (shutter != null) shutter.Value = BySpeed(b.Maker) ? v : 360f * Rates[Mathf.Clamp(rate != null ? rate.Value : 1, 0, Rates.Length - 1)] / v;
                    break;
                case What.Speed: if (speed != null) speed.Value = v; break;
                case What.Nd: if (filter != null) filter.Value = v; break;
                case What.Iris: if (stop != null) stop.Value = v; break;
                case What.Balance: if (balance != null) balance.Value = v; break;
                case What.Tint: if (tint != null) tint.Value = v; break;
                case What.Format: if (mod != null) mod.Keep(KeyFor(b), b.Formats[(int)v].Name); break;
                case What.Tool: if (tool != null) tool.Value = (int)v; break;
                case What.Look:
                    if (v > 0.5f)
                    {
                        if (Luts.lutChoice != null && Luts.lutChoice.Value <= 0)
                        {
                            int at = Array.IndexOf(Luts.lutChoice.Options, Luts.CurveName(Luts.recorded != null ? Luts.recorded.Value : b.Curve) + " to Rec.709");
                            Luts.lutChoice.Value = at > 0 ? at : Array.IndexOf(Luts.lutChoice.Options, Luts.CurveName(b.Curve) + " to Rec.709");
                        }
                        if (Luts.strength != null) Luts.strength.Value = 1f;
                    }
                    else if (Luts.strength != null) Luts.strength.Value = 0f;
                    break;
            }
        }

        /// <summary>The open list, by the value it opened from: its maker's panel, the step it is at lit.</summary>
        static void Picker(Shown s)
        {
            rows.Clear();
            if (open == What.None) return;
            int current = Options(open, s);
            if (optionNames.Count == 0) { open = What.None; return; }
            float k = s.K, size = 14f * k, row = 22f * k, pad = 8f * k;
            float wide = 0f;
            foreach (string o in optionNames) wide = Mathf.Max(wide, Width(o, size));
            wide += 2f * pad;
            int perColumn = Mathf.Max(1, Mathf.FloorToInt(Screen.height * 0.55f / row));
            int columns = (optionNames.Count + perColumn - 1) / perColumn, tall = Mathf.Min(optionNames.Count, perColumn);
            float bw = wide * columns, bh = row * tall + pad;
            float x = Mathf.Clamp(openFrom.center.x - 0.5f * bw, 4f, Screen.width - bw - 4f);
            float y = openFrom.center.y > 0.5f * Screen.height ? openFrom.yMin - bh - 6f * k : openFrom.yMax + 6f * k;
            y = Mathf.Clamp(y, 4f, Screen.height - bh - 4f);
            pickerBox = new Rect(x, y, bw, bh);
            Color panel, lit, litText;
            switch (s.Maker)
            {
                case Maker.Venice: panel = new Color(0.08f, 0.08f, 0.08f, 0.95f); lit = Orange; litText = Color.black; break;
                case Maker.Red: panel = new Color(0.05f, 0.05f, 0.05f, 0.95f); lit = new Color(0.8f, 0.1f, 0.1f, 1f); litText = White; break;
                case Maker.Blackmagic: panel = new Color(0.12f, 0.12f, 0.12f, 0.92f); lit = new Color(1f, 0.55f, 0.1f, 1f); litText = Color.black; break;
                case Maker.Canon: panel = new Color(0.1f, 0.1f, 0.1f, 0.92f); lit = new Color(0.85f, 0.15f, 0.15f, 1f); litText = White; break;
                case Maker.SonyFx: panel = new Color(0.1f, 0.1f, 0.12f, 0.92f); lit = new Color(0.95f, 0.55f, 0.1f, 1f); litText = Color.black; break;
                case Maker.Panasonic: panel = new Color(0.08f, 0.1f, 0.14f, 0.92f); lit = new Color(0.3f, 0.55f, 0.95f, 1f); litText = White; break;
                case Maker.Fujifilm: panel = new Color(0.06f, 0.06f, 0.06f, 0.92f); lit = new Color(0.25f, 0.55f, 0.95f, 1f); litText = White; break;
                case Maker.Nikon: panel = new Color(0.05f, 0.05f, 0.05f, 0.94f); lit = new Color(1f, 0.85f, 0.1f, 1f); litText = Color.black; break;
                default: panel = new Color(0.12f, 0.12f, 0.12f, 0.94f); lit = Amber; litText = Color.black; break;
            }
            Box(pickerBox, panel);
            Frame(pickerBox, Mathf.Max(1f, k), new Color(1f, 1f, 1f, 0.25f));
            for (int i = 0; i < optionNames.Count; i++)
            {
                int c = i / perColumn, r = i % perColumn;
                var cell = new Rect(x + c * wide, y + 0.5f * pad + r * row, wide, row);
                if (i == current) Box(new Rect(cell.x + 2f, cell.y + 1f, cell.width - 4f, cell.height - 2f), lit);
                Say(cell.x + pad, cell.y + 0.5f * (row - size * 1.2f), optionNames[i], size, i == current ? litText : White);
                rows.Add(cell);
            }
        }

        /// <summary>The clicks: on a value its list opens; on a step in the list it is set; anywhere else the list closes.</summary>
        void OnGUI()
        {
            GUI.depth = 20;
            Event ev = Event.current;
            if (testing && ev.type == EventType.Repaint) Draw(0f, Screen.height, focal != null ? focal.Value : 50f, hand != null ? hand.Value : 10f, true, "");
            if (ev.type != EventType.MouseDown || ev.button != 0) return;
            if (Time.frameCount - drawnAt > 3) { open = What.None; return; }
            Vector2 at = ev.mousePosition;
            if (open != What.None)
            {
                What was = open;
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i].Contains(at)) { Pick(was, i); open = What.None; ev.Use(); return; }
                open = What.None;
                foreach (Hot h in hots)
                    if (h.What != was && h.What != What.None && h.Where.Contains(at))
                    {
                        if (h.What == What.Peaking) { if (peaking != null) peaking.Value = !peaking.Value; }
                        else { open = h.What; openFrom = h.Where; }
                        break;
                    }
                ev.Use();
                return;
            }
            foreach (Hot h in hots)
                if (h.What != What.None && h.Where.Contains(at))
                {
                    if (h.What == What.Peaking) { if (peaking != null) peaking.Value = !peaking.Value; }
                    else { open = h.What; openFrom = h.Where; }
                    ev.Use();
                    return;
                }
        }

        static bool testing;

#if DEV
        /// <summary>
        /// For the development build: "on" draws the chosen camera's screen by itself, with the settings' values (to look at a screen
        /// without the viewfinder); "off"; a number picks that camera (0 Cinema Camera's own, 1 the first); "open NAME" opens a
        /// value's list (Rate, Shutter, Speed, Nd, Iris, Balance, Tint, Format, Tool, Look); "pick N" picks the open list's Nth step.
        /// </summary>
        public static string Test(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a == "on" || a == "off") { testing = a == "on"; if (!testing) open = What.None; return "drawn by itself: " + testing; }
            if (a.StartsWith("open ")) { if (Enum.TryParse(a.Substring(5).Trim(), true, out What w)) { open = w; foreach (Hot h in hots) if (h.What == w) openFrom = h.Where; return "open: " + w; } return "no such value"; }
            if (a.StartsWith("pick ") && int.TryParse(a.Substring(5).Trim(), out int n)) { What was = open; if (was == What.None) return "no list open"; Pick(was, n); open = What.None; return "picked " + n + " of " + was; }
            if (int.TryParse(a, out int which) && body != null) { body.Value = which; return "camera: " + (Chosen != null ? Chosen.Name : "Cinema Camera's own"); }
            return Says();
        }

        public static string Says()
        {
            Body b = Chosen;
            if (b == null) return "Cinema Camera's own (" + All.Length + " cameras known)";
            var inv = CultureInfo.InvariantCulture;
            return b.Name + ", " + FormatOf(b).Name + " (" + FormatOf(b).Wide.ToString("0.00", inv) + " mm wide, lens sees " + High().ToString("0.00", inv) + " mm high); "
                   + "fps " + RateNames[rate.Value] + ", shutter 1/" + shutter.Value.ToString("0", inv) + ", EI " + speed.Value.ToString("0", inv) + ", ND " + filter.Value.ToString("0.0", inv)
                   + " stops, iris " + stop.Value.ToString("0.0", inv) + ", WB " + balance.Value.ToString("0", inv) + "K tint " + tint.Value.ToString("0.0", inv)
                   + "; gains " + gains.ToString("F3") + "; tool " + Tool + "; recorded " + Curve + ", LUT " + LookName(null) + " at " + (Luts.strength != null ? Luts.strength.Value : 0f).ToString("0.00", inv)
                   + "; spread: floor " + (running != null ? running.scope.Floor.ToString("0.000", inv) : "-") + ", reading " + (running != null ? running.scope.Reading.ToString("0.00", inv) : "-")
                   + "; " + hots.Count + " clickable, list " + open + "; drawn " + (Time.frameCount - drawnAt) + " frames ago";
        }
#endif
    }

    /// <summary>
    /// The look's and the cameras' settings put on Cinema Camera's page at the main menu, so that they are there before the first
    /// flight (at the space centre too); Cinema Camera puts its own there at the game's start (Cine.cs).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class BodiesAtStart : MonoBehaviour
    {
        void Awake()
        {
            Bodies.Setup();
            Destroy(gameObject);
        }
    }
}
