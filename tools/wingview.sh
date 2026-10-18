#!/bin/sh
# Dev: the Daeva wings on a hero in the character viewer, front and back, side by side: build/wingview.png. Needs ffmpeg.
# Usage: sh tools/wingview.sh [spread 0..1] [extra yaw]
cd "$(dirname "$0")/.."
OUT=build/wingview; mkdir -p "$OUT"
for yaw in 0 180 ${2:-}; do
  DAEVAS_NO_TEST=1 DAEVAS_HIDE=1 DAEVAS_VIEWER=marcus DAEVAS_VIEWERWINGS=${1:-1} timeout 120 build/game/GearsOfAion.exe --mute --size 1280x720 \
    --viewer Idle --viewer-yaw $yaw --shot "$(cygpath -w "$PWD/$OUT/$yaw.png")" --shot-screen viewer --shot-after 6 > /dev/null 2>&1 < /dev/null &
done
wait
ffmpeg -nostdin -y -loglevel error -i "$OUT/0.png" -i "$OUT/180.png" -filter_complex hstack build/wingview.png && echo build/wingview.png
