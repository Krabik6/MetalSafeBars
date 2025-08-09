# MetalSafeBars

Читабельные полоски здоровья для **Risk of Rain 2** на macOS (CrossOver):
- Ничего не скрывает — рисует поверх ванильных баров.
- HP и щит как отдельные заливки.
- Лёгкий и совместимый подход (UI-оверхлей).

**GUID:** `me.metalfix.ror2.healthbars`  
**Требования:** `bbepis-BepInExPack` 5.4+

## Сборка

```bash
rm -rf obj bin && \
dotnet build -c Release && \
ver=$(jq -r '.version_number' manifest.json | awk -F. '{OFS="."; $NF+=1; print}') && \
jq --arg v "$ver" '.version_number=$v' manifest.json > manifest.tmp && mv manifest.tmp manifest.json && \
PKG="MetalSafeBars" && \
rm -rf "$PKG" && mkdir -p "$PKG/BepInEx/plugins/MetalSafeBars" && \
cp bin/Release/netstandard2.1/MetalSafeBars.dll "$PKG/BepInEx/plugins/MetalSafeBars/MetalSafeBars.dll" && \
cp manifest.json README.md icon.png "$PKG"/ && \
ZIP="release_zips/MetalSafeBars-$ver.zip" && \
rm -f "$ZIP" && \
zip -r "$ZIP" "$PKG" && \
rm -rf "$PKG"
```

**Автор:** Lev Budko

