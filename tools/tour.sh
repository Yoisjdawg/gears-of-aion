#!/bin/sh
# Screenshot tour: one background shot per class and per mode, into build/tour/.
# Usage: sh tools/tour.sh "<Gears Content dir>" "<AION 2 dir>"
set -e
cd "$(dirname "$0")/.."
mkdir -p build/tour
export DAEVAS_NO_TEST=1
for cls in gladiator templar assassin ranger sorcerer spiritmaster cleric chanter; do
  build/game/GearsOfAion.exe --gears "$1" --aion "$2" --background --mute --autoplay --class "$cls" --wave 3 --level 6 \
    --shot "build/tour/class-$cls.png" --shot-when cast --shot-after 30 || true
done
for mode in Endless Siege BossRush Practice Tutorial; do
  build/game/GearsOfAion.exe --gears "$1" --aion "$2" --background --mute --autoplay --mode "$mode" --class templar \
    --shot "build/tour/mode-$mode.png" --shot-after 35 || true
done
echo "tour done: build/tour"
