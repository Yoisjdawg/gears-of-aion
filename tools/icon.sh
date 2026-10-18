#!/bin/sh
# The app icon from the logo (--logo --logo-plain): src/Daevas.Game/Assets/app.ico at 16..256 px (PNG entries), and
# build/logo/logo.png (with the name). Needs ffmpeg and a built game.
set -e
cd "$(dirname "$0")/.."
OUT=build/logo; mkdir -p "$OUT/ico"
DAEVAS_NO_TEST=1 timeout 60 build/game/GearsOfAion.exe --background --mute --logo "$(cygpath -w "$PWD/$OUT/logo-plain.png")" --logo-plain > /dev/null 2>&1 < /dev/null || true
DAEVAS_NO_TEST=1 timeout 60 build/game/GearsOfAion.exe --background --mute --logo "$(cygpath -w "$PWD/$OUT/logo.png")" > /dev/null 2>&1 < /dev/null || true
for z in 16 24 32 48 64 128 256; do ffmpeg -nostdin -y -loglevel error -i "$OUT/logo-plain.png" -vf scale=$z:$z:flags=lanczos -pix_fmt rgba "$OUT/ico/$z.png"; done
python -I - "$OUT/ico" src/Daevas.Game/Assets/app.ico <<'PY'
import struct, sys, os
sizes = [16, 24, 32, 48, 64, 128, 256]
data = [open(os.path.join(sys.argv[1], f"{z}.png"), "rb").read() for z in sizes]
out, off = b"", 6 + 16 * len(sizes)
for z, b in zip(sizes, data):
    out += struct.pack("<BBBBHHII", z % 256, z % 256, 0, 0, 1, 32, len(b), off); off += len(b)
open(sys.argv[2], "wb").write(struct.pack("<HHH", 0, 1, len(sizes)) + out + b"".join(data))
PY
echo "icon in src/Daevas.Game/Assets/app.ico"
