# PivotTool
## Description
This tool extends transform functionality, allowing for temporary custom pivots for objects and groups of objects, without the hassle involved with creating empty GameObjects. The pivot can be repositioned and rotated independent of the selected objects.
## Instructions
## Hotkeys
Hotkeys make use of the Shortcut API, so the following are rebindable via *Edit->Shortcuts*.
* **P** : Hold to move/rotate the pivot point
* **S** : Hold and click in the scene to snap the pivot to the clicked point (tests against geometry)
## Todo
* Keyboard entry for position, rotation, scale
  * Should respect `adjustPivot` mode
  * Probably just a GUI overlay like the adjust/snap pivot indicators
* Respect local/global pivot mode
  * Currently only uses local
* Save/restore pivots?
  * Either via bookmarks, or automatically by selection
* Create concrete pivot on demand via empty GameObject
  * Could be tricky to manage in the sense that a user could easily end up with a ridiculous chain of empties if not careful
