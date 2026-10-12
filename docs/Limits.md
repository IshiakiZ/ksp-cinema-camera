# Limits

* **Exposure by the camera needs TUFX** (it is done through Keystone's film, which TUFX does), and is
  rough: the game's picture is not worked in real units, so "what full sun wants" is simply the game's own
  daylight picture, taken as f/16 at 1/100 s on ISO 100. Tried by day on the pad and in orbit; not by
  night.
* **The light meter** reads the finished picture as it is shown, so it cannot tell more than about two and
  a half stops over (white is as bright as a picture gets); beyond three either way the viewfinder shows
  an arrow. Like a real meter, a centre-weighted reading is fooled by black space round a sunlit ship (it
  read -3.8 for a well-lit capsule against the stars, where the spot reading on the capsule was -0.7) and
  by a bright sky. Checked against the pictures themselves on the pad (-1.3 both), in orbit and in the
  cockpit (within 0.1 of a stop each time).
* **Moves.** While the camera stays or drifts, the game's own camera keys and mouse do nothing to it (its
  lens ring still works), and the move ends by itself when the ship is 250 km off or goes round another
  world. Any camera left behind sees a ship that has gone far, compared with how far off the camera was,
  from behind: a ship burning away from a camera 30 m off is seen end-on within a few seconds. For a ship
  seen side-on as it goes, leave the camera a few hundred metres to one side of the way it will go.
  Drifting was tried in orbit round Kerbin (105 to 250 km) with burns of 8 to 12 seconds and through five
  minutes of time warp; staying, on the pad. Nobody has pressed the keys: they were tried through the game's remote control.
* **Paths.** Up to twelve points, the same time from each to the next; a path whose points look at the
  ship looks at it all the way, but one that passes right over or under the ship turns quickly there
  (straight up there is no level to keep). A path is kept under the craft's name: two craft of one name
  share it. Paths were tried round a rocket on the pad and round a capsule in orbit, recorded frame by
  frame; not in flight through the air, not with a plane, not in time warp.
* **The cockpit.** The lens and the meter are put in front of the cockpit's own camera, its zoom is the
  focal length, and the focus finds the piece of the cockpit in the middle of the picture (the Mk1 pod's
  instrument panel is 20 cm from the kerbal's eye). Focusing out through a window was not tried (the Mk1
  pod's view cannot be turned up far enough to put the middle on its window). The cockpit's camera knows
  how far off only the cockpit's own insides are: what is seen through a window is taken as far away, so it is blurred as a far
  background is when the focus is inside, and sharp when it is outside. Nothing seen through a window is
  smeared by the shutter, as the cockpit's camera sees it standing still. The bend of a wide lens is left
  out there, so that the cockpit's switches are clicked where they are seen. Moves end when the cockpit is
  entered. Tried in the Mk1 pod in orbit only.
* **Keys.** The game reads its own keys whether the modifier is held or not, so while the camera is
  running and the modifier is held, the game's own SAS keys (`T`, `F`) and camera-mode keys (`C`, `V`)
  are held off; the SAS button on the screen looks locked for that moment. With the camera off, the
  modifier and `C` still also show or hide the crew's portraits.
* The game draws the distance with a second camera, and everything beyond about 400 m counts as being at
  400 m for the blur. With the focus nearer than that it makes no difference to speak of.
* The lens works on the picture once it is finished, so it knows one distance for each pixel: something
  see-through (smoke, a flame, a window) is blurred as whatever is seen through it is.
* With few looks along the way, a strong smear of something with hard edges shows as a row of faint
  copies in a still picture (a lattice tower, with the camera swung round at 50 degrees a second and the
  shutter open for the whole frame).
* Through a very long lens (some hundreds of millimetres and more) the game's own sky of stars shows how
  coarse its picture is: the stars become soft blobs.
* **What it costs** could not be told from nothing (2026-10-07, an Apple M5 Pro at 1280 x 720, on the
  launch pad at about 67 frames a second): with the blur at small, medium and large, the smear, the
  lens's character and grain switched on in turn and together, each frame took 14.8 to 14.9 ms, and 14.9
  with the camera off (one such reading differs from the next by 0.15 ms). On a slower graphics card it
  will show: the blur is the dearest part, and `Blur quality` is what to lower. The meter costs five
  small copies of the picture five times a second, while the viewfinder is up; it was not measured.
* It switches itself off in the map.
