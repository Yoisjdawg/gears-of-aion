#!/bin/sh
# Builds the release package: build/GearsOfAion-<version>-win64.zip (self-contained win-x64, plus source and notices).
# Usage: sh tools/package.sh <version>
set -e
cd "$(dirname "$0")/.."
VER="$1"
[ -n "$VER" ] || { echo "usage: sh tools/package.sh <version>"; exit 1; }
T="$(pwd)/../.tools"
export DOTNET_ROOT="$T/dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1
rm -rf build/package
"$T/dotnet/dotnet.exe" publish src/Daevas.Game/Daevas.Game.csproj -c Release -r win-x64 --self-contained true -o build/package -v q
rm -f build/package/*.pdb
# debugger-only parts of the runtime (players never attach a debugger)
rm -f build/package/mscordaccore*.dll build/package/mscordbi.dll build/package/Microsoft.DiaSymReader.Native.amd64.dll build/package/createdump.exe
mkdir -p build/package/source build/package/third_party
for d in src sheets native tools; do
  tar --exclude=bin --exclude=obj --exclude='*.dll' --exclude='*.exe' -cf - "$d" | tar -xf - -C build/package/source
done
cp MODLOG.md README.md build/package/source/
cp LICENSE THIRD_PARTY_NOTICES.md README.md build/package/
cp third_party/* build/package/third_party/
rm -f "build/GearsOfAion-$VER-win64.zip"
python -I - "$VER" <<'EOF'
import zipfile, os, sys
root = "build/package"; out = "build/GearsOfAion-%s-win64.zip" % sys.argv[1]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for d, _, fs in os.walk(root):
        for f in fs:
            p = os.path.join(d, f); z.write(p, os.path.relpath(p, root).replace(os.sep, "/"))
print(out, os.path.getsize(out))
EOF
sha256sum "build/GearsOfAion-$VER-win64.zip"
