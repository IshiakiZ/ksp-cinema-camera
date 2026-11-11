using System;
using System.Collections.Generic;
using Keystone;
using UnityEngine;
using UnityEngine.Rendering;

#if !DEV
[assembly: KSPAssembly("CineCamera", 0, 6)]
[assembly: KSPAssemblyDependency("Keystone", 0, 11)]
#endif

namespace CineCamera
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class CineAtStart : MonoBehaviour
    {
        void Awake() { Cine.Setup(); Destroy(gameObject); }
    }

    /// <summary>
    /// The flight camera worked like a real one: a lens of a given focal length in front of a sensor of
    /// a given size, an aperture, a focus, a shutter and a film speed, each doing to the picture what it
    /// does on a camera.
    ///
    ///   focal length and sensor   how much the camera sees (its angle of view), as on a real lens
    ///   aperture and focus        depth of field: what is nearer or further than the focus is blurred,
    ///                             the more so the wider the aperture and the longer the lens
    ///   shutter                   motion blur: how far things smear while the shutter is open
    ///   film speed (ISO)          grain
    ///   the lens's character      darker corners, a little colour fringing, and the bend of a wide lens
    ///
    /// The blurring, the grain and the rest are done by the lens that Keystone, the base this mod stands
    /// on, has of its own (see Keystone.Lens): this mod works out, from the camera's settings, what to ask
    /// of it, every frame. No other mod is needed. (Where Keystone's lens cannot work, a copy of Keystone without
    /// its shader for the system the game runs on, the same is asked of TUFX if that is installed; with neither, the
    /// camera still has its lens's angle of view and its bars, and says in its page what is missing.)
    ///
    /// It can be moved as a camera crew moves one: left on the ground to follow the ship as it goes, left
    /// drifting in space as the ship burns away from it, taken round the ship, or flown along a path of a few
    /// points set beforehand (see CinePath). Its viewfinder has a light meter (see Meter). In the cockpit, where
    /// the game draws the inside with a camera of its own after the view through the windows, the lens is put in
    /// front of that camera instead, so that the inside and the outside go through it together.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Cine : MonoBehaviour
    {
        public const string Version = "0.8.0";

        static readonly string[] Sensors = { "full frame", "super 35", "IMAX 70 mm" };
        static readonly float[] SensorHigh = { 24f, 18.66f, 52.63f };                 // millimetres
        static readonly string[] Bars = { "none", "1.85 : 1", "2.39 : 1" };
        static readonly float[] BarShape = { 0f, 1.85f, 2.39f };
        // (the moves, as the page lists them; kept by name in the settings file, so the list can grow)
        const int Still = 0, Stay = 1, Circle = 2, Drift = 3, Fly = 4, Track = 5, Chase = 6, Pair = 7, Mount = 8;
        static readonly string[] Moves = { "none", "stay here and follow", "circle the ship", "drift and follow", "fly the path", "track from afar", "chase", "the ship and its target", "on the ship" };

        static Mod mod;
        static Toggle on;
        static Slider focal, stop, hand, pull, shutter, speed, character, filter, round, span, cutTime, trackFar, shaky, rack, flares;
        static Choice sensor, focus, bokeh, bars, finder, exposure, move, meter, cut;
        static Toggle keep;
        // (the page's fine details, in its Advanced view: each as installed does what the mod did before it had them)
        static Slider grainBy, cornersBy, fringeBy, bendBy, roarBy, chaseTakes, pairTakes;
        static float Of(Slider slider, float installed) => slider != null ? (float)slider : installed;

        /// <summary>
        /// How grainy the picture is in the middle tones at a film speed, for Keystone's lens, and how much grainier its shadows
        /// are (dark: the sensor's own noise against the light's own, in the middle tones) (2026-11-08). A cinema camera's
        /// picture is clean at its own base ISO: its sensor's noise, a pixel fine, about one level of 255 in the middle tones of
        /// a picture 1080 pixels high (the noise of the light itself on a sensor some six thousand pixels across, seen at that
        /// size), the sensor's own a third as much there and more the darker, none in white (the lens's shader spreads it so;
        /// the camera's curve to Rec.709 after it lays it a little flatter in the shadows, which is why a third). Each stop faster
        /// takes half as much light for the same picture: the light's own noise against it grows by the square root of that,
        /// the sensor's own by all of it. The base is the chosen camera's own, 800 where none is (most cinema cameras'); from a
        /// stop below its second base, where it has one, the camera is taken to be set at that (as it is set, or sets itself,
        /// for low light), about half a stop noisier there than at its first: an FX9 at 4000 nearly as clean as at 800, at 3200
        /// on its first base much less so. Before, it went from none at ISO 100 to the most at 6400, half the most at 800
        /// whatever the camera: 6.5 levels in a dull sky at an FX9's own 800, some nine in the middle tones, in blocks a pixel
        /// and a half across at 1080 high (the owner: "The grain on the cinema cameras is not realistic. It's not how actual
        /// ones look. It's way too grainy."); then, the same day, 2^0.6 more each stop, and the grain spread over the tones as
        /// film's.
        /// </summary>
        static float Grain(float iso, out float dark)
        {
            iso = Mathf.Max(iso, 1f);
            float first = Bodies.BaseIso(), second = Bodies.SecondIso();
            // (how much less light the sensor takes than at its base, for the same picture)
            float less = iso / first;
            if (second > 0f && iso >= 0.5f * second) less = Mathf.Min(less, 1.5f * iso / second);
            float light = Mathf.Sqrt(less);
            dark = 0.3f * light;
            return 0.05f * light;
        }

        /// <summary>The settings added after Fine(true) are fine details: on the page only in Keystone's Advanced view, until Fine(false).
        /// (Keystone 0.11.0 has the two views; an older one has neither call, and shows the fine details with the rest.)</summary>
        static void Fine(bool fine) { try { if (fine) Advanced(mod); else Simple(mod); } catch (Exception) { } }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] static void Advanced(Mod page) => page.Advanced();
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] static void Simple(Mod page) => page.Simple();

        public static void Setup()
        {
            if (mod != null && Kit.Find("Cinema Camera") == mod) return;
            mod = Kit.Register("Cinema Camera", Version, "The flight camera worked like a real one: lens, sensor, aperture, focus, shutter and film speed, each doing to the picture what it does on a camera.");
            try { Keys(mod); } catch (Exception) { }             // (for Keystone's page of every key; not with a Keystone older than 0.9.0)
            on = mod.Toggle("on", "Cinema camera", false, "On: the flight camera takes its angle of view from the lens below, and the picture gets depth of field, motion blur and grain from the settings. Off: the game's camera as it was.");
            mod.Heading("Lens");
            focal = mod.Slider("focal", "Focal length", 50f, 12f, 300f, " mm", 0, "A short lens sees wide and keeps nearly everything sharp; a long one sees narrow and throws the background out of focus. The game's own zoom keys turn this ring too.");
            sensor = mod.Choice("sensor", "Sensor", 0, Sensors, "The size of the film or sensor behind the lens. The same lens sees wider on a bigger one, and blurs the background more.");
            stop = mod.Slider("stop", "Aperture  f /", 2.8f, 1f, 22f, "", 1, "The f-number. Small numbers are a wide-open lens: a thin slice in focus and a soft background. Large numbers keep more sharp.");
            mod.Heading("Focus");
            focus = mod.Choice("focus", "Focus on", 0, new[] { "the ship", "the middle", "by hand" }, "The ship being flown, whatever is in the middle of the picture, or a distance you set.");
            hand = mod.Slider("hand", "Distance", 10f, 0.5f, 300f, " m", 1, "Where the focus is when it is set by hand.");
            pull = mod.Slider("pull", "Focus speed", 4f, 0.5f, 20f, "", 1, "How quickly the focus follows when what it is on moves nearer or further.");
            rack = mod.Slider("rack", "A pull of focus takes", 1.2f, 0f, 4f, " s", 1, "When what the focus is on is changed (F with the modifier key, or here), the focus is pulled from the one to the other over so long, as a focus puller turns the ring: slowly at first, quickly through the middle, settling at the end. 0: at the focus speed above.");
            bokeh = mod.Choice("bokeh", "Blur quality", 1, new[] { "small", "medium", "large" }, "How large the out-of-focus blur may get. Larger costs more of the graphics card's time.");
            mod.Heading("Shutter and film");
            shutter = mod.Slider("shutter", "Shutter  1 /", 120f, 24f, 2000f, " s", 0, "How long the shutter is open. Long times (small numbers) smear what moves; short ones freeze it. It cannot be open for longer than a frame of the game lasts.");
            speed = mod.Slider("iso", "Film speed  ISO", 100f, 100f, 6400f, "", 0, "A faster film speed takes less light and is grainier. A camera is cleanest at its own base ISO, which picking the camera sets (800 where none is picked).");
            Fine(true);
            grainBy = mod.Slider("grain", "Grain at each film speed", 1f, 0f, 3f, "x", 2, "How grainy each film speed is. At 1, as a real cinema camera: at its own base ISO the grain is a pixel fine and hardly seen, and each stop faster it is about half as much again. 2 is twice the grain, 0 none at all.");
            Fine(false);
            exposure = mod.Choice("exposure", "Exposure", 0, new[] { "left alone", "by the camera" }, "By the camera: how bright the picture comes out goes by the aperture, the shutter and the film speed, as it does in a real one. Full sun wants f/16 at 1/100 s on ISO 100 film (or anything that lets in as much); open the lens or slow the shutter in the dark. It needs the TUFX mod to do it. Left alone: the picture is as bright as the game, or another mod's eye, makes it.");
            filter = mod.Slider("filter", "Grey filter", 0f, 0f, 12f, " stops", 1, "A grey filter in front of the lens, to hold the light back by so many stops: for a wide-open lens or a slow shutter in daylight.");
            character = mod.Slider("character", "Lens character", 0.3f, 0f, 1f, "", 2, "How much the lens shows of itself: darker corners (most with the aperture wide open), a little colour fringing, and the bend of a wide lens. None at 0.");
            Fine(true);
            cornersBy = mod.Slider("corners", "Darker corners", 1f, 0f, 3f, "x", 2, "How much darker the lens's character makes the picture's corners, the most with the aperture wide open. 0: none.");
            fringeBy = mod.Slider("fringe", "Colour fringing", 1f, 0f, 3f, "x", 2, "How much the lens's character fringes edges towards the picture's sides in red and blue. 0: none.");
            bendBy = mod.Slider("bend", "A wide lens's bend", 1f, 0f, 3f, "x", 2, "How much the lens's character bows straight lines on a wide lens (shorter than 35 mm). 0: none.");
            Fine(false);
            flares = mod.Slider("flares", "Lamps' flare", 0.5f, 0f, 1f, "", 2, "How much a lamp shining into the lens flares, as real lenses do: a soft glare, rays from the aperture's blades (longer the more it is stopped down, barely any wide open), and faint ghosts of the aperture across the picture (by the lens's character). Only while the lamp is in sight. The sun's flare is the game's own (or Scatterer's). None at 0.");
            bars = mod.Choice("bars", "Bars", 0, Bars, "Black bars for a cinema-shaped picture. They are drawn only while the game's interface is hidden (F2), or the viewfinder is up.");
            mod.Heading("Moves");
            move = mod.Choice("move", "Camera move", 0, Moves, "Stay here and follow: the camera is left where it is, fixed to the ground (or to the spot over it), and turns to follow the ship as it flies off, as a camera on a tripod by the launch pad does. Circle the ship: the camera goes slowly round it. Drift and follow: for space. The camera is left where it is, going on at the speed the ship had, as a camera on a second spacecraft would: a burn takes the ship away from it, and it turns to follow. Fly the path: the camera flies through the points of the path set below. The ship and its target: the camera keeps behind the ship and to the side, looking past it at what it is making for, both in the picture (the target set in the game, a craft or a docking port or a world; else the nearest craft, else the launch pad). On the ship: the camera is fixed to the part nearest it, where it is and looking the way it looks, as a camera bolted to the hull is (put the game's camera there first). With the game's modifier key, T leaves the camera where it is (fixed to the ground near a world, drifting in space) and takes it back, and G flies the path.");
            trackFar = mod.Slider("trackfar", "Tracker's distance", 4f, 0.5f, 15f, " km", 1, "Track from afar: the camera is put down on the ground so far from the ship, to the side of its way with the sun behind the camera, as the long-lens trackers that film launches stand kilometres from the pad, and follows it out with its lens drawn out to keep its size.");
            Fine(true);
            chaseTakes = mod.Slider("chaselag", "The chase plane catches up in", 2f, 0.5f, 8f, " s", 1, "The chase: how long the camera takes to come back to its place behind the ship when the ship speeds up, slows or turns. Shorter is a tighter chase.");
            pairTakes = mod.Slider("pairlag", "Following the ship and its target", 3f, 0.5f, 10f, " s", 1, "The ship and its target: how long the camera takes to come round to its place as the two of them move.");
            Fine(false);
            shaky = mod.Slider("shaky", "Hand-held", 0f, 0f, 1f, "", 2, "While a move holds the camera (staying, tracking, the chase, a path): 0 as on a tripod, 1 as held in the hands, the picture wandering a little and swaying with the breath, the more through a long lens. A camera near a launch shakes with its roar whatever this is, once the sound gets to it.");
            Fine(true);
            roarBy = mod.Slider("roar", "Shaken by the engines' roar", 1f, 0f, 3f, "x", 2, "How much the camera shakes with the roar of running engines near it, on a tripod as in the hands. 0: not at all.");
            Fine(false);
            keep = mod.Toggle("keep", "Zoom to keep the ship's size", true, "While the camera stays behind, the lens is drawn out as the ship gets further off, so that it stays the size it was in the picture: as far as a 2400 mm lens, which is what the cameras that follow real launches have.");
            round = mod.Slider("round", "Circling", 6f, -30f, 30f, " ° a second", 1, "How fast the camera goes round the ship. Less than nought: the other way.");
            span = mod.Slider("span", "Path takes", 10f, 2f, 120f, " s", 0, "How long the camera takes to fly the path, from its first point to its last.");
            cut = mod.Choice("cut", "Between moves", 1, new[] { "a cut", "a dissolve", "through black" }, "How one move gives way to the next, and to the game's own camera: straight from one picture to the next, as a cut; the last picture of the one fading out over the next, as a dissolve; or down to black and up again.");
            cutTime = mod.Slider("cuttime", "Its length", 0.8f, 0.2f, 3f, " s", 1, "How long a dissolve, or the way through black, takes.");
            mod.Heading("Viewfinder");
            finder = mod.Choice("finder", "Viewfinder", 0, new[] { "off", "on", "with thirds" }, "The game's own interface is put away and a camera's is drawn in its place: frame marks, the focus bracket, the lens, aperture, shutter and film speed, a light meter, a timecode and the ship's battery. This window still opens with the modifier key and K; F2 brings the game's interface back.");
            meter = mod.Choice("meter", "Meter", 1, new[] { "off", "centre-weighted", "spot" }, "The viewfinder's light meter: how far over (+) or under (-) a middle grey the picture comes out, in stops, as a camera's shows it. Centre-weighted: the whole picture, its middle counting most. Spot: only what is inside the focus bracket.");
        }

        ILens lens;                                               // what does the blurring and the rest, while the camera is running (nothing, where nothing here can)
        bool running, hidden;
        bool viewing, ours;                                       // the viewfinder is up (and the game's interface put away for it); and it is this that is putting it away or bringing it back just now
        float hideAgainAt;                                        // (the interface put away again no oftener than once a second)
        bool inside;                                              // the game is showing the cockpit from inside, where it holds the flight camera to a kerbal's eye itself
        float angleSet = -1f, angleBefore, leastBefore;
        float focusAt = 10f, focusWanted = 10f, frames = 60f;
        int focusWas = -1; float rackFrom = 10f, rackAge = 99f;          // (since 0.7.0: a pull of focus under way: from where, how far through)
        float stops = float.NaN;                                  // how far over (or under) what full sun wants the camera's own exposure is, when it is set by the camera
        float lensNow = 50f;                                      // the focal length the picture is taken through this frame: the ring, drawn out by a move, flown by a path, or the cockpit's own zoom
        // (a move that holds the camera: which; where it was left (on the ground: on which world, where on it; in space: the orbit it drifts
        // on); how far the ship was then and is now; which way the camera is turned; how far the lens is drawn out)
        int holding;
        CelestialBody stayOn;
        double stayLat, stayLon, stayHigh;
        Orbit drift;
        Vector3 driftUp = Vector3.up;
        float stayFar = 20f, drawn = 1f, far;
        Quaternion stayLook;
        // (the path kept for the craft being flown; how long it has been flown; its lens just now)
        readonly CinePath path = new CinePath();
        string pathFor, pathShipName;
        Vessel pathShip;
        float pathTime, pathLens = 50f;
        string said = "", told = "";                              // a few words in the viewfinder for a moment; what the page last said about the path
        float saidUntil;
        // (the light meter, and its reading as shown: stops over or under middle grey)
        readonly Meter gauge = new Meter();
        float metered = float.NaN;
        bool locked;                                              // the game's own keys held off while the modifier key is down
        float charge = -1f, chargeAt;                             // how full the ship's batteries are (0 to 1; less than nought: it has none), and when that was last looked up
        string readout = "";
        static Texture2D black;

        void Awake()
        {
            Setup();
            // (a move is something begun, not something kept: a flight never starts with the camera left behind somewhere)
            if (move != null) move.Value = Still;
            mod.Panel = Panel;
            GameEvents.onHideUI.Add(Hidden);
            GameEvents.onShowUI.Add(Shown);
            Camera.onPreCull += BeforeDrawing;
        }

        void OnDestroy()
        {
            Camera.onPreCull -= BeforeDrawing;
            GameEvents.onHideUI.Remove(Hidden);
            GameEvents.onShowUI.Remove(Shown);
            Stop();
            Lock(false);
            gauge.Off();
            Unshoot();
            if (shooting != null) { shooting.Release(); shooting = null; }
            if (shot != null) { shot.Release(); Destroy(shot); shot = null; }
            if (mod != null && mod.Panel == Panel) mod.Panel = null;
        }

        void Hidden() => hidden = true;

        void Shown()
        {
            hidden = false;
            // (the player has brought the game's interface back, with F2: the viewfinder goes)
            if (viewing && !ours) { viewing = false; finder.Value = 0; }
        }

        /// <summary>Put the viewfinder up, or take it down: the game's own interface goes away for it and comes back after.</summary>
        void View(bool up)
        {
            if (up == viewing) return;
            viewing = up;
            ours = true;
            try { if (up) GameEvents.onHideUI.Fire(); else GameEvents.onShowUI.Fire(); }
            finally { ours = false; }
        }

        float High => Bodies.High() > 0f ? Bodies.High() : SensorHigh[sensor.Value];

        /// <summary>How long this frame stands for: as the player sees it (paused or not), or a recording's own frame when the game is being filmed frame by frame.</summary>
        static float Step => Time.captureFramerate > 0 ? 1f / Time.captureFramerate : Mathf.Min(Time.unscaledDeltaTime, 0.1f);

        static float Angle(float lens, float high) => 2f * Mathf.Atan(0.5f * high / Mathf.Max(lens, 0.1f)) * Mathf.Rad2Deg;

        static float Focal(float angle, float high) => 0.5f * high / Mathf.Tan(0.5f * Mathf.Clamp(angle, 1f, 170f) * Mathf.Deg2Rad);

        /// <summary>Whether the game is showing the inside of a cockpit (or any other view from inside a part), where it holds the flight camera to a kerbal's eye itself.</summary>
        static bool Inside()
        {
            CameraManager cameras = CameraManager.Instance;
            return cameras != null && (cameras.currentCameraMode == CameraManager.CameraMode.IVA || cameras.currentCameraMode == CameraManager.CameraMode.Internal);
        }

        /// <summary>The camera that draws the cockpit's inside, over the view through its windows: the last of the scene's cameras to draw, in the cockpit.</summary>
        static Camera Cockpit()
        {
            InternalCamera inner = InternalCamera.Instance;
            Camera cam = inner != null ? inner.GetComponent<Camera>() : null;
            return cam != null && cam.enabled ? cam : null;
        }

        /// <summary>Whether a camera left behind by this ship should drift with it in space rather than stay fixed over the ground.</summary>
        static bool InSpace(Vessel ship)
        {
            if (ship == null || ship.mainBody == null || ship.LandedOrSplashed || ship.situation == Vessel.Situations.PRELAUNCH) return false;
            if (ship.situation == Vessel.Situations.ORBITING || ship.situation == Vessel.Situations.ESCAPING) return true;
            CelestialBody body = ship.mainBody;
            bool aboveAir = !body.atmosphere || ship.altitude > body.atmosphereDepth;
            return aboveAir && (ship.altitude > 15000.0 || ship.srfSpeed > 250.0);
        }

        /// <summary>
        /// The camera's keys, all with the game's modifier key held (Option on a Mac, Alt elsewhere): C the camera on and
        /// off; V the viewfinder; F what the focus is on (the ship, the middle, by hand); T leaves the camera where it is and
        /// takes it back; G flies the path (Y until 2026-11-06: Natural Weather's weather centre has Y); Page Up and Page Down pull the focus further and nearer by hand for as long as
        /// they are held.
        /// </summary>
        /// <summary>The camera's keys (with the game's modifier key held), as Keystone lists them.</summary>
        static void Keys(Mod mod)
        {
            mod.Uses(KeyCode.C, true, "the camera on and off");
            mod.Uses(KeyCode.V, true, "the viewfinder");
            mod.Uses(KeyCode.F, true, "what the focus is on");
            mod.Uses(KeyCode.T, true, "the camera stays where it is");
            mod.Uses(KeyCode.G, true, "the camera flies its path");
            mod.Uses(KeyCode.PageUp, true, "the focus further");
            mod.Uses(KeyCode.PageDown, true, "the focus nearer");
        }

        void Update()
        {
            gauge.Read();
            // The game reads its own keys whether or not the modifier key is held: with the camera's keys it would also switch
            // SAS on or off (T), hold it (F), change its own camera's mode (V), put the landing gear up or down (G) and show or hide
            // the crew's portraits (C). While the camera is running and the modifier is held, those of the game's own are held off,
            // so that the camera's keys do only what they say. (The modifier is always down a moment before the letter, so the hold
            // is in place in time.)
            // (not while the game's window is out of focus: away by Alt-Tab, Unity can go on taking Alt to be held, and the gear would
            // be held off with it, 2026-11-06)
            Lock(running && HighLogic.LoadedSceneIsFlight && Application.isFocused && GameSettings.MODIFIER_KEY.GetKey());
            if (!HighLogic.LoadedSceneIsFlight || GUIUtility.keyboardControl != 0 || MapView.MapIsEnabled) return;
            if (!GameSettings.MODIFIER_KEY.GetKey()) return;
            if (Input.GetKeyDown(KeyCode.C)) on.Value = !on.Value;
            if (!running) return;
            if (Input.GetKeyDown(KeyCode.V)) finder.Value = finder.Value == 0 ? 1 : 0;
            if (Input.GetKeyDown(KeyCode.F)) focus.Value = (focus.Value + 1) % 3;
            if (Input.GetKeyDown(KeyCode.T)) move.Value = move.Value == Stay || move.Value == Drift ? Still : InSpace(FlightGlobals.ActiveVessel) ? Drift : Stay;
            if (Input.GetKeyDown(KeyCode.G)) move.Value = move.Value == Fly ? Still : Fly;
            float turn = (Input.GetKey(KeyCode.PageUp) ? 1f : 0f) - (Input.GetKey(KeyCode.PageDown) ? 1f : 0f);
            if (turn != 0f)
            {
                // (taking hold of the ring: the focus stays where it was and goes on from there)
                if (focus.Value != 2) { hand.Value = Mathf.Clamp(focusAt, 0.5f, 300f); focus.Value = 2; }
                hand.Value = Mathf.Clamp(hand.Value * Mathf.Exp(turn * 1.1f * Time.unscaledDeltaTime), 0.5f, 300f);
            }
        }

        const string Locked = "CinemaCameraKeys";

        void Lock(bool hold)
        {
            if (hold == locked) return;
            locked = hold;
            if (hold) InputLockManager.SetControlLock(ControlTypes.SAS | ControlTypes.CAMERAMODES | ControlTypes.GROUP_GEARS, Locked);
            else InputLockManager.RemoveControlLock(Locked);
        }

        void LateUpdate()
        {
            FlightCamera camera = FlightCamera.fetch;
            bool wanted = on.Value && camera != null && camera.mainCamera != null && !MapView.MapIsEnabled;
            if (wanted && !running) Begin(camera);
            else if (!wanted && running) Stop();
            View(running && finder.Value != 0);
            // (with the camera on when a flight begins, the viewfinder goes up in the scene's first frames, and the game then shows
            // its new scene's interface itself, without the event (UIMasterController.ShowUI, which turns the canvases on and leaves
            // IsUIShowing false): both were drawn. Put away again, as the viewfinder's, whenever the interface is drawn while it is up.)
            KSP.UI.UIMasterController ui = KSP.UI.UIMasterController.Instance;
            if (viewing && Time.unscaledTime >= hideAgainAt && ui != null && ui.mainCanvas != null && ui.mainCanvas.enabled)
            {
                hideAgainAt = Time.unscaledTime + 1f;
                ours = true;
                try { GameEvents.onHideUI.Fire(); }
                finally { ours = false; }
            }
            if (!running) { readout = on.Value ? "(not in the map)" : ""; gauge.Off(); metered = float.NaN; return; }

            Vessel ship = FlightGlobals.ActiveVessel;
            bool wasInside = inside;
            inside = Inside();
            if (inside != wasInside) { angleSet = -1f; within.Forget(); }
            // (the path kept for the craft being flown, whenever that changes: another ship switched to, or a craft launched; one being
            // flown when another ship is taken over is ended, as it was the other's)
            if (ship != null && (ship != pathShip || ship.vesselName != pathShipName))
            {
                if (holding == Fly) move.Value = Still;
                else LoadPath(ship);
            }

            // ---- the move: left where it is (on the ground, or drifting in space) and turned after the ship, taken slowly round it, or flown along the path.
            // (In the cockpit the game holds the flight camera to a kerbal's eye itself, and a move would pull the view in the windows away from
            // the cockpit round them: a move ends when the cockpit is entered, and the game's own hold is left alone.)
            if (inside && move.Value != Still) move.Value = Still;
            int hold = ship == null || inside ? Still : move.Value == Stay || move.Value == Drift || move.Value == Fly || move.Value == Track || move.Value == Chase || move.Value == Pair || move.Value == Mount ? move.Value : Still;
            // (between one move and the next, a dissolve or a way through black: this frame's picture is kept as it is drawn, and
            // the move changes with the next frame, the kept picture fading out over it (see OnGUI). Not into or out of the cockpit.)
            if (hold != holding && cut.Value != 0 && !inside && !wasInside && shotFrame < 0 && Shoot(camera.mainCamera)) { shotFrame = Time.frameCount; hold = holding; }
            else if (shotFrame == Time.frameCount) hold = holding;
            if (hold != holding)
            {
                if (shotFrame >= 0) { fadeAge = 0f; Kit.Log("Cinema Camera", (cut.Value == 1 ? "a dissolve" : "through black") + " to the next move"); }
                shotFrame = -1; Unshoot();
                if (holding != Still) Release(camera, !inside);
                if (hold != Still && !Hold(camera, ship, hold)) move.Value = Still;
            }
            else if (shotFrame >= 0 && shotFrame < Time.frameCount) { shotFrame = -1; Unshoot(); }       // (asked for, and taken back before it changed)
            if (holding != Still) Place(camera, ship);
            if (fadeAge >= 0f) fadeAge += Step;
            if (holding == Still)
            {
                drawn = 1f;
                if (move.Value == Circle && camera.updateActive) camera.camHdg += round.Value * Mathf.Deg2Rad * Time.deltaTime;
            }

            // ---- the lens: its angle of view. The game's own zoom (which changes that angle) turns the focal length ring.
            float high = High;
            Camera picture = camera.mainCamera;                   // the camera whose picture goes through the lens, and is metered
            if (inside)
            {
                // (In the cockpit the cockpit's own zoom is the lens: its angle of view, taken as a focal length. The game keeps the view in
                // the windows to it, and the ring is left as it was for outside.)
                Camera cockpit = Cockpit();
                if (cockpit != null) picture = cockpit;
                lensNow = Focal(picture.fieldOfView, high);
            }
            else
            {
                if (holding == Still && angleSet > 0f && Mathf.Abs(camera.FieldOfView - angleSet) > 0.02f) focal.Value = Focal(camera.FieldOfView, high);
                lensNow = holding == Fly ? pathLens : focal.Value * drawn;
                float set = Angle(lensNow, high);
                if (Mathf.Abs(camera.FieldOfView - set) > 0.005f) camera.SetFoV(set);
                angleSet = set;
            }
            float angle = Angle(lensNow, high);

            // ---- the focus
            float wantedAt = hand.Value;
            if (focus.Value != 2)
            {
                if (inside) wantedAt = Within(picture != camera.mainCamera ? picture : null, camera);
                else
                {
                    Transform eye = camera.mainCamera.transform;
                    float toShip = ship != null ? Vector3.Distance(eye.position, ship.CoM) : 50f;
                    wantedAt = toShip;
                    // (what the middle of the picture shows: parts, the ground and buildings, kerbals)
                    if (Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, 60000f, (1 << 0) | (1 << 15) | (1 << 17), QueryTriggerInteraction.Ignore))
                    {
                        // On the ship: the part of it that is looked at, if it is the ship that is looked at; otherwise its middle.
                        if (focus.Value == 1 || (ship != null && hit.rigidbody != null && hit.rigidbody.GetComponentInParent<Vessel>() == ship)) wantedAt = hit.distance;
                    }
                    else if (focus.Value == 1) wantedAt = 60000f;
                }
            }
            // (followed as a lens is turned: evenly in "one over the distance", so that it takes as long from 2 m to 4 m as from 100 m to infinity)
            focusWanted = wantedAt;
            // (Since 0.7.0: a change of what the focus is on is a pull of focus, over the time set, slow, quick, slow; then followed as before)
            if (focus.Value != focusWas) { focusWas = focus.Value; rackFrom = focusAt; rackAge = 0f; }
            if (rack.Value > 0.05f && rackAge < rack.Value)
            {
                rackAge += Step;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rackAge / rack.Value));
                focusAt = 1f / Mathf.Lerp(1f / Mathf.Max(rackFrom, 0.1f), 1f / Mathf.Max(wantedAt, 0.1f), k);
            }
            else
            {
                float follow = 1f - Mathf.Exp(-pull.Value * Step);
                focusAt = 1f / Mathf.Lerp(1f / Mathf.Max(focusAt, 0.1f), 1f / Mathf.Max(wantedAt, 0.1f), follow);
            }

            // ---- the shutter: open for this part of a frame. (In a film made frame by frame, each frame stands for a set time of the
            // film whatever the game takes to draw it: the film's frame rate, not the game's, says how much of a frame the shutter is
            // open for. Taken from the game's, a film made at a few frames a second had a blur a tenth of what its shutter said.)
            if (Time.captureFramerate > 0) frames = Time.captureFramerate;
            else frames += (1f / Mathf.Clamp(Time.unscaledDeltaTime, 0.002f, 0.2f) - frames) * 0.1f;
            float open = Mathf.Clamp(360f * frames / shutter.Value, 0f, 360f);
            // ---- the film, and the lens's own marks (the corners darkest with the aperture wide open; the bend only on a wide lens, and never in
            // the cockpit, whose switches are clicked where they are seen)
            float mark = character.Value, wide = Mathf.Clamp01((4f - stop.Value) / 3f);
            // (Since 0.7.0. Whatever is drawn beyond the near camera's reach, 400 m at the pad, lies at its far end in the picture of
            // depths the blur is worked out from: a focus further than that, as a tracker kilometres off has, blurred the ship it is
            // on. So the focus goes no further than that far end, where everything beyond it then lies sharp. At nine tenths of it, as
            // first, a 2400 mm lens at f/2.8 still drew the far ship some 26 pixels wide.)
            float focusUsed = focusAt;
            float grainMid = Grain(speed.Value, out float grainDark);
            if (!inside && camera.mainCamera != null && focusAt > camera.mainCamera.farClipPlane) focusUsed = camera.mainCamera.farClipPlane;
            var look = new Look
            {
                focal = lensNow, sensorHigh = high, stop = stop.Value, focus = focusUsed, blur = bokeh.Value,
                open = open > 4f ? open / 360f : 0f,
                grain = grainMid * Of(grainBy, 1f),
                corners = mark * (0.48f + 0.52f * wide) * Of(cornersBy, 1f), fringe = mark * Of(fringeBy, 1f),
                bend = inside ? 0f : mark * Mathf.Clamp01((35f - lensNow) / 23f) * Of(bendBy, 1f),
            };
            if (darkable) { try { Darker(ref look, grainDark); } catch (Exception) { darkable = false; } }   // (not with a Keystone older than 2026-11-08's)
            // ---- the exposure, when it is the camera's: by how much light the aperture, the shutter and the film take, against
            // what full sun wants (the photographer's "sunny sixteen": f/16 at one over the film speed)
            stops = float.NaN;
            if (exposure.Value == 1)
            {
                float takes = speed.Value * (1f / shutter.Value) / (stop.Value * stop.Value), sunny = 100f * (1f / 100f) / 256f;
                stops = Mathf.Clamp(Mathf.Log(takes / sunny, 2f) - filter.Value, -10f, 10f);
                Grade grade = Grade.None;
                grade.exposure = stops; grade.byHand = true;
                Film.Ask("Cinema Camera", grade);
            }
            ILens best = Lens.Best;
            if (best != lens)
            {
                if (lens != null) lens.Off();
                lens = best;
            }
            if (lens != null) lens.Use(picture, look);
            // ---- (since 0.7.0) lamps flaring in the lens (not in the cockpit, whose own camera draws over the view out of it)
            if (!inside) Flares.Use(camera.mainCamera, flares.Value, stop.Value, character.Value); else Flares.Off();

            // ---- the meter, while the viewfinder is up (it is part of it)
            bool metering = viewing && meter.Value != 0;
            bool filmed = !float.IsNaN(stops) && Film.Best != null;         // (the camera's own exposure is being put on the picture)
            if (metering)
            {
                gauge.Spot = meter.Value == 2;
                gauge.Use(picture, filmed ? stops : 0f, Uncovered());
                if (!float.IsNaN(gauge.Reading))
                {
                    // (an exposure changed since that picture was drawn counts at once: the needle goes with the ring, as on a camera)
                    float now = gauge.Reading + (filmed ? stops - gauge.ReadingExposure : 0f);
                    metered = float.IsNaN(metered) ? now : metered + (now - metered) * (1f - Mathf.Exp(-6f * Step));
                }
            }
            else { gauge.Off(); metered = float.NaN; }

            // ---- what a camera's markings would say
            float f = lensNow, n = stop.Value, blurry = 0.03f * high / 24f;                 // (the blur that still counts as sharp, millimetres on the sensor)
            float beyond = f * f / (n * blurry) + f, at = focusAt * 1000f;
            float nearest = at * (beyond - f) / (beyond + at - 2f * f), furthest = at >= beyond ? float.PositiveInfinity : at * (beyond - f) / (beyond - at);
            readout = Doing(true)
                      + f.ToString("F0") + " mm on " + (Bodies.SensorSaid() ?? Sensors[sensor.Value]) + ": " + angle.ToString(angle < 3f ? "F2" : "F1") + "° from top to bottom. In focus at " + Metres(focusAt) + "; sharp from " + Metres(nearest / 1000f) + " to " +
                      (float.IsInfinity(furthest) ? "as far as can be seen" : Metres(furthest / 1000f)) + ". Shutter open for " + (open / 360f * 100f).ToString("F0") + "% of each frame" +
                      (360f * frames / shutter.Value > 360f ? " (all a frame has: the game is drawing " + frames.ToString("F0") + " a second)" : "") + "."
                      + (float.IsNaN(stops) ? "" : Film.Best == null ? "\n<color=#ffc080>The exposure cannot be set by the camera here. " + Film.WhyNot + "</color>"
                          : "\nExposure by the camera: " + (Mathf.Abs(stops) < 0.15f ? "what full sun wants." : Mathf.Abs(stops).ToString("F1") + (stops > 0f ? " stops over" : " stops under") + " what full sun wants" + (filter.Value > 0.05f ? ", with the filter." : ".")))
                      + (metering && !float.IsNaN(metered) ? "\nThe meter" + (meter.Value == 2 ? " (spot)" : "") + " reads " + Signed(metered) + ": the picture comes out " + (Mathf.Abs(metered) < 0.15f ? "as bright as a middle grey." : Mathf.Abs(metered).ToString("F1") + (metered > 0f ? " stops lighter" : " stops darker") + " than a middle grey.") : "")
                      + (lens == null ? "\n<color=#ffc080>Only the angle of view and the bars are working: there is nothing here to blur the picture. " + Lens.WhyNot + " Installing the TUFX mod gives the camera the rest.</color>"
                          : lens != Lens.Own ? "\n(The blur, the smear and the grain are being done by " + lens.Name + ".)" : "");
        }

        static string Metres(float m) => m >= 10000f ? (m / 1000f).ToString("F0") + " km" : m >= 1000f ? (m / 1000f).ToString("F1") + " km" : m >= 100f ? m.ToString("F0") + " m" : m.ToString("F1") + " m";

        static string Signed(float v) => (v >= 0.05f ? "+" : v <= -0.05f ? "-" : "±") + Mathf.Abs(v).ToString("F1");

        /// <summary>What the camera is doing, if it is doing more than looking: for the page (in words) or the viewfinder (a few capitals).</summary>
        string Doing(bool words)
        {
            if (!words && Time.unscaledTime < saidUntil) return said;
            if (inside)
                return words ? "In the cockpit: the cockpit's own zoom is the lens, and the inside and the view through the windows go through it together. " : "IN THE COCKPIT";
            switch (holding)
            {
                case Stay:
                    return words ? "The camera is staying where it was left, " + Metres(far) + " from the ship" + (drawn > 1.02f ? ", its lens drawn out to follow" : "") + ". " : "STAYING   " + Metres(far).ToUpperInvariant() + " OFF";
                case Drift:
                    return words ? "The camera is drifting where it was left, at the speed the ship had then, " + Metres(far) + " from the ship" + (drawn > 1.02f ? ", its lens drawn out to follow" : "") + ". " : "DRIFTING   " + Metres(far).ToUpperInvariant() + " OFF";
                case Track:
                    return words ? "The camera is tracking from afar, on the ground " + Metres(far) + " from the ship, its lens at " + (focal.Value * drawn).ToString("F0") + " mm. " : "TRACKING   " + Metres(far).ToUpperInvariant() + " OFF";
                case Chase:
                    return words ? "The camera is chasing the ship, " + Metres(far) + " off, keeping its station a moment behind its every change. " : "CHASE   " + Metres(far).ToUpperInvariant() + " OFF";
                case Pair:
                    return words ? "The camera has the ship and " + pairWhat + " in the picture, " + Metres(far) + " behind the ship. " : "WITH " + pairWhat.ToUpperInvariant();
                case Mount:
                    return words ? "The camera is fixed to the ship's " + (mountOn != null && mountOn.partInfo != null ? mountOn.partInfo.title : "part") + ". " : "ON THE SHIP";
                case Fly:
                    bool flown = pathTime >= span.Value;
                    return words ? (flown ? "The path has been flown: the camera stays at its last point, going along with the ship, until the move is changed. " : "Flying the path: " + pathTime.ToString("F1") + " of " + span.Value.ToString("F0") + " s. ")
                                 : flown ? "PATH FLOWN" : "PATH   " + pathTime.ToString("F1") + " / " + span.Value.ToString("F0") + " S";
            }
            return move.Value == Circle ? (words ? "The camera is going round the ship. " : "CIRCLING") : "";
        }

        void Flash(string words)
        {
            said = words;
            saidUntil = Time.unscaledTime + 2f;
        }

        /// <summary>
        /// In the cockpit: how far off what is in the middle of the picture is. The cockpit's insides are in a space of their own
        /// (the game draws them with a camera of their own, over the view in the windows): they are looked for first (see
        /// CockpitFocus), and what is seen through the windows after.
        /// </summary>
        float Within(Camera cockpit, FlightCamera camera)
        {
            float inner = within.Distance(cockpit);
            if (inner > 0f) return Mathf.Max(inner, 0.05f);
            Transform eye = camera.transform;
            if (Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, 60000f, (1 << 0) | (1 << 15) | (1 << 17), QueryTriggerInteraction.Ignore)) return Mathf.Max(hit.distance, 0.5f);
            return 60000f;
        }

        readonly CockpitFocus within = new CockpitFocus();

        void Begin(FlightCamera camera)
        {
            angleBefore = camera.FieldOfView;
            // (begun in the cockpit, where the angle of view is the cockpit's: the one to go back to is the one the game keeps for outside)
            if (Inside() && CameraManager.Instance != null && CameraManager.Instance.existingFlightFoV > 0f) angleBefore = CameraManager.Instance.existingFlightFoV;
            leastBefore = camera.fovMin;
            camera.fovMin = 1f;                                                           // (a 300 mm lens sees 4.6 degrees; the game stops its own zoom at 20)
            angleSet = -1f;
            holding = Still;
            Vessel ship = FlightGlobals.ActiveVessel;
            focusAt = ship != null ? Vector3.Distance(camera.mainCamera.transform.position, ship.CoM) : 20f;
            running = true;
        }

        /// <summary>
        /// Take the flight camera over for a move: left where it is on the ground (fixed to the ground under it, or to the spot
        /// above the ground that it is at), left drifting in space, or flying the path. False if it cannot be.
        /// </summary>
        bool Hold(FlightCamera camera, Vessel ship, int how)
        {
            Vector3d here = camera.transform.position;
            if (how == Stay)
            {
                if (ship.mainBody == null) return false;
                stayOn = ship.mainBody;
                stayLat = stayOn.GetLatitude(here); stayLon = stayOn.GetLongitude(here); stayHigh = stayOn.GetAltitude(here);
            }
            else if (how == Track)
            {
                // (Since 0.7.0.) Put down on the ground so far off, square to the ship's way (east, for one standing on the pad), on the side
                // where the sun is behind the camera; its lens drawn out to have the ship a third of the picture high, and further as it goes.
                CelestialBody body = ship.mainBody;
                if (body == null) return false;
                Vector3d at = ship.CoMD, up = (at - body.position).normalized;
                Vector3d north = Vector3d.Exclude(up, body.transform.up).normalized, east = Vector3d.Cross(up, north);
                Vector3d level = Vector3d.Exclude(up, ship.srf_velocity);
                Vector3d way = level.magnitude > 20.0 ? level.normalized : east;
                Vector3d side = Vector3d.Cross(up, way).normalized;
                CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
                if (sun != null && Vector3d.Dot(side, (sun.position - at).normalized) < 0.0) side = -side;
                double far = Mathf.Clamp(trackFar.Value, 0.5f, 15f) * 1000.0;
                Vector3d place = at + side * far;
                stayOn = body;
                stayLat = body.GetLatitude(place); stayLon = body.GetLongitude(place);
                double ground = body.TerrainAltitude(stayLat, stayLon, true);
                if (body.ocean && ground < 0.0) ground = 0.0;
                // (on a mast of ten metres, and above whatever ground lies between it and the ship, so that it sees the ship's foot: the
                // ground along the way looked at in twelve places)
                double high = ground + 10.0, shipHigh = body.GetAltitude(at);
                for (int k = 1; k < 12; k++)
                {
                    double f = k / 12.0;
                    Vector3d p = place + (at - place) * f;
                    double g = body.TerrainAltitude(body.GetLatitude(p), body.GetLongitude(p), true);
                    if (body.ocean && g < 0.0) g = 0.0;
                    // (the camera so high that the line from it to the ship's foot passes five metres over this ground)
                    if (f < 0.999) high = Math.Max(high, (g + 5.0 - f * shipHigh) / (1.0 - f));
                }
                stayHigh = Math.Min(high, ground + 300.0);
                float length = Length(ship);
                float wanted = High * (float)far / (3f * length);                                   // (mm: the ship a third of the picture high)
                Vector3d spot = body.GetWorldSurfacePosition(stayLat, stayLon, stayHigh);
                stayFar = Mathf.Max(5f, (float)(spot - at).magnitude) * focal.Value / Mathf.Max(wanted, focal.Value);
                stayLook = Quaternion.LookRotation((Vector3)(at - spot), (Vector3)up);
                drawn = 1f;
                camera.DeactivateUpdate();
                holding = how;
                return true;
            }
            else if (how == Chase)
            {
                // (Since 0.7.0.) Where the camera is now from the ship, kept in the ship's own way of going (along its way over the ground, up,
                // across), as a chase plane keeps station; at least 12 m off.
                if (ship.mainBody == null) return false;
                ChaseFrame(ship, out Vector3 fwd, out Vector3 up, out Vector3 side);
                Vector3 off = camera.transform.position - ship.CoM;
                if (off.magnitude < 12f) off = -fwd * 30f + up * 6f + side * 8f;
                chaseAt = new Vector3(Vector3.Dot(off, fwd), Vector3.Dot(off, up), Vector3.Dot(off, side));
                chasePos = off; chaseVel = Vector3.zero; chaseLastVel = (Vector3)ship.srf_velocity;
                stayOn = ship.mainBody;
                stayLook = camera.transform.rotation;
                stayFar = off.magnitude;
                drawn = 1f;
                camera.DeactivateUpdate();
                holding = how;
                return true;
            }
            else if (how == Mount)
            {
                // (Since 0.8.0.) Fixed to the part nearest the camera, where it is, looking the way it looks (see Mounted).
                Part nearest = null;
                float best = float.MaxValue;
                foreach (Part part in ship.parts)
                {
                    if (part == null) continue;
                    float d = Vector3.Distance(part.transform.position, camera.transform.position);
                    if (d < best) { best = d; nearest = part; }
                }
                if (nearest == null) return false;
                if (best > 60f) { Flash("TOO FAR"); told = "The camera is " + Metres(best) + " from the ship: bring it within 60 m of the part it is to be fixed to, looking the way it is to look, then choose the move."; return false; }
                mountOn = nearest;
                mountAway = false;
                mountAt = nearest.transform.InverseTransformPoint(camera.transform.position);
                mountTurn = Quaternion.Inverse(nearest.transform.rotation) * camera.transform.rotation;
                stayOn = ship.mainBody;
                stayLook = camera.transform.rotation;
                stayFar = best;
                drawn = 1f;
                camera.DeactivateUpdate();
                holding = how;
                return true;
            }
            else if (how == Pair)
            {
                // (Since 0.8.0.) From where the camera is now, it goes to its place behind the ship and glides there (see PairShip).
                if (ship.mainBody == null) return false;
                if (!PairTarget(ship, out _, out _)) { Flash("NO TARGET"); told = "There is nothing to frame with the ship: set a target (a craft, a docking port or a world), or come near another craft or the pad."; return false; }
                pairPos = camera.transform.position - ship.CoM; pairVel = Vector3.zero; pairLift = Vector3.zero;
                stayOn = ship.mainBody;
                stayLook = camera.transform.rotation;
                stayFar = pairPos.magnitude;
                drawn = 1f;
                camera.DeactivateUpdate();
                holding = how;
                return true;
            }
            else if (how == Drift)
            {
                // Left in space at the ship's own speed: on an orbit of its own, the one a thing let go of there, moving as the ship
                // was, would have. It is made as the game makes the ship's own orbit from where the ship is and how fast it goes,
                // and the camera is put where that orbit has it every frame after, as the game does for anything on rails: it does not
                // wander however long it is left, and a burn takes the ship away from it.
                CelestialBody body = ship.mainBody;
                if (body == null || ship.orbit == null) return false;
                double now = Planetarium.GetUniversalTime();
                var orbit = new Orbit();
                orbit.UpdateFromStateVectors((here - body.position).xzy, ship.orbit.getOrbitalVelocityAtUT(now), body, now);
                if (double.IsNaN(orbit.semiMajorAxis) || double.IsNaN(orbit.eccentricity) || double.IsNaN(orbit.getPositionAtUT(now).x)) return false;
                drift = orbit; stayOn = body;
                // (and it keeps the roll it had: in space there is no level to come back to, and a camera let go does not turn itself upright)
                driftUp = camera.transform.up;
            }
            else
            {
                if (path.Points.Count == 0) { Flash("NO PATH YET"); told = "There is no path to fly yet: put a point or two first."; return false; }
                pathTime = 0f;
                pathLens = path.Points[0].Lens;
            }
            stayFar = Mathf.Max(5f, Vector3.Distance(camera.transform.position, ship.CoM));
            stayLook = camera.transform.rotation;
            drawn = 1f;
            // (where the game had its camera, from the pivot it hangs from, to be put back there at once when the move ends)
            gameAt = camera.transform.localPosition; gameLooks = camera.transform.localRotation; gameKept = true;
            // (the game's own camera stops putting itself where it thinks it should be, until it is told to again)
            camera.DeactivateUpdate();
            holding = how;
            return true;
        }

        /// <summary>
        /// The move ends: the camera goes back to the ship, where the game keeps it (but in the cockpit, where the game holds it
        /// itself and gives it back on leaving). After a path the lens stays as the path left it.
        /// </summary>
        void Release(FlightCamera camera, bool giveBack)
        {
            if (holding == Still) return;
            if (holding == Fly) focal.Value = Mathf.Clamp(pathLens, focal.Least, focal.Most);
            holding = Still;
            drawn = 1f;
            drift = null;
            if (giveBack && camera != null)
            {
                // (Put back where the game had it, from its pivot, which has gone on with the ship: given back to the game alone, the
                // camera stayed where the move left it for the frame or two before the game next placed it, and on the ship's
                // dropped booster that was two frames of the ship seen from 2.2 km before the cut, the PC's film of 2026-10-25.)
                if (gameKept) { camera.transform.localPosition = gameAt; camera.transform.localRotation = gameLooks; }
                camera.ActivateUpdate();
            }
            gameKept = false;
            angleSet = -1f;
        }
        Vector3 gameAt; Quaternion gameLooks = Quaternion.identity; bool gameKept;

        void End(FlightCamera camera)
        {
            move.Value = Still;
            Release(camera, true);
        }

        /// <summary>The held camera put where its move has it this frame.</summary>
        void Place(FlightCamera camera, Vessel ship)
        {
            if (holding == Fly) { FlyPath(camera, ship); Shake(camera.transform, ship); return; }
            if (holding == Chase) { ChaseShip(camera, ship); Shake(camera.transform, ship); return; }
            if (holding == Pair) { PairShip(camera, ship); Shake(camera.transform, ship); return; }
            if (holding == Mount)
            {
                Mounted(camera, ship);
                if (holding != Mount) return;
                // (the roar's shake kept as an offset, to lay on again where the camera is placed as the cameras draw: BeforeDrawing)
                Vector3 p0 = camera.transform.position; Quaternion r0 = camera.transform.rotation;
                Shake(camera.transform, ship);
                mountShakeAt = camera.transform.position - p0; mountShakeTurn = camera.transform.rotation * Quaternion.Inverse(r0);
                return;
            }
            Vector3d spot;
            if (holding == Stay || holding == Track) spot = stayOn.GetWorldSurfacePosition(stayLat, stayLon, stayHigh);
            else
            {
                spot = drift.getPositionAtUT(Planetarium.GetUniversalTime());
                // (left drifting on a path that comes down, as one let go beside a rocket that has not yet reached orbit is, it is taken back
                // before it reaches the ground)
                if (stayOn.GetAltitude(spot) < stayOn.TerrainAltitude(stayOn.GetLatitude(spot), stayOn.GetLongitude(spot), !stayOn.ocean) + 2.0) { End(camera); return; }
            }
            Vector3 to = (Vector3)(ship.CoMD - spot);
            far = to.magnitude;
            // (once the ship is a quarter of a thousand kilometres off, or round another world, there is nothing left to follow)
            if (far > 250000f || far < 0.01f || ship.mainBody != stayOn) { End(camera); return; }
            Vector3 up = holding == Drift ? driftUp : ((Vector3)(spot - stayOn.position)).normalized, ahead = to / far;
            // (turned after it as a hand on a tripod turns a camera: a moment behind; kept level on the ground (as it was in space), but looking
            // straight along that way, where there is no level)
            Quaternion wanted = Quaternion.LookRotation(ahead, Mathf.Abs(Vector3.Dot(ahead, up)) < 0.995f ? up : stayLook * Vector3.up);
            // (A ship burning away is followed into a long lens, and the moment behind that is nothing through a short one is half the
            // picture through a long one: drifting, the camera keeps the closer to it the narrower its lens sees.)
            float rate = holding == Drift || holding == Track ? 7f * Mathf.Clamp(25f / Angle(focal.Value * drawn, High), 1f, 8f) : 7f;
            stayLook = Quaternion.Slerp(stayLook, wanted, 1f - Mathf.Exp(-rate * Step));
            Transform rig = camera.transform;
            rig.position = spot; rig.rotation = stayLook;
            // (where Smooth Running carries the craft on between the game's steps, a camera standing on the ground stays standing;
            // one left drifting goes on with the craft, which it drifts beside)
            if ((holding == Stay || holding == Track) && keepable) { try { KeepStill(); } catch (Exception) { keepable = false; } }     // (not with a Keystone older than 0.9.0)
            Shake(rig, ship);
            float most = Mathf.Max(1f, 2400f / focal.Value);
            float wantedDrawn = keep.Value || holding == Track ? Mathf.Clamp(far / stayFar, 1f, most) : 1f;
            drawn = Mathf.Lerp(drawn, wantedDrawn, 1f - Mathf.Exp(-3f * Step));
        }

        static bool keepable = true;
        static void KeepStill() => Keystone.Motion.KeepCamera();

        /// <summary>How much grainier the shadows are (Look.grainDark), apart from the rest of the look: a Keystone older than
        /// 2026-11-08's has no such field, and its lens spreads the grain as before.</summary>
        static bool darkable = true;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Darker(ref Look look, float dark) => look.grainDark = dark;

        // ---- (since 0.7.0) the chase: a camera keeping station on the ship as a chase plane does, a moment behind its every change
        Vector3 chaseAt, chasePos, chaseVel, chaseLastVel;

        /// <summary>The ship's own way of going: along its way over the ground (its nose's way while it hardly moves), up, and across.</summary>
        static void ChaseFrame(Vessel ship, out Vector3 fwd, out Vector3 up, out Vector3 side)
        {
            up = ((Vector3)(ship.CoMD - ship.mainBody.position)).normalized;
            Vector3 going = (Vector3)ship.srf_velocity;
            fwd = going.magnitude > 8f ? going.normalized : ship.ReferenceTransform != null ? ship.ReferenceTransform.up : ship.transform.up;
            if (Mathf.Abs(Vector3.Dot(fwd, up)) > 0.98f) { Vector3 n = Vector3.ProjectOnPlane((Vector3)ship.mainBody.transform.up, up); up = n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.forward; }
            side = Vector3.Cross(up, fwd).normalized;
            up = Vector3.Cross(fwd, side).normalized;
        }

        /// <summary>
        /// The chase camera this frame: held to its station by a spring that settles in about two seconds without overshooting, and
        /// pushed back by the ship's own speeding up (in the ship's frame the camera is a mass the ship pulls along): a rocket's burn
        /// leaves it behind for a moment and it closes up again; a turn swings it round wide. It looks at the ship's middle.
        /// </summary>
        void ChaseShip(FlightCamera camera, Vessel ship)
        {
            float dt = Step;
            ChaseFrame(ship, out Vector3 fwd, out Vector3 up, out Vector3 side);
            Vector3 target = fwd * chaseAt.x + up * chaseAt.y + side * chaseAt.z;
            Vector3 v = (Vector3)ship.srf_velocity;
            Vector3 speedUp = dt > 1e-4f ? (v - chaseLastVel) / dt : Vector3.zero;
            chaseLastVel = v;
            if (speedUp.magnitude > 200f) speedUp = Vector3.zero;                              // (the game putting the ship somewhere else)
            // (a spring of two seconds as installed, critically damped; the page's fine detail another length, damped the same)
            float by = 2f / Of(chaseTakes, 2f), k = 10f * by * by, c = 6.3f * by;
            if (!FlightDriver.Pause)
            {
                Vector3 pull = k * (target - chasePos) - c * chaseVel - speedUp;
                chaseVel += pull * dt;
                chasePos += chaseVel * dt;
                if ((chasePos - target).magnitude > 400f) { chasePos = target; chaseVel = Vector3.zero; }
            }
            Transform rig = camera.transform;
            rig.position = ship.CoM + chasePos;
            // (kept level with the horizon, as a chase plane's camera is: the sky's up, not the way the ship's own frame has it)
            Vector3 sky = ((Vector3)(ship.CoMD - ship.mainBody.position)).normalized, look = -chasePos.normalized;
            Quaternion wanted = Quaternion.LookRotation(look, Mathf.Abs(Vector3.Dot(look, sky)) < 0.995f ? sky : up);
            stayLook = Quaternion.Slerp(stayLook, wanted, 1f - Mathf.Exp(-9f * dt));
            rig.rotation = stayLook;
            far = chasePos.magnitude;
        }

        // ---- (since 0.8.0) on the ship: a camera bolted to a part, as on a real rocket's skirt or a capsule's window
        Part mountOn;
        Vector3 mountAt;
        Quaternion mountTurn = Quaternion.identity;

        /// <summary>
        /// The camera on the ship this frame: where it was put on its part, in that part's own axes, turning as the part turns (the
        /// booster's view of the ground falling away, the capsule's of the planet below). It shakes with the engines' roar as one
        /// fixed to the hull would (see Shake). When its part comes away from the ship (a booster dropped) it goes on with the part,
        /// as the cameras on real boosters do, watching the ship fly on, until the part is gone, no longer loaded, or 2.25 km from
        /// the ship flown.
        /// </summary>
        void Mounted(FlightCamera camera, Vessel ship)
        {
            Vessel on = mountOn != null ? mountOn.vessel : null;
            bool gone = mountOn == null || mountOn.State == PartStates.DEAD || on == null || !on.loaded;
            if (!gone && on != ship && (on.CoMD - ship.CoMD).magnitude > 2250.0) gone = true;
            if (gone) { Flash("ITS PART IS GONE"); End(camera); return; }
            if (on != ship && !mountAway) { mountAway = true; Flash("ON THE " + (mountOn.partInfo != null ? mountOn.partInfo.title.ToUpperInvariant() : "PART")); }
            Transform rig = camera.transform;
            rig.position = mountOn.transform.TransformPoint(mountAt);
            rig.rotation = mountOn.transform.rotation * mountTurn;
            stayLook = rig.rotation;
            far = Vector3.Distance(rig.position, ship.CoM);
        }

        bool mountAway;
        int mountDrawn = -1;
        Vector3 mountShakeAt;
        Quaternion mountShakeTurn = Quaternion.identity;

        /// <summary>
        /// (Since 0.8.0.) The camera on a part put where the part is drawn, just before the flight's own cameras draw: where Smooth
        /// Running is on the craft are carried on to the frame's moment then, each by its own speed, and the camera with the ship it
        /// follows only. On a dropped booster going its own way, placed in the frame's scripts it stood off by the difference, up to a
        /// metre a frame and jumping as the steps fell; and on a ship that turns fast it lagged the turn. Placed here from the part's
        /// own carrying (Keystone's Motion).
        /// </summary>
        void BeforeDrawing(Camera c)
        {
            if (holding != Mount || mountOn == null || mountDrawn == Time.frameCount || c.targetTexture != null) return;
            FlightCamera fc = FlightCamera.fetch;
            if (fc == null || fc.cameras == null) return;
            bool ours = false;
            for (int i = 0; i < fc.cameras.Length; i++) if (fc.cameras[i] == c) { ours = true; break; }
            if (!ours || !keepable) return;
            mountDrawn = Time.frameCount;
            try { PlaceMounted(fc.transform, mountOn.vessel, mountOn.transform.TransformPoint(mountAt), mountOn.transform.rotation * mountTurn, mountShakeAt, mountShakeTurn); }
            catch (Exception) { keepable = false; }                                     // (not with a Keystone older than 0.9.0)
        }

        static void PlaceMounted(Transform rig, Vessel on, Vector3 at, Quaternion turn, Vector3 shakeAt, Quaternion shakeTurn)
        {
            if (!Keystone.Motion.Now || on == null) return;
            rig.position = Keystone.Motion.CarryWith(on, at) + shakeAt;
            rig.rotation = shakeTurn * Keystone.Motion.TurnWith(on, turn);
        }

        // ---- (since 0.8.0) the ship and its target: both in the picture, as a dogfight's camera or a docking's has them
        Vector3 pairPos, pairVel, pairLift;
        string pairWhat = "";

        /// <summary>
        /// What the ship is framed with: the target set in the game (a craft, a docking port on one, or a world), else the nearest other
        /// craft within 2.5 km, else the space centre's launch pad within 30 km while the ship is more than a few of its lengths from it.
        /// </summary>
        bool PairTarget(Vessel ship, out Vector3d at, out string what)
        {
            at = Vector3d.zero; what = "";
            ITargetable set = FlightGlobals.fetch != null ? FlightGlobals.fetch.VesselTarget : null;
            if (set != null)
            {
                if (set is CelestialBody b) { at = b.position; what = b.bodyName; return true; }
                if (set is Vessel v) { if (v != ship) { at = v.CoMD; what = v.vesselName; return true; } }
                else
                {
                    Transform t = set.GetTransform();
                    Vessel of = set.GetVessel();
                    if (t != null && of != ship) { at = t.position; what = (set is ModuleDockingNode ? "the docking port on " : "") + (of != null ? of.vesselName : set.GetName()); return true; }
                }
            }
            double best = 2500.0;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                // (not what a ship drops: a spent booster nine metres off was taken for the target, and the camera swung to it, the PC's
                // film of 2026-10-25; nor a flag)
                if (v == null || v == ship || v.state == Vessel.State.DEAD || v.vesselType == VesselType.Debris || v.vesselType == VesselType.Flag) continue;
                double d = (v.CoMD - ship.CoMD).magnitude;
                if (d < best) { best = d; at = v.CoMD; what = v.vesselName; }
            }
            if (what.Length > 0) return true;
            if (ship.mainBody != null && ship.mainBody.isHomeWorld && Pad(ship.mainBody, out Vector3d pad))
            {
                double d = (pad - ship.CoMD).magnitude;
                if (d < 30000.0 && d > 3.0 * Length(ship)) { at = pad; what = "the launch pad"; return true; }
            }
            return false;
        }

        /// <summary>The launch pad's place, where the game puts a craft on it.</summary>
        static bool Pad(CelestialBody home, out Vector3d at)
        {
            at = Vector3d.zero;
            PSystemSetup ps = PSystemSetup.Instance;
            if (ps == null || ps.SpaceCenterFacilityLaunchSites == null) return false;
            foreach (PSystemSetup.SpaceCenterFacility f in ps.SpaceCenterFacilityLaunchSites)
            {
                if (f == null || f.name != "LaunchPad" || f.spawnPoints == null || f.hostBody != home) continue;
                foreach (PSystemSetup.SpaceCenterFacility.SpawnPoint sp in f.spawnPoints)
                {
                    if (sp == null || sp.name != "LaunchPad") continue;
                    if (!sp.latlonaltSet) sp.SetSpawnPointLatLonAlt();
                    if (!sp.latlonaltSet) return false;
                    at = home.GetWorldSurfacePosition(sp.latitude, sp.longitude, sp.altitude);
                    return true;
                }
            }
            return false;
        }

        /// <summary>How long the ship is: the furthest of its parts from its middle, both ways, and so much more at each end; never under
        /// the least.</summary>
        static float Length(Vessel ship, float least = 4f, float ends = 1f)
        {
            float length = least;
            foreach (Part part in ship.parts) if (part != null) length = Mathf.Max(length, 2f * (Vector3.Distance(part.transform.position, ship.CoM) + ends));
            return length;
        }

        /// <summary>
        /// The ship and its target this frame. The camera stands behind the ship from the target, lifted above the line between them
        /// (towards the sky, or in space the orbit's up) and swung a little to the side the sun is on, so that the ship is lit: so far
        /// back that the ship is a fifth of the picture high, and so far up that the ship and the target are two fifths of the picture
        /// apart, looking between the two, the ship low and near, what it makes for high and beyond. A spring takes it there and keeps
        /// it there (settling in about three seconds without overshooting), so a turn of the ship, a new target or the target coming
        /// near swing it round smoothly. Kept level with the horizon, and above the ground.
        /// </summary>
        void PairShip(FlightCamera camera, Vessel ship)
        {
            float dt = Step;
            if (!PairTarget(ship, out Vector3d target, out pairWhat)) { End(camera); return; }
            Vector3d line = target - ship.CoMD;
            float apart = (float)line.magnitude;
            if (apart < 0.5f) { End(camera); return; }
            Vector3 u = (Vector3)(line / apart);
            Vector3 sky = ((Vector3)(ship.CoMD - ship.mainBody.position)).normalized;
            // (Which way is up across the line to the target: the sky's, as far as it lies across the line. With the target nearly
            // straight below or above that is next to nothing and swings with the least drift, and the camera swung round with it
            // (the PC's film of 2026-10-25: 15 degrees a frame for a third of a second, the pad straight under the climbing rocket):
            // there the way it last had is kept, carried round with the line; the orbit's up before there was one.)
            Vector3 fromSky = Vector3.ProjectOnPlane(sky, u), fromLast = Vector3.ProjectOnPlane(pairLift, u);
            if (fromLast.sqrMagnitude < 1e-6f) fromLast = Vector3.ProjectOnPlane(ship.orbit != null ? (Vector3)ship.orbit.GetOrbitNormal().xzy : Vector3.forward, u);
            float bySky = Mathf.Clamp01((fromSky.magnitude - 0.1f) / 0.2f);
            Vector3 lift = fromSky.normalized * bySky + fromLast.normalized * (1f - bySky);
            lift = lift.sqrMagnitude > 0.25f ? lift.normalized : fromLast.normalized;
            pairLift = lift;
            Vector3 side = Vector3.Cross(u, lift);
            CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            if (sun != null && Vector3.Dot(side, (Vector3)(sun.position - ship.CoMD).normalized) < 0f) side = -side;
            Vector3 off = (lift * Mathf.Cos(35f * Mathf.Deg2Rad) + side * Mathf.Sin(35f * Mathf.Deg2Rad)).normalized;
            float view = Angle(focal.Value * drawn, High) * Mathf.Deg2Rad;
            // (a small probe framed as it is, not as a rocket's four metres)
            float back = Mathf.Clamp(Length(ship, 1.5f, 0.4f) / (0.2f * view), 6f, 3000f);
            // (how high above the line: the ship and the target the wanted angle apart as seen from there; found by halving)
            float wanted = 0.4f * view, lo = 0.02f, hi = 1.4f;
            for (int k = 0; k < 16; k++)
            {
                float a = 0.5f * (lo + hi);
                float seen = a - Mathf.Atan2(back * Mathf.Sin(a), apart + back * Mathf.Cos(a));
                if (seen < wanted) lo = a; else hi = a;
            }
            float up = 0.5f * (lo + hi);
            Vector3 station = -u * (back * Mathf.Cos(up)) + off * (back * Mathf.Sin(up));
            // (a spring of three seconds as installed, critically damped; the page's fine detail another length, damped the same)
            float by2 = 3f / Of(pairTakes, 3f), k2 = 4f * by2 * by2, c2 = 4f * by2;
            if (!FlightDriver.Pause)
            {
                pairVel += (k2 * (station - pairPos) - c2 * pairVel) * dt;
                pairPos += pairVel * dt;
                if ((pairPos - station).magnitude > 5000f) { pairPos = station; pairVel = Vector3.zero; }
            }
            Vector3 place = ship.CoM + pairPos;
            // (never in the ground: kept two metres over it, and over the sea)
            CelestialBody body = ship.mainBody;
            Vector3d placeD = (Vector3d)place;
            double lat = body.GetLatitude(placeD), lon = body.GetLongitude(placeD), high = body.GetAltitude(placeD);
            double ground = body.TerrainAltitude(lat, lon, true);
            if (body.ocean && ground < 0.0) ground = 0.0;
            if (high < ground + 2.0) { place += sky * (float)(ground + 2.0 - high); pairPos = place - ship.CoM; }
            Transform rig = camera.transform;
            rig.position = place;
            Vector3 toShip = (ship.CoM - place).normalized, toTarget = ((Vector3)target - place).normalized;
            Vector3 look = (toShip + toTarget).normalized;
            if (look.sqrMagnitude < 0.5f) look = toShip;
            // (and the picture's up: the sky's, as far as it lies across the way the camera looks; looking nearly straight down or up,
            // the picture's up as it last was, eased over, never flipped from one to the other at a line)
            Vector3 upSky = Vector3.ProjectOnPlane(sky, look), upLast = Vector3.ProjectOnPlane(stayLook * Vector3.up, look);
            float upBySky = Mathf.Clamp01((upSky.magnitude - 0.05f) / 0.15f);
            Vector3 upWay = upSky.normalized * upBySky + upLast.normalized * (1f - upBySky);
            if (upWay.sqrMagnitude < 0.25f) upWay = Vector3.ProjectOnPlane(lift, look);
            Quaternion aim = Quaternion.LookRotation(look, upWay);
            stayLook = Quaternion.Slerp(stayLook, aim, 1f - Mathf.Exp(-6f * dt));
            rig.rotation = stayLook;
            far = (ship.CoM - place).magnitude;
        }

        // ---- (since 0.7.0) the hand's wander and the launch's roar
        readonly Dictionary<ModuleEngines, float> litAt = new Dictionary<ModuleEngines, float>();
        float roarLooked = -10f, roar;

        /// <summary>
        /// A held camera's own movement, on top of where its move put it: the slow wander and the breath of a camera held in the hands
        /// (Hand-held), and the shake of a launch's roar near it, which begins when the sound gets there (a second for every 340 m)
        /// and goes as its strength in decibels, the louder the nearer and the more thrust. Through a long lens both show the more.
        /// </summary>
        void Shake(Transform rig, Vessel ship)
        {
            float t = Time.time;
            float hand = shaky.Value;
            // (the roar: every running engine near the camera, heard once its sound has had time to get here)
            if (Time.unscaledTime - roarLooked > 0.1f)
            {
                roarLooked = Time.unscaledTime;
                float sum = 0f;
                Vector3 eye = rig.position;
                foreach (Vessel v in FlightGlobals.VesselsLoaded)
                {
                    if (v == null || v.parts == null) continue;
                    float d = Vector3.Distance(eye, v.CoM);
                    if (d > 6000f) continue;
                    foreach (Part part in v.parts)
                    {
                        if (part == null) continue;
                        for (int m = 0; m < part.Modules.Count; m++)
                        {
                            if (!(part.Modules[m] is ModuleEngines e)) continue;
                            if (!(e.finalThrust > 1f)) { litAt.Remove(e); continue; }
                            if (!litAt.TryGetValue(e, out float since)) { since = t; litAt[e] = since; }
                            if (t - since < d / 340f) continue;
                            sum += e.finalThrust / Mathf.Max(d, 25f);
                        }
                    }
                }
                roar = Mathf.Clamp01(Mathf.Log10(1f + 10f * sum) / 3f);
            }
            if (hand <= 0.001f && roar <= 0.01f) return;
            float lensFactor = Mathf.Clamp(lensNow / 50f, 1f, 6f);
            // (held in the hands: drift of a few tenths of a degree, the breath every four seconds or so)
            float yaw = hand * 0.32f * (Mathf.PerlinNoise(t * 0.45f, 1.3f) - 0.5f) * 2f + hand * 0.08f * Mathf.Sin(t * 1.55f);
            float pitch = hand * 0.28f * (Mathf.PerlinNoise(t * 0.5f, 7.1f) - 0.5f) * 2f + hand * 0.12f * Mathf.Sin(t * 1.57f + 0.4f);
            float tilt = hand * 0.4f * (Mathf.PerlinNoise(t * 0.3f, 11.7f) - 0.5f) * 2f;
            // (the roar: fast and small, the more the louder; a tripod shakes as well as a hand)
            float r = roar * roar * 1.1f * Of(roarBy, 1f);
            yaw += r * (Mathf.PerlinNoise(t * 17f, 3.3f) - 0.5f) * 2f;
            pitch += r * (Mathf.PerlinNoise(t * 19f, 5.9f) - 0.5f) * 2f;
            tilt += 0.5f * r * (Mathf.PerlinNoise(t * 13f, 8.2f) - 0.5f) * 2f;
            rig.rotation = rig.rotation * Quaternion.Euler(pitch / lensFactor * Mathf.Sqrt(lensFactor), yaw / lensFactor * Mathf.Sqrt(lensFactor), tilt);
        }

        /// <summary>
        /// The path, flown: the camera where the path has it after so long, in the path's frame as it is now for the ship. Once it
        /// is flown, the camera stays at its last point (going along with the ship) until the move is changed.
        /// </summary>
        void FlyPath(FlightCamera camera, Vessel ship)
        {
            if (path.Points.Count == 0 || ship.mainBody == null) { End(camera); return; }
            // (by the player's clock, so that a path takes the time it is given however fast or slowly the game runs; but not while the game is paused)
            if (!FlightDriver.Pause) pathTime = Mathf.Min(pathTime + Step, span.Value + 1f);
            path.At(span.Value > 0f ? pathTime / span.Value : 1f, out Vector3 at, out Quaternion aim, out float lensAt);
            Quaternion frame = CinePath.Frame(path.Turns, ship);
            Transform rig = camera.transform;
            rig.position = (Vector3)ship.CoMD + frame * at;
            rig.rotation = frame * aim;
            pathLens = Mathf.Clamp(lensAt, 1f, 2400f);
            far = at.magnitude;
        }

        // ---- the path, set point by point

        static string Named(Vessel ship) => ship.GetDisplayName() ?? ship.vesselName ?? "";

        void LoadPath(Vessel ship)
        {
            pathShip = ship; pathShipName = ship.vesselName;
            pathFor = Named(ship);
            path.Read(mod.Kept(CinePath.KeyFor(pathFor)));
            told = "";
        }

        /// <summary>Kept in the camera's settings file under the craft's name (taken out of it again when there is nothing left of it).</summary>
        void SavePath() => mod.Keep(CinePath.KeyFor(pathFor), path.Points.Count > 0 ? path.Write() : null);

        string PutPoint()
        {
            FlightCamera camera = FlightCamera.fetch;
            Vessel ship = FlightGlobals.ActiveVessel;
            if (!running || camera == null || ship == null || ship.mainBody == null) return "The camera has to be on, in flight, to set a path.";
            if (inside) return "Not from the cockpit: a path is for the camera outside the ship.";
            if (holding == Fly) return "Not while the path is being flown.";
            if (ship != pathShip || ship.vesselName != pathShipName) LoadPath(ship);
            if (path.Points.Count >= CinePath.Most) return "A path has " + CinePath.Most + " points at most.";
            if (path.Points.Count == 0) path.Turns = CinePath.TurnOf(camera);
            Quaternion back = Quaternion.Inverse(CinePath.Frame(path.Turns, ship));
            Transform rig = camera.transform;
            var point = new CinePath.Point { At = back * (rig.position - (Vector3)ship.CoMD), Aim = back * rig.rotation, Lens = lensNow };
            path.Points.Add(point);
            SavePath();
            Flash("POINT " + path.Points.Count + " PUT");
            return "Point " + path.Points.Count + " put, " + Metres(point.At.magnitude) + " from the ship, through " + lensNow.ToString("F0") + " mm.";
        }

        string TakeLast()
        {
            if (holding == Fly || path.Points.Count == 0) return "";
            path.Points.RemoveAt(path.Points.Count - 1);
            SavePath();
            Flash(path.Points.Count > 0 ? "POINT " + (path.Points.Count + 1) + " TAKEN AWAY" : "PATH CLEARED");
            return path.Points.Count > 0 ? "The last point is taken away: " + path.Points.Count + " left." : "The path is cleared.";
        }

        string ClearPath()
        {
            if (holding == Fly) return "";
            path.Points.Clear();
            SavePath();
            Flash("PATH CLEARED");
            return "The path is cleared.";
        }

        string PathSaid()
        {
            int n = path.Points.Count;
            string craft = "\"" + pathFor + "\"";
            if (n == 0) return "No path for " + craft + " yet. Put the camera where the shot is to begin (with the mouse, as ever) and put a point there; then where it is to go next, and so on, up to " + CinePath.Most + ". Each point keeps the camera's place, which way it looks and its lens. The path is kept for this craft, and goes along with it.";
            return "For " + craft + ": " + n + (n == 1 ? " point" : " points") + ", flown in " + span.Value.ToString("F0") + " s; " + CinePath.TurnSaid[(int)path.Turns] + ". Fly it with the button, as the camera move, or with the modifier key and G.";
        }

        void Stop()
        {
            if (!running) return;
            running = false;
            bool within = Inside();
            Release(FlightCamera.fetch, !within);
            if (move != null) move.Value = Still;
            View(false);
            if (lens != null) lens.Off();
            lens = null;
            Flares.Off();
            gauge.Off();
            metered = float.NaN;
            Lock(false);
            FlightCamera camera = FlightCamera.fetch;
            if (camera != null)
            {
                camera.fovMin = leastBefore;
                // (in the cockpit the game keeps its own angle of view; the one put back is the one it goes back to outside)
                if (within && CameraManager.Instance != null) CameraManager.Instance.existingFlightFoV = Mathf.Clamp(angleBefore, camera.fovMin, camera.fovMax);
                else camera.SetFoV(Mathf.Clamp(angleBefore, camera.fovMin, camera.fovMax));
            }
            angleSet = -1f;
        }

        /// <summary>How much of the picture's height is not under the cinema bars (0 to 1).</summary>
        float Uncovered()
        {
            if (!hidden || bars.Value == 0) return 1f;
            return Mathf.Clamp01(Screen.width / BarShape[bars.Value] / Screen.height);
        }

        void OnGUI()
        {
            if (!running || Event.current.type != EventType.Repaint) return;
            GUI.depth = 20;                                       // (behind the settings window and anything else that is drawn over the picture)
            if (black == null)
            {
                black = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                black.SetPixel(0, 0, Color.white);
                black.Apply();
            }
            Fade();
            float top = 0f, bottom = Screen.height;
            if (hidden && bars.Value != 0)
            {
                float tall = Screen.width / BarShape[bars.Value];
                if (tall < Screen.height - 1f)
                {
                    float bar = 0.5f * (Screen.height - tall);
                    Fill(new Rect(0f, 0f, Screen.width, bar), Color.black);
                    Fill(new Rect(0f, Screen.height - bar, Screen.width, bar), Color.black);
                    top = bar; bottom = Screen.height - bar;
                }
            }
            // (a real camera's own screen in its place, where one is chosen: Bodies.cs)
            if (viewing && !Bodies.Draw(top, bottom, lensNow, focusAt, Mathf.Abs(1f / Mathf.Max(focusAt, 0.1f) - 1f / Mathf.Max(focusWanted, 0.1f)) < 0.03f / Mathf.Max(focusAt, 0.1f), Doing(false))) Finder(top, bottom);
        }

        // ---- the way from one move to the next
        RenderTexture shot;                 // (the last picture of the move that ends, as drawn, with nothing of the interface)
        CommandBuffer shooting;
        Camera shotBy;
        int shotFrame = -1;
        float fadeAge = -1f;                // (how far the fade has gone, by the camera's own step: a film made frame by frame has it at its own pace)

        /// <summary>This frame's picture kept as the near camera finishes it (after its own effects, before any interface). False if it cannot be.</summary>
        bool Shoot(Camera near)
        {
            if (near == null) return false;
            if (shot == null || shot.width != Screen.width || shot.height != Screen.height)
            {
                if (shot != null) { shot.Release(); Destroy(shot); }
                // (kept in a picture with no alpha of its own, which reads as solid: the camera's own alpha is anything, and the kept
                // picture is faded over the next by a colour's alpha)
                RenderTextureFormat kind = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.RGB565;
                shot = new RenderTexture(Screen.width, Screen.height, 0, kind) { name = "Cinema Camera: the last of a move" };
                if (shooting != null) { Unshoot(); shooting.Release(); shooting = null; }
            }
            if (shooting == null)
            {
                shooting = new CommandBuffer { name = "Cinema Camera: the last of a move" };
                shooting.Blit(BuiltinRenderTextureType.CameraTarget, shot);
            }
            near.AddCommandBuffer(CameraEvent.AfterEverything, shooting);
            shotBy = near;
            return true;
        }

        void Unshoot()
        {
            if (shotBy != null && shooting != null) shotBy.RemoveCommandBuffer(CameraEvent.AfterEverything, shooting);
            shotBy = null;
        }

        /// <summary>The kept picture over the new move's: fading out (a dissolve), or down to black and up again.</summary>
        void Fade()
        {
            if (fadeAge < 0f || shot == null) return;
            float t = fadeAge / Mathf.Max(cutTime.Value, 0.05f);
            if (t >= 1f) { fadeAge = -1f; return; }
            var whole = new Rect(0f, 0f, Screen.width, Screen.height);
            Color was = GUI.color;
            if (cut.Value == 1) { GUI.color = new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0f, 1f, t)); GUI.DrawTexture(whole, shot); }
            else if (t < 0.5f) { GUI.color = Color.white; GUI.DrawTexture(whole, shot); Fill(whole, new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, 2f * t))); }
            else Fill(whole, new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, 2f * (1f - t))));
            GUI.color = was;
        }

        static void Fill(Rect where, Color colour)
        {
            Color was = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(where, black);
            GUI.color = was;
        }

        // ---- the viewfinder: what a camera shows round its picture

        static GUIStyle words;
        static Texture2D dot;
        static Texture2D[] arrows;
        static readonly Color Marks = new Color(1f, 1f, 1f, 0.8f), Faint = new Color(1f, 1f, 1f, 0.22f), Sharp = new Color(0.45f, 1f, 0.5f, 0.95f), Red = new Color(1f, 0.16f, 0.12f, 1f), Amber = new Color(1f, 0.75f, 0.3f, 1f);

        /// <summary>Words on the picture: light, with a dark copy just behind them so that they read on anything. `side` -1 puts their left end at x, 0 their middle, 1 their right end.</summary>
        static float Say(float x, float y, string text, float size, Color colour, int side = -1)
        {
            if (words == null) words = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = false, richText = false, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
            words.fontSize = Mathf.Max(8, Mathf.RoundToInt(size));
            var content = new GUIContent(text);
            Vector2 takes = words.CalcSize(content);
            var where = new Rect(side < 0 ? x : side == 0 ? x - 0.5f * takes.x : x - takes.x, y, takes.x + 2f, takes.y);
            Color was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f * colour.a);
            words.normal.textColor = Color.white;
            GUI.Label(new Rect(where.x + 1f, where.y + 1f, where.width, where.height), content, words);
            GUI.color = colour;
            GUI.Label(where, content, words);
            GUI.color = was;
            return takes.x;
        }

        /// <summary>An L at a corner: its two arms run the given ways (1 or -1 each) from the corner.</summary>
        static void Corner(float x, float y, float arm, float thick, int right, int down, Color colour)
        {
            Fill(new Rect(right > 0 ? x : x - arm, down > 0 ? y : y - thick, arm, thick), colour);
            Fill(new Rect(right > 0 ? x : x - thick, down > 0 ? y : y - arm, thick, arm), colour);
        }

        /// <summary>A small triangle, with a dark one just behind it: pointing up (0), left (1) or right (2).</summary>
        static void Arrow(Rect where, int way, Color colour)
        {
            if (arrows == null) arrows = new Texture2D[3];
            if (arrows[way] == null)
            {
                var made = new Texture2D(16, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int j = 0; j < 16; j++)
                    for (int i = 0; i < 16; i++)
                    {
                        // (the picture's rows run up from its foot: j = 15 is drawn at the top)
                        float x = (i + 0.5f) / 16f, y = (j + 0.5f) / 16f;
                        float room = way == 0 ? 0.5f * (1f - y) - Mathf.Abs(x - 0.5f) : way == 1 ? 0.5f * x - Mathf.Abs(y - 0.5f) : 0.5f * (1f - x) - Mathf.Abs(y - 0.5f);
                        made.SetPixel(i, j, new Color(1f, 1f, 1f, Mathf.Clamp01(room * 16f + 0.5f)));
                    }
                made.Apply();
                arrows[way] = made;
            }
            Color was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f * colour.a);
            GUI.DrawTexture(new Rect(where.x + 1f, where.y + 1f, where.width, where.height), arrows[way]);
            GUI.color = colour;
            GUI.DrawTexture(where, arrows[way]);
            GUI.color = was;
        }

        void Finder(float top, float bottom)
        {
            float wide = Screen.width, k = Screen.height / 720f, thick = Mathf.Max(1f, Mathf.Round(2f * k)), thin = Mathf.Max(1f, Mathf.Round(k));
            float inset = 20f * k;
            var frame = new Rect(inset, top + inset, wide - 2f * inset, bottom - top - 2f * inset);
            float midX = 0.5f * wide, midY = 0.5f * (top + bottom);

            // ---- the frame: a mark at each corner of what will be in the picture, and (if asked for) the lines that part it in thirds
            float arm = 36f * k;
            Corner(frame.xMin, frame.yMin, arm, thick, 1, 1, Marks);
            Corner(frame.xMax, frame.yMin, arm, thick, -1, 1, Marks);
            Corner(frame.xMin, frame.yMax, arm, thick, 1, -1, Marks);
            Corner(frame.xMax, frame.yMax, arm, thick, -1, -1, Marks);
            if (finder.Value == 2)
                for (int n = 1; n <= 2; n++)
                {
                    Fill(new Rect(frame.xMin + frame.width * n / 3f, frame.yMin, thin, frame.height), Faint);
                    Fill(new Rect(frame.xMin, frame.yMin + frame.height * n / 3f, frame.width, thin), Faint);
                }

            // ---- the middle: the focus bracket (green once the focus has come to rest on what it was asked for), and a small cross
            bool rested = Mathf.Abs(1f / Mathf.Max(focusAt, 0.1f) - 1f / Mathf.Max(focusWanted, 0.1f)) < 0.03f / Mathf.Max(focusAt, 0.1f);
            Color bracket = rested ? Sharp : Marks;
            float half = 30f * k, tab = 10f * k;
            Corner(midX - half, midY - half, tab, thick, 1, 1, bracket);
            Corner(midX + half, midY - half, tab, thick, -1, 1, bracket);
            Corner(midX - half, midY + half, tab, thick, 1, -1, bracket);
            Corner(midX + half, midY + half, tab, thick, -1, -1, bracket);
            Fill(new Rect(midX - 5f * k, midY - 0.5f * thin, 10f * k, thin), Marks);
            Fill(new Rect(midX - 0.5f * thin, midY - 5f * k, thin, 10f * k), Marks);

            // ---- top left: running, and for how long (the flight's own clock, in hours, minutes, seconds and frames of film)
            float big = 17f * k, small = 12.5f * k, x = frame.xMin + 12f * k, y = frame.yMin + 9f * k;
            Vessel ship = FlightGlobals.ActiveVessel;
            bool stopped = FlightDriver.Pause;
            if (dot == null)
            {
                dot = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                for (int j = 0; j < 16; j++)
                    for (int i = 0; i < 16; i++)
                        dot.SetPixel(i, j, new Color(1f, 1f, 1f, Mathf.Clamp01(7.5f - Mathf.Sqrt((i - 7.5f) * (i - 7.5f) + (j - 7.5f) * (j - 7.5f)))));
                dot.Apply();
            }
            if (!stopped && Time.unscaledTime % 1.2f < 0.8f)
            {
                Color was = GUI.color;
                GUI.color = Red;
                GUI.DrawTexture(new Rect(x, y + 3f * k, 14f * k, 14f * k), dot);
                GUI.color = was;
            }
            x += 20f * k;
            x += Say(x, y, stopped ? "STBY" : "REC", big, stopped ? Marks : Red) + 12f * k;
            double clock = ship != null ? Math.Max(0.0, ship.missionTime) : 0.0;
            int whole = (int)clock;
            Say(x, y, (whole / 3600).ToString("00") + ":" + (whole / 60 % 60).ToString("00") + ":" + (whole % 60).ToString("00") + ":" + ((int)((clock - whole) * 24.0)).ToString("00"), big, Marks);

            // ---- top middle: the picture's size and how many of them a second; under it, what the camera is doing (a move, a path, the cockpit)
            Say(midX, y + 2f * k, Screen.width + " x " + Screen.height + "    " + frames.ToString("F0") + " fps", small, Marks, 0);
            string doing = Doing(false);
            if (doing.Length > 0) Say(midX, y + 21f * k, doing, small * 0.92f, Time.unscaledTime < saidUntil ? Sharp : new Color(1f, 1f, 1f, 0.7f), 0);

            // ---- top right: the ship's batteries, as the camera's own
            if (Time.unscaledTime > chargeAt)
            {
                chargeAt = Time.unscaledTime + 0.5f;
                charge = -1f;
                if (ship != null)
                {
                    ship.GetConnectedResourceTotals(PartResourceLibrary.ElectricityHashcode, out double have, out double holds);
                    if (holds > 0.0) charge = (float)(have / holds);
                }
            }
            if (charge >= 0f)
            {
                float cellWide = 34f * k, cellHigh = 15f * k, cx = frame.xMax - 12f * k - cellWide, cy = y + 2f * k;
                Color level = charge < 0.15f ? Red : charge < 0.35f ? Amber : Marks;
                Fill(new Rect(cx, cy, cellWide, thin), level); Fill(new Rect(cx, cy + cellHigh - thin, cellWide, thin), level);
                Fill(new Rect(cx, cy, thin, cellHigh), level); Fill(new Rect(cx + cellWide - thin, cy, thin, cellHigh), level);
                Fill(new Rect(cx + cellWide, cy + 4f * k, 3f * k, cellHigh - 8f * k), level);
                Fill(new Rect(cx + 2f * thin, cy + 2f * thin, (cellWide - 4f * thin) * Mathf.Clamp01(charge), cellHigh - 4f * thin), level);
                Say(cx - 8f * k, y + 1f * k, (charge * 100f).ToString("F0") + "%", small, level, 1);
            }

            // ---- along the foot: the lens and what it is set to, as a camera lists them
            float open = Mathf.Clamp(360f * frames / shutter.Value, 0f, 360f);
            string[] cells =
            {
                lensNow.ToString("F0") + " mm",
                "f/" + stop.Value.ToString("0.#"),
                "1/" + shutter.Value.ToString("F0"),
                "ISO " + speed.Value.ToString("F0"),
                (focus.Value == 2 ? "MF  " : focus.Value == 1 ? "AF centre  " : "AF ship  ") + Metres(focusAt),
            };
            float footY = frame.yMax - 30f * k, room = frame.width - 60f * k, step = room / cells.Length;
            for (int n = 0; n < cells.Length; n++) Say(frame.xMin + 30f * k + step * (n + 0.5f), footY, cells[n], big, n == 4 && rested ? Sharp : Marks, 0);
            // (above that, the light meter, as a camera's viewfinder shows its own)
            if (meter.Value != 0) Gauge(midX, footY - 13f * k, k, thin, small);
            // (and above that, smaller: what a camera on a drone tells of where it is, and what is wrong if anything is)
            string telling = ship != null ? "HEIGHT " + Metres((float)Math.Max(0.0, ship.radarAltitude)) + "     SPEED " + ship.srfSpeed.ToString("F0") + " m/s     " + Sensors[sensor.Value].ToUpperInvariant() : "";
            Say(midX, footY - (meter.Value != 0 ? 58f : 20f) * k, telling, small * 0.92f, new Color(1f, 1f, 1f, 0.7f), 0);
            if (lens == null) Say(midX, frame.yMin + 50f * k, "NO BLUR: SEE THE CAMERA'S PAGE (" + (Application.platform == RuntimePlatform.OSXPlayer ? "OPTION" : "ALT") + "-K)", small, Amber, 0);
            else if (open >= 359f) Say(midX, frame.yMin + 50f * k, "SHUTTER OPEN ALL FRAME", small * 0.92f, new Color(1f, 1f, 1f, 0.55f), 0);
        }

        /// <summary>
        /// The light meter, drawn as a camera's viewfinder draws its own: a scale of stops from three under to three over, with a
        /// tick at every third of a stop, and a mark under it at what the picture comes to against a middle grey (an arrow at the
        /// end, blinking, when it is further off than the scale goes). `line` is the height of the scale's foot.
        /// </summary>
        void Gauge(float midX, float line, float k, float thin, float small)
        {
            float perStop = 31f * k, left = midX - 3f * perStop, right = midX + 3f * perStop;
            for (int i = -9; i <= 9; i++)
            {
                bool whole = i % 3 == 0;
                float x = midX + i * perStop / 3f, tall = (whole ? 8f : 4f) * k;
                Fill(new Rect(x - 0.5f * thin, line - tall, thin, tall), whole ? Marks : new Color(1f, 1f, 1f, 0.55f));
            }
            for (int s = -3; s <= 3; s++)
                Say(midX + s * perStop, line - 8f * k - 16f * k, s == 0 ? "0" : s == -3 ? "-3" : s == 3 ? "+3" : Mathf.Abs(s).ToString(), small * 0.92f, Marks, 0);
            Say(left - 16f * k, line - 16f * k, meter.Value == 2 ? "SPOT" : "METER", small * 0.9f, new Color(1f, 1f, 1f, 0.6f), 1);
            if (float.IsNaN(metered)) return;
            Color colour = Mathf.Abs(metered) > 2f ? Amber : Marks;
            float size = 10f * k;
            if (Mathf.Abs(metered) > 3f)
            {
                if (Time.unscaledTime % 0.6f < 0.36f)
                    Arrow(metered > 0f ? new Rect(right + 4f * k, line - 6f * k - 0.5f * size, size, size) : new Rect(left - 4f * k - size, line - 6f * k - 0.5f * size, size, size), metered > 0f ? 2 : 1, colour);
            }
            else Arrow(new Rect(midX + metered * perStop - 0.5f * size, line + 2f * k, size, size * 0.8f), 0, colour);
            Say(right + 18f * k, line - 16f * k, Signed(metered), small, colour, -1);
        }

        void Panel()
        {
            GUILayout.Space(4f);
            if (!string.IsNullOrEmpty(readout)) GUILayout.Label("<size=11><color=#b0d8ff>" + readout + "</color></size>", Host.Rich);
            // ---- the path: set here, a point at a time, with the camera put where each should be
            GUILayout.Space(6f);
            GUILayout.Label("<b>Path</b>", Host.Rich);
            Vessel ship = FlightGlobals.ActiveVessel;
            if (!running || ship == null) { GUILayout.Label("<size=11>Switch the camera on, in flight, to set a path for the craft being flown.</size>", Host.Rich); return; }
            GUILayout.Label("<size=11>" + PathSaid() + "</size>", Host.Rich);
            // (in two short rows of buttons of their own width, from the left: a page wider than the window, as this one is with its widest
            // choices, would otherwise spread them out of sight)
            bool could = GUI.enabled;
            GUILayout.BeginHorizontal();
            GUI.enabled = could && !inside && holding != Fly && path.Points.Count < CinePath.Most;
            if (GUILayout.Button("Put a point here", GUILayout.Width(150f))) told = PutPoint();
            GUI.enabled = could && !inside && (path.Points.Count > 0 || holding == Fly);
            if (GUILayout.Button(holding == Fly ? "Stop" : "Fly it", GUILayout.Width(110f))) move.Value = holding == Fly ? Still : Fly;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUI.enabled = could && holding != Fly && path.Points.Count > 0;
            if (GUILayout.Button("Take the last away", GUILayout.Width(150f))) told = TakeLast();
            if (GUILayout.Button("Clear", GUILayout.Width(110f))) told = ClearPath();
            GUI.enabled = could;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(told)) GUILayout.Label("<size=11><color=#a0a0a0>" + told + "</color></size>", Host.Rich);
        }

