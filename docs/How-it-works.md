# How it works

Every frame the camera's settings are turned into what a lens does, and handed to Keystone's lens (see
[Keystone's lens](https://github.com/IshiakiZ/ksp-keystone/wiki/The-lens)), which draws the game's finished picture again through it.
The angle of view is `2 atan(sensor height / (2 x focal length))`. Something at a distance `d`, with the
lens focused at `s`, is drawn as a disc `f² / (N (s - f)) x |d - s| / d` wide on the film (`f` the focal
length, `N` the f-number), which is what a real lens does; the shutter becomes the part of each frame it
is open for, and whatever moved in that time is smeared along the way it went.

## Moves

A move that holds the camera (staying, drifting, a path) stops the game's own camera from placing itself
and puts it somewhere every frame instead:

* **staying**: at a place fixed to the ground (a latitude, a longitude and a height, worked out again every
  frame, since the game slides its whole world about under a moving ship);
* **drifting**: on an orbit of its own, made when the move begins from where the camera is and the ship's
  velocity, the way the game makes the ship's own orbit from where it is and how fast it goes; every frame
  the camera is put where that orbit has it at the game's time, as the game does for anything on rails.
  The camera keeps the roll it had (in space there is no level to come back to) and turns after the ship
  the more closely the narrower its lens sees;
* **a path**: at the place, aim and lens the path has after so much of its time, in the path's frame as it
  is for the ship just then.

The game's own camera gets the camera back when the move ends, and eases from there to where it had been.

## Paths

The points are kept relative to the ship's middle, in the frame the game's own camera was keeping to when
the first point was put (north and up in its free camera, the stars in its orbital one, the ship in its
locked one, the ship's heading in its chase one). Between points the camera follows a centripetal
Catmull-Rom curve through them, which passes through each point and neither overshoots nor loops. Each
point's aim is taken against looking at the ship's middle from that point, and that is what goes along
the curve: points that all look at the ship give a path that looks at it all the way round. The lens goes
from point to point evenly in stops of the angle of view. The path's time is shared equally between the
gaps, gathering speed over its first fifth and slowing over its last.

## The light meter

While the viewfinder is up, five times a second the finished picture (after the film and the lens) is
copied and halved on the graphics card until it is a few thousand dots, and those are read back two frames
later, when the card has long finished with them. Each dot's brightness, as light (the shown value to the
power 2.2), is weighed, the middle of the picture most (or only the focus bracket, for spot), and the
reading is how many stops that is from a middle grey (18% of white). When the camera sets the exposure
itself, a change of aperture, shutter or film counts at once, before the next reading.

With the camera on when a flight begins, the viewfinder goes up in the scene's first frames, and the game
then shows its new scene's interface by itself, without the event mods are told by: the game's interface and
the viewfinder were drawn together (seen on Windows). The camera now puts the interface away again whenever it
is showing while the viewfinder is up (once a second at most); F2 still takes the viewfinder down, as before.

## The cockpit

The game draws the cockpit's inside with a camera of its own, after the view through the windows. The lens
and the meter are put on that camera, so that the whole picture goes through them; its angle of view is
read as the focal length; and the focus is found among the cockpit's own pieces, which mostly have nothing
a ray can hit: a ray from the kerbal's eye is tried against the boxes round them, and then against the
triangles of the few it goes into.
