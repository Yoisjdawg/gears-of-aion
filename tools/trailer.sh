#!/bin/sh
# Trailer: records gameplay clips on autoplay (silent, in the background) and cuts them together with
# captions and title cards into build/trailer/GearsOfAion-trailer.mp4 (1280x720, no audio).
# Needs ffmpeg with libopenh264 on the PATH (or DAEVAS_FFMPEG).
# Usage: sh tools/trailer.sh            record every clip, then cut
#        sh tools/trailer.sh 6 8        re-record clips 6 and 8, then cut
#        sh tools/trailer.sh --cut-only
set -e
cd "$(dirname "$0")/.."
OUT=build/trailer
EXE=${DAEVAS_EXE:-build/game/GearsOfAion.exe}
FF="${DAEVAS_FFMPEG:-ffmpeg} -nostdin"
FONT="C\\:/Windows/Fonts/segoeuib.ttf"
mkdir -p "$OUT"
export DAEVAS_NO_TEST=1

# class | mode | wave | level | map | moment | earliest second | caption
CLIPS="sorcerer|Campaign|1|6|downtown|cast|6|Eight AION classes, played as Gears heroes
gladiator|Campaign|2|8|downtown|chain|6|Chains, crowd control and elements
ranger|Campaign|2|8|downtown|flight|6|Daeva wings over the Siege
templar|Campaign|3|10|downtown|boss|6|Vrak, the Corpser, Vrak the Scorched and the Brumak
chanter|Endless|4|12|downtown|transform|6|Daeva Transformation
assassin|Campaign|7|12|downtown|death|18|Dust storms, razorhail and night thunderstorms
spiritmaster|Endless|2|10|historic|pet|6|Three districts from your own install
cleric|Campaign|5|12|industrial|death|6|Hold the line"

ONLY=" $* "
if [ "$1" != "--cut-only" ]; then
  i=0
  echo "$CLIPS" | while IFS='|' read -r cls mode wave level map moment caption; do
    i=$((i + 1))
    echo "clip $i: $cls $mode wave $wave ($moment)"
    DAEVAS_MAP=$map timeout 420 "$EXE" --background --mute --autoplay --no-hud --class "$cls" --mode "$mode" --wave "$wave" --level "$level" \
      --record "$OUT/clip$i.mp4" --record-start 6 --record-when "$moment" --record-seconds 6 > "$OUT/clip$i.log" 2>&1 || true
    [ -s "$OUT/clip$i.mp4" ] || echo "  clip $i missing (see $OUT/clip$i.log)"
  done
fi

# the opening: a flight from the street to the district's edge and out over the Abyss (a camera path, no autoplay)
if [ "$1" != "--cut-only" ]; then
  DAEVAS_CAMPATH="0,2,15,0,1.6,60;0,10,45,0,5,100;0,28,80,0,-20,150;-60,60,150,-40,-40,0;-185,52,135,0,-12,0" timeout 420 "$EXE" --background --mute --no-hud --start-screen playing --class templar \
    --record "$OUT/abyss.mp4" --record-start 5 --record-seconds 9 > "$OUT/abyss.log" 2>&1 || true
fi

# title card, clips with captions, end card; 0.5 s crossfades
card() { $FF -y -loglevel error -f lavfi -i "color=c=0x0c0b10:s=1280x720:d=$2:r=30" \
  -vf "drawtext=fontfile='$FONT':text='$1':fontcolor=0xffd678:fontsize=72:x=(w-tw)/2:y=(h-th)/2-30,drawtext=fontfile='$FONT':text='$3':fontcolor=white:fontsize=26:x=(w-tw)/2:y=(h/2)+40,fade=in:0:15,fade=out:st=$(($2 - 1)):d=1" \
  -c:v libopenh264 -b:v 6M -pix_fmt yuv420p "$OUT/$4.mp4"; }
card "GEARS OF AION" 4 "The Daevas of AION 2 land in the Siege of Gears of War\\: E-Day" card-start
card "GEARS OF AION" 5 "Needs Gears of War\\: E-Day and AION 2  -  made by yoisjdawg" card-end

idx=1
{
  if [ -s "$OUT/abyss.mp4" ]; then
    $FF -y -loglevel error -i "$OUT/abyss.mp4" -vf "scale=1280:720,drawtext=fontfile='$FONT':text='A district of Sera, torn into the Abyss':fontcolor=white:fontsize=30:borderw=3:bordercolor=black@0.6:x=60:y=h-90" \
      -c:v libopenh264 -b:v 8M -pix_fmt yuv420p -an "$OUT/cap0.mp4"
    echo "$OUT/cap0.mp4"
  fi
} > "$OUT/parts0.txt"
echo "$CLIPS" | {
  while IFS='|' read -r cls mode wave level map moment start caption; do
    f="$OUT/clip$idx.mp4"
    if [ -s "$f" ]; then
      $FF -y -loglevel error -i "$f" -vf "scale=1280:720,curves=all=0/0 0.25/0.36 0.6/0.72 1/1,hue=s=1.1,drawtext=fontfile='$FONT':text='$caption':fontcolor=white:fontsize=30:borderw=3:bordercolor=black@0.6:x=60:y=h-90" \
        -c:v libopenh264 -b:v 8M -pix_fmt yuv420p -an "$OUT/cap$idx.mp4"
      echo "$OUT/cap$idx.mp4"
    fi
    idx=$((idx + 1))
  done
} > "$OUT/parts1.txt"
cat "$OUT/parts0.txt" "$OUT/parts1.txt" > "$OUT/parts.txt"
echo "$OUT/card-end.mp4" >> "$OUT/parts.txt"

# chain the parts with xfade
args="-i $OUT/card-start.mp4"; chain=""; last="[0:v]"; k=1; acc=4
while read -r part; do
  args="$args -i $part"
  len=$($FF -i "$part" 2>&1 | sed -n 's/.*Duration: 00:00:\([0-9.]*\).*/\1/p')
  off=$(awk "BEGIN{print $acc - 0.5}")
  chain="$chain$last[$k:v]xfade=transition=fade:duration=0.5:offset=$off[x$k];"
  last="[x$k]"; acc=$(awk "BEGIN{print $acc + $len - 0.5}"); k=$((k + 1))
done < "$OUT/parts.txt"
$FF -y -loglevel error $args -filter_complex "${chain%;}" -map "$last" -c:v libopenh264 -b:v 8M -pix_fmt yuv420p -movflags +faststart "$OUT/GearsOfAion-trailer.mp4"
echo "trailer: $OUT/GearsOfAion-trailer.mp4 ($acc s)"