#if DEV
        static Cine Running()
        {
            foreach (Cine one in FindObjectsOfType<Cine>()) return one;
            return null;
        }

        /// <summary>For the development build: go over to the other lens where there are two (Keystone's own and TUFX's), and back.</summary>
        public static string OtherLens()
        {
            Lens.PreferOther = !Lens.PreferOther;
            return "preferring " + (Lens.PreferOther ? "the other lens (" + (Lens.Other != null ? Lens.Other.Name : "there is none") + ")" : "Keystone's own");
        }

        /// <summary>For the development build: the lamps' flares (since 0.7.0): how many lamps are known, in sight, flared.</summary>
        public static string FlaresSaid() => Flares.Said;
        /// <summary>For the development build: every lamp the flares know, where it is from the craft and which way it shines, and how much of it is shown.</summary>
        public static string FlaresLamps() => Flares.Lamps(FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main);
        /// <summary>For the development build: a test flare in the middle of the picture, "on" or "off", to see the drawing alone; and which of the camera's passes come after it.</summary>
        public static string FlaresTest(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a == "effects" || a == "alpha") { Flares.When = a == "effects" ? UnityEngine.Rendering.CameraEvent.AfterImageEffects : UnityEngine.Rendering.CameraEvent.AfterForwardAlpha; return "flares drawn " + Flares.When; }
            if (a == "object" || a == "drawmesh") { Flares.ByObject = a == "object"; Flares.Off(); return "flares drawn by " + (Flares.ByObject ? "an object of their own" : "Graphics.DrawMesh"); }
            if (a.StartsWith("shader:")) return Flares.Remake(a.Substring(7).Trim());
            if (a == "material" || a == "white" || a == "nomips" || a == "picture")
            {
                Material m = Flares.Glow;
                if (m == null) return "the flares have no material yet";
                if (a == "white") m.mainTexture = Texture2D.whiteTexture;
                else if (a == "nomips") m.mainTexture = Flares.Picture(false);
                else if (a == "picture") m.mainTexture = Flares.Picture(true);
                var tex = m.mainTexture as Texture2D;
                return m.shader.name + ", queue " + m.renderQueue + ", keywords [" + string.Join(" ", m.shaderKeywords) + "], _MainTex " + (tex != null ? tex.name + " " + tex.width + "x" + tex.height + " " + tex.format + " mips " + tex.mipmapCount + " " + tex.filterMode + " " + tex.wrapMode : "none")
                    + ", scale " + m.mainTextureScale.ToString("F2") + " offset " + m.mainTextureOffset.ToString("F2") + (m.HasProperty("_TintColor") ? ", tint " + m.GetColor("_TintColor") : "") + (m.HasProperty("_InvFade") ? ", invfade " + m.GetFloat("_InvFade") : "")
                    + "; the texture limit " + QualitySettings.masterTextureLimit;
            }
            if (a.StartsWith("dist:")) { float.TryParse(a.Substring(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Flares.PlaneAt); return "the flares laid " + (Flares.PlaneAt > 0f ? Flares.PlaneAt + " m before the lens" : "just past the lens's nearest reach"); }
            if (a == "cube" || a == "nocube" || a.StartsWith("cube:"))
            {
                // (the bluntest test there is: a plain bright cube held 2 m before the flight camera's lens, every frame; if even
                // this does not show, the picture seen is not the one that camera draws. "cube:<shader>": the cube in white with
                // that shader, made as the flares' material is; "cube:flares": with the flares' own material)
                GameObject old = GameObject.Find("Cinema Camera test cube");
                if (old != null) Destroy(old);
                if (a == "nocube") return "test cube taken away";
                string named = a.StartsWith("cube:") ? a.Substring(5).Trim() : null;
                Material paint;
                string with;
                if (named == "flares")
                {
                    paint = Flares.Glow;
                    if (paint == null) return "the flares have no material yet (Cinema Camera on, flares above 0)";
                    with = "the flares' own material (" + paint.shader.name + ")";
                }
                else if (named != null)
                {
                    Shader one = Shader.Find(named);
                    if (one == null) return "no shader " + named;
                    paint = new Material(one);
                    if (paint.HasProperty("_TintColor")) paint.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                    if (paint.HasProperty("_Color")) paint.SetColor("_Color", Color.white);
                    with = one.name + (one.isSupported ? "" : ", not supported") + ", queue " + paint.renderQueue;
                }
                else
                {
                    Shader plain = Shader.Find("Unlit/Color") ?? Shader.Find("KSP/Unlit");
                    if (plain == null) return "no plain shader";
                    paint = new Material(plain);
                    if (paint.HasProperty("_Color")) paint.SetColor("_Color", new Color(1f, 0.2f, 0.8f));
                    with = plain.name;
                }
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Cinema Camera test cube";
                Destroy(cube.GetComponent<Collider>());
                cube.layer = 0;
                cube.transform.localScale = Vector3.one * 0.3f;
                cube.GetComponent<Renderer>().sharedMaterial = paint;
                cube.AddComponent<HeldBeforeLens>();
                Camera cam0 = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
                return "test cube held 2 m before the lens (" + with + ")" + (cam0 != null ? "; " + cam0.name + " near " + cam0.nearClipPlane.ToString("F2") + " far " + cam0.farClipPlane.ToString("F0") + ", layer 0 " + ((cam0.cullingMask & 1) != 0 ? "drawn" : "NOT drawn") + ", soft particles " + QualitySettings.softParticles : "");
            }
            Flares.TestFlare = a == "on";
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            var say = new System.Text.StringBuilder("test flare " + (Flares.TestFlare ? "on" : "off"));
            if (cam != null)
            {
                say.Append("; ").Append(cam.name).Append(", hdr ").Append(cam.allowHDR).Append(", target ").Append(cam.targetTexture != null ? cam.targetTexture.name : "the screen").Append("; its passes:");
                foreach (UnityEngine.Rendering.CameraEvent when in Enum.GetValues(typeof(UnityEngine.Rendering.CameraEvent)))
                    foreach (UnityEngine.Rendering.CommandBuffer b in cam.GetCommandBuffers(when)) say.Append(" [").Append(when).Append(": ").Append(b.name).Append(']');
                say.Append("; on it:");
                foreach (Component c in cam.GetComponents<Component>()) say.Append(' ').Append(c.GetType().Name);
            }
            return say.ToString();
        }

        /// <summary>For the development build: what the camera is doing, in a line.</summary>
        /// <summary>(Development build: the ship and its target, where each falls in the picture (0 to 1 across and up), how far apart they
        /// are, and where the camera stands from the ship.)</summary>
        public static string Pairing()
        {
            Cine one = Running();
            Vessel ship = FlightGlobals.ActiveVessel;
            if (one == null || ship == null || FlightCamera.fetch == null) return "not in flight";
            if (!one.PairTarget(ship, out Vector3d target, out string what)) return "no target";
            Camera c = FlightCamera.fetch.mainCamera;
            Vector3 s = c.WorldToViewportPoint(ship.CoM), t = c.WorldToViewportPoint((Vector3)target);
            return "holding " + Moves[one.holding] + "; " + what + " " + Metres((float)(target - ship.CoMD).magnitude) + " from the ship; the camera " + Metres(Vector3.Distance(c.transform.position, ship.CoM)) + " from the ship, its view " + c.fieldOfView.ToString("F1") + " degrees high; the ship at " + s.x.ToString("F2") + ", " + s.y.ToString("F2") + (s.z < 0f ? " (behind)" : "") + ", the target at " + t.x.ToString("F2") + ", " + t.y.ToString("F2") + (t.z < 0f ? " (behind)" : "") + "; the ship " + Length(ship, 1.5f, 0.4f).ToString("F1") + " m long, as framed";
        }

        public static string Stats()
        {
            Cine one = Running();
            if (one == null) return "not in flight";
            return (one.running ? "running (" + (one.lens == null ? "no lens" : one.lens.Name + "'s lens") + "): " : "not running: ") + one.readout + " | on " + on.Value + ", f/" + stop.Value + ", focus " + focus.Options[focus.Value] + ", bars " + bars.Options[bars.Value]
                   + ", interface hidden " + one.hidden + " | move " + move.Options[move.Value] + ", holding " + one.holding + ", far " + one.far.ToString("F1") + ", drawn " + one.drawn.ToString("F2") + ", lens " + one.lensNow.ToString("F1")
                   + " | path for \"" + one.pathFor + "\": " + one.path.Points.Count + " points, " + one.path.Turns + ", flown " + one.pathTime.ToString("F2") + " s | " + one.gauge.Said + ", shown " + (float.IsNaN(one.metered) ? "none" : one.metered.ToString("F2"))
                   + " | inside " + one.inside + ", keys held off " + one.locked + " | " + one.within.Said + ", focus at " + one.focusAt.ToString("F3")
                   + (FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null ? ", the near camera's far end " + FlightCamera.fetch.mainCamera.farClipPlane.ToString("F0") : "")
                   + ", roar " + one.roar.ToString("F2");
        }

        /// <summary>For the development build: put a point of the path where the camera is (as the page's button does).</summary>
        public static string Point() { Cine one = Running(); return one == null ? "not in flight" : one.PutPoint(); }

        /// <summary>For the development build: the path cleared (as the page's button does), and so taken out of the settings file.</summary>
        public static string Unpoint() { Cine one = Running(); return one == null ? "not in flight" : one.ClearPath(); }

        /// <summary>For the development build: the meter's small picture, as it was last read, written to a file.</summary>
        public static string MeterPicture(string path) { Cine one = Running(); return one == null ? "not in flight" : one.gauge.Save(path); }

        /// <summary>For the development build: the path as it is kept.</summary>
        public static string Kept() { Cine one = Running(); return one == null ? "not in flight" : CinePath.KeyFor(one.pathFor) + " = " + (mod.Kept(CinePath.KeyFor(one.pathFor)) ?? "(nothing)"); }

        /// <summary>For the development build: into the cockpit, or out again (the game's own C key).</summary>
        public static string CockpitSaid() { Cine one = Running(); return one == null ? "not running" : one.within.Said; }

        public static string Iva()
        {
            CameraManager cameras = CameraManager.Instance;
            if (cameras == null) return "no camera manager";
            if (Inside()) { cameras.SetCameraFlight(); return "outside"; }
            return cameras.SetCameraIVA() ? "in the cockpit" : "the game would not go into the cockpit";
        }

        /// <summary>For the development build: the cockpit's view turned (the kerbal's head), as "pitch,turn" in degrees (the game allows -30 to 60 and -60 to 60).</summary>
        public static string Look(string arg)
        {
            InternalCamera inner = InternalCamera.Instance;
            if (inner == null) return "no cockpit camera";
            string[] parts = (arg ?? "0,0").Split(',');
            float pitch = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), turn = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0f;
            Reach.Field(typeof(InternalCamera), "currentPitch")?.SetValue(inner, pitch);
            Reach.Field(typeof(InternalCamera), "currentRot")?.SetValue(inner, turn);
            return "cockpit view turned to pitch " + pitch + ", turn " + turn;
        }

        /// <summary>For the development build: the settings window scrolled to the foot of the page (to see the path's buttons).</summary>
        public static string Foot()
        {
            if (Host.Instance == null) return "no window";
            System.Reflection.FieldInfo scroll = Reach.Field(typeof(Host), "scroll");
            if (scroll == null) return "the window has no scroll";
            scroll.SetValue(Host.Instance, new Vector2(0f, 100000f));
            return "scrolled to the foot";
        }

        /// <summary>For the development build: the game's own camera put in one of its modes (AUTO, FREE, ORBITAL, CHASE, LOCKED).</summary>
        public static string Mode(string name)
        {
            FlightCamera camera = FlightCamera.fetch;
            if (camera == null) return "no flight camera";
            FlightCamera.SetModeImmediate((FlightCamera.Modes)Enum.Parse(typeof(FlightCamera.Modes), name, true));
            return "mode " + camera.mode + ", auto " + camera.autoMode;
        }

        /// <summary>For the development build: the cameras as they are now (where the rig and the picture's camera are, the cockpit's camera, what the middle of the cockpit hits, the clocks).</summary>
        public static string Rig()
        {
            FlightCamera camera = FlightCamera.fetch;
            if (camera == null) return "no flight camera";
            var o = new System.Text.StringBuilder();
            o.Append("rig at " + camera.transform.position.ToString("F2") + ", picture's camera " + (camera.mainCamera.transform.position - camera.transform.position).magnitude.ToString("F3") + " m from it, turned " + Quaternion.Angle(camera.mainCamera.transform.rotation, camera.transform.rotation).ToString("F2") + "°; updating " + camera.updateActive + ", mode " + camera.mode + ", fov " + camera.FieldOfView.ToString("F2"));
            o.Append("; capture " + Time.captureFramerate + ", unscaled step " + Time.unscaledDeltaTime.ToString("F4") + ", step " + Time.deltaTime.ToString("F4"));
            Camera cockpit = Cockpit();
            CameraManager cameras = CameraManager.Instance;
            o.Append("; camera mode " + (cameras != null ? cameras.currentCameraMode.ToString() : "?") + ", flight fov kept " + (cameras != null ? cameras.existingFlightFoV.ToString("F2") : "?"));
            if (cockpit != null)
            {
                o.Append("; cockpit camera " + cockpit.name + " fov " + cockpit.fieldOfView.ToString("F2") + " near " + cockpit.nearClipPlane + " far " + cockpit.farClipPlane + " hdr " + cockpit.allowHDR + " depth " + cockpit.depthTextureMode + " mask " + cockpit.cullingMask + " clear " + cockpit.clearFlags + " depth order " + cockpit.depth);
                foreach (UnityEngine.Rendering.CameraEvent when in new[] { UnityEngine.Rendering.CameraEvent.AfterImageEffects, UnityEngine.Rendering.CameraEvent.AfterForwardAlpha })
                    foreach (UnityEngine.Rendering.CommandBuffer b in cockpit.GetCommandBuffers(when)) o.Append(", buffer at " + when + ": " + b.name);
                foreach (Component c in cockpit.GetComponents<Component>()) o.Append(", " + c.GetType().Name);
                if (Physics.Raycast(cockpit.transform.position, cockpit.transform.forward, out RaycastHit hit, 30f, (1 << 16) | (1 << 20), QueryTriggerInteraction.Collide))
                    o.Append("; middle of the cockpit: " + hit.collider.name + " on layer " + hit.collider.gameObject.layer + (hit.collider.isTrigger ? " (trigger)" : "") + " at " + hit.distance.ToString("F2") + " m");
                else o.Append("; nothing inside in the middle");
            }
            foreach (Camera c in Camera.allCameras) if (c.targetTexture == null) o.Append("\n  " + c.name + " depth " + c.depth + " fov " + c.fieldOfView.ToString("F1") + " hdr " + c.allowHDR + " enabled " + c.enabled + " post " + (c.GetComponent("PostProcessLayer") != null));
            return o.ToString();
        }
#endif
    }

#if DEV
    /// <summary>(For the development build: Cine.FlaresTest "cube".) Keeps its object 2 m before the flight camera's lens, every frame.</summary>
    sealed class HeldBeforeLens : MonoBehaviour
    {
        void LateUpdate()
        {
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (cam == null) return;
            transform.position = cam.transform.position + cam.transform.forward * 2f;
            transform.rotation = cam.transform.rotation;
        }
    }
#endif
}
