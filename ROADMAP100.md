# Gears of Aion: the next 100

The creator asked (2026-10-17) to fix the floating buildings, see-through walls, the see-through vehicle and the piles
that couldn't be walked on, to hide the sky under the district's edge with the Abyss, and then to make a new 100-item
roadmap and start on it. [x] = done. `python -I tools/roadmap.py done 7 8` marks items; `todo [section]` lists them.
Progress: 100 of 100 done.

## The Abyss around the district
1. [x] The district floats on an Abyss island: a ring of Gears cliff faces hanging from its edge, a rock underside to a point
2. [x] A violet void below the horizon, with drifting nebulae and stars
3. [x] Floating rock islands (Gears cliffs on top) all around, above and below
4. [x] The island's rim follows the district's footprint (cliffs start right behind the last buildings)
5. [x] The distant ring of half-placed cells and the painted skyline are gone (no more floating far buildings)
6. [x] Flight stops at the edge of the district ("The Siege lines end here")
7. [x] A broken street edge along the rim: a rubble lip, cracked asphalt and bent rebar where the city tore away
8. [x] Aether motes and Abyss wind drifting up past the rim
9. [x] The floating islands drift and turn slowly
10. [x] Distant Abyss landmarks: a ruined fortress silhouette and the glow of the Abyss core
11. [x] Violet light from below on the cliffs and the island's underside
12. [x] Chunks crumbling off the rim now and then, falling into the void
13. [x] A shimmer where flight meets the district's edge
14. [x] The night Abyss: brighter nebulae, glowing crystals in the cliffs
15. [x] The underside's detail: hanging pipes, roots and sewer sections torn out with the district

## The city, fixed
16. [x] See-through walls fixed: each material's alpha is opaque, cut-out or blended, read from the texture itself
17. [x] City walls drawn from both sides (single-sided Gears panels no longer vanish from behind)
18. [x] Glass and grime overlays stay see-through; masks (wire, broken edges) cut out crisply
19. [x] Floating pieces inside the district: find them (DAEVAS_FLOATCHECK) and settle or drop them
20. [x] Gaps between buildings you can see through to the void: fill them
21. [x] Pieces still drawn black or untextured: find their Gears textures
22. [x] Window glass: a dark reflective tint instead of flat grey
23. [x] Open shopfronts: a dark interior behind the glass so nothing shows through
24. [x] Where road and sidewalk textures meet: a softer edge
25. [x] Rooftop ladders and stairs checked for collision

## Collision and movement
26. [x] Rubble, debris and dirt piles are walkable mounds (in the city and the street dressing)
27. [x] The Locust stand on the mounds too
28. [x] Wrecked vehicles: colliders shaped to the wreck, not one big box
29. [x] Climb onto wrecks and walk on their roofs
30. [x] City colliders from the mesh's shape (angled and L-shaped walls)
31. [x] The city's stairs and ramps walkable
32. [x] Never stuck in a corner (a nudge out after a moment)
33. [x] Locust pathing that prefers flat ground over mounds and goes round wrecks
34. [x] The camera never sinks into mounds or wrecks
35. [x] A landing roll after a long drop from a roof

## Vehicles and props
36. [x] Props keep every triangle (big sections are split, nothing is dropped)
37. [x] Sharper prop textures (1024 px)
38. [x] Wrecks built from their full Gears part meshes where they exist (doors, wheels, hoods)
39. [x] Burning wrecks: smoke and embers from their engines
40. [x] Blasts knock wreck doors and hoods loose
41. [x] More street dressing from the Gears install: crates, carts, newspaper stands
42. [x] Props sit on the real street height (no hovering on slopes)

## Speed and stability
43. [x] Memory: about 2.2 GB in play (it was 4-6 GB and more)
44. [x] One copy at a time: a second launch brings the first to the front
45. [x] The window keeps responding while the city loads
46. [x] The log survives a busy log folder, notes memory every 30 s, and reports what's held past 6 GB
47. [x] Loading: the city built on several threads
48. [x] A texture memory budget for each graphics preset
49. [x] No frame hitches while textures stream in (uploads spread across frames)
50. [x] A safe-mode start (low preset, no streaming) offered after a crash
51. [x] The last crash shown on the next start, with the log folder one click away
52. [x] Benchmark the Abyss on low, medium and high

## Flight and wings
53. [x] Wings after AION 2's Daeva wings: great white feathered wings, sized so the hero stays in view
54. [x] Wings fold against the back on the ground and open for flight
55. [x] Gliding: hold Space in the air to glide down slowly
56. [x] Wing sprint with a feather trail
57. [x] A diving attack onto the Locust from the air
58. [x] Grab a ledge when a jump falls just short of a roof
59. [x] The wingbeat follows flight: faster climbing, gliding still
60. [x] Folded wings sway as the hero runs
61. [x] A wing colour choice: Elyos white or Asmodian dark
62. [x] A soft shadow from the wings on the hero's back
63. [x] Wings cast shadows in sunlight
64. [x] A sweeping open as the wings unfold for take-off
65. [x] Feathers knocked loose when hit in the air

## Gameplay
66. [x] Locust climbing up over the rim from the void (a new way for waves to arrive)
67. [x] Aether rifts: glowing tears in the air that bring elites
68. [x] Aether shards scattered on rooftops to collect in flight
69. [x] An Abyss storm wave modifier: lightning from below the rim
70. [x] Daily challenge seeds
71. [x] An endless mode on the island
72. [x] A rescue mode: COG soldiers to keep alive
73. [x] Rooftop vantage points with bonus damage
74. [x] Wave intros that show where the Locust are coming from
75. [x] Combo finishers for each class

## Look and feel
76. [x] The title screen behind the menus: the island drifting in the void, in 3D
77. [x] Hit sparks and dust that match the surface
78. [x] Puddles that reflect the sky
79. [x] Fog tuned for each district
80. [x] Colour grading choices
81. [x] Ash and embers falling over the streets
82. [x] Street lamps that flicker and spark
83. [x] Bullet holes and scorch marks that stay on walls

## Menus and settings
84. [x] A wing size setting (for visibility)
85. [x] A camera distance setting
86. [x] The controls shown on the first start
87. [x] A pause menu: resume, options, quit
88. [x] Controller rumble
89. [x] A subtitle size setting
90. [x] Colourblind-safe enemy markers
91. [x] The minimap shows the island's edge
92. [x] Loading tips about the Abyss

## Testing and release
93. [x] Testing with real launches: a visible window and sound, as the creator starts it
94. [x] A real-launch smoke test script with a memory watchdog (tools/smoke.ps1)
95. [x] The movement self-test covers mounds, wrecks and stairs
96. [x] A screenshot tour of the island's edge for every district
97. [x] Capsule art from a good camera spot, with the Abyss
98. [x] The trailer re-cut with the new name, wings and the Abyss
99. [x] README, REMIX and the version 0.9.0 notes
100. [x] Publish (only with the creator's yes)
