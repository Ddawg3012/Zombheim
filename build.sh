#!/usr/bin/env bash
# Build Zombheim: preflight the sheets, generate code, compile, package dist/Zombheim-<version>.zip.
# Reference assemblies (BepInEx 5 core from its GitHub release, UnityEngine modules from NuGet) are fetched into .deps/.
set -euo pipefail
cd "$(dirname "$0")"
VERSION=0.1.0
DEPS=.deps
if [ ! -f $DEPS/bepinex/BepInEx/core/BepInEx.dll ]; then
  mkdir -p $DEPS/bepinex && curl -sSL -o $DEPS/bepinex.zip https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip
  python3 -I -m zipfile -e $DEPS/bepinex.zip $DEPS/bepinex
fi
if [ ! -f $DEPS/unity/lib/net45/UnityEngine.CoreModule.dll ]; then
  mkdir -p $DEPS/unity && curl -sSL -o $DEPS/unity.nupkg https://api.nuget.org/v3-flatcontainer/unityengine.modules/2021.3.33/unityengine.modules.2021.3.33.nupkg
  python3 -I -m zipfile -e $DEPS/unity.nupkg $DEPS/unity
fi
python3 tools/sheets.py
dotnet build src/Zombheim/Zombheim.csproj -c Release -nologo -v quiet
rm -rf dist && mkdir -p dist/pkg/Zombheim
cp src/Zombheim/bin/Release/net472/Zombheim.dll dist/pkg/Zombheim/
cp README.md LICENSE dist/pkg/Zombheim/
(cd dist/pkg && python3 -c "
import zipfile,os
with zipfile.ZipFile('../Zombheim-$VERSION.zip','w',zipfile.ZIP_DEFLATED) as z:
    for root,_,files in os.walk('Zombheim'):
        for f in sorted(files): z.write(os.path.join(root,f))
")
echo "built dist/Zombheim-$VERSION.zip"
