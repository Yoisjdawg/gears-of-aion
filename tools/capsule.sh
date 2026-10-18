#!/bin/sh
# Capsule art: the district's island floating in the Abyss (no HUD) under the logo, in store sizes:
# build/capsule/capsule-2x.png (1232x706), capsule-616x353.png and header-460x215.png. Needs ffmpeg.
# Usage: sh tools/capsule.sh [class]
set -e
cd "$(dirname "$0")/.."
OUT=build/capsule
CLS=${1:-sorcerer}
FF="${DAEVAS_FFMPEG:-ffmpeg} -nostdin"
FONT="C\\:/Windows/Fonts/segoeuib.ttf"
mkdir -p "$OUT"
rm -f "$OUT/hero.png"
# the district's island in the Abyss, seen from the void (the camera aimed with DAEVAS_CAM)
DAEVAS_NO_TEST=1 DAEVAS_CAM="${CAPSULE_CAM:--185,52,135,0,-12,0}" timeout 180 build/game/GearsOfAion.exe --background --mute --no-hud --size 1920x1080 --class "$CLS" \
  --start-screen playing --shot "$(cygpath -w "$PWD/$OUT/hero.png")" --shot-after 20 --quit-after 26 > "$OUT/hero.log" 2>&1 < /dev/null || true
[ -s "$OUT/hero.png" ] || { echo "no frame (see $OUT/hero.log)"; exit 1; }
DAEVAS_NO_TEST=1 timeout 60 build/game/GearsOfAion.exe --background --mute --logo "$(cygpath -w "$PWD/$OUT/logo.png")" > /dev/null 2>&1 < /dev/null || true
[ -s "$OUT/logo.png" ] || { echo "no logo"; exit 1; }
# the logo (wings, cog and name; 1024 square, drawn around x 30..994, y 250..830) over the top of the frame
$FF -y -loglevel error -i "$OUT/hero.png" -i "$OUT/logo.png" -filter_complex "[0]scale=1256:706,crop=1232:706,drawbox=x=0:y=0:w=iw:h=ih*0.42:color=black@0.40:t=fill[bg];[1]crop=964:580:30:250,scale=-1:250[logo];[bg][logo]overlay=x=(W-w)/2:y=12,drawtext=fontfile='$FONT':text='AION 2  x  Gears of War\: E-Day':fontcolor=white:fontsize=30:borderw=3:bordercolor=black@0.7:x=(w-tw)/2:y=262" "$OUT/capsule-2x.png"
$FF -y -loglevel error -i "$OUT/capsule-2x.png" -vf scale=616:353 "$OUT/capsule-616x353.png"
$FF -y -loglevel error -i "$OUT/capsule-2x.png" -vf "scale=460:-2,crop=460:215:0:0" "$OUT/header-460x215.png"
echo "capsule art in $OUT"
