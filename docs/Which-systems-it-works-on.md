# Which systems it works on

* **Mac:** made and tried here (macOS, OpenGL), with TUFX installed and with TUFX taken out.
* **Linux:** the game runs on OpenGL there too, so the same lens should work. Not tried.
* **Windows:** the game runs on Direct3D 11 there, and Keystone's lens has a shader built for Direct3D
  (`Keystone/PluginData/lens-windows.bundle`, made on Windows without the Unity editor). Tried on Windows 11
  (KSP 1.12.5, an NVIDIA GeForce RTX 4080 SUPER) on 2026-10-10, without TUFX: the camera switched on from
  its page, the view narrowed to its 50 mm lens with the focus on the ship (the game's log: "has a lens of
  its own (built for Direct3D11, from lens-windows.bundle)"), the viewfinder with its light meter, the bars,
  and the camera taken round the ship. Where Keystone's lens cannot work (a copy of Keystone without that
  shader), the camera asks the same of **[TUFX](https://github.com/KSPModStewards/TUFX)** 1.1 or later if
  that is installed, by way of `KeystoneTUFX.dll`, which comes in the `Keystone` folder and which the game
  loads only where TUFX is; without either, the lens's angle of view, the focus readout and the bars work,
  and nothing is blurred, and the camera's page says so. TUFX on Windows was tried later that day: its lens
  taken in place of Keystone's (the development build's `OtherLens`): "running (TUFX's lens)", "(The blur, the
  smear and the grain are being done by TUFX.)"; at 135 mm and f/2 with the focus at 300 m the ship 21 m off went
  soft through both, more through TUFX's than through Keystone's at the same settings; and on the release build
  started without `lens-windows.bundle`, the camera took TUFX's by itself and said so on its page.

The moves, the paths, the viewfinder and its light meter need no shader of their own (the meter shrinks
the picture with the game's own copying and reads it back), so they should work wherever the game does;
they were tried on a Mac (version 0.6.0, 2026-10-09; not with TUFX taken out) and the viewfinder, the
meter and the move round the ship on Windows (above), and there later a path of four points round the Jumping
Flea's capsule, 25 to 40 m off, flown in ten seconds. In the cockpit the lens is the same lens as outside,
and works or not as it does there; done by TUFX, the cockpit's picture is blurred too (tried on Windows: the
Mk1 pod's panel, its near switches sharp and the gauges further off soft, through each lens).
