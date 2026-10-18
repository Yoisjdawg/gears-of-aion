#!/bin/sh
# Screenshots of every district's island and its edge into the Abyss: media/tour/<district>-island.png, -edge.png,
# and a contact sheet media/tour-sheet.png. Needs ffmpeg and a built game.
cd "$(dirname "$0")/.."
OUT=media/tour; mkdir -p "$OUT"
W=$(cygpath -w "$PWD/$OUT")
for m in downtown historic industrial; do
  for view in island edge; do
    if [ "$view" = island ]; then cam="-230,75,0,0,-10,0"; else cam="0,14,40,0,-2,140"; fi
    DAEVAS_NO_TEST=1 DAEVAS_MAP=$m DAEVAS_CAM=$cam timeout 300 build/game/GearsOfAion.exe --mute --no-hud --start-screen playing \
      --class templar --shot "$W\\$m-$view.png" --shot-after 16 --quit-after 22 > /dev/null 2>&1 < /dev/null
  done
done
cd "$OUT"
ffmpeg -nostdin -y -loglevel error -i downtown-island.png -i historic-island.png -i industrial-island.png \
  -i downtown-edge.png -i historic-edge.png -i industrial-edge.png -filter_complex \
  "[0]scale=640:-2[a];[1]scale=640:-2[b];[2]scale=640:-2[c];[3]scale=640:-2[d];[4]scale=640:-2[e];[5]scale=640:-2[f];[a][b][c]hstack=3[t];[d][e][f]hstack=3[u];[t][u]vstack" ../tour-sheet.png
echo "tour in $OUT"
