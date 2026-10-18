# Third-party software in Daevas of E-Day

| Component | Author | License | Use |
|---|---|---|---|
| [CUE4Parse](https://github.com/FabianFG/CUE4Parse) (commit 18430ca, patched: `third_party/cue4parse.patch`) | FabianFG and contributors | Apache-2.0 (`third_party/CUE4Parse-LICENSE.txt`, `third_party/CUE4Parse-NOTICE.txt`) | Reads Gears of War: E-Day's archives |
| CUE4Parse-Conversion (same repository) | FabianFG and contributors | Apache-2.0 | Texture decoding |
| [ooz](https://github.com/powzix/ooz) (`native/ooz`) | Powzix | GPL-3.0-or-later | Oodle (Kraken/Mermaid/Selkie/Leviathan) decompression, built as `ooz.dll` |
| [ACL](https://github.com/nfrechette/acl) (commit 414689d, in CUE4Parse-Natives, built as `CUE4Parse-Natives.dll`) | Nicholas Frechette and contributors | MIT (`third_party/ACL-LICENSE.txt`) | Decodes Gears of War: E-Day's animations |
| [vgmstream](https://github.com/vgmstream/vgmstream) r2117 (`vgmstream/vgmstream-cli.exe`, built with Vorbis only: `VGM_MPEG=0 VGM_G719=0 VGM_FFMPEG=0 VGM_ATRAC9=0 VGM_CELT=0 VGM_SPEEX=0 VGM_G7221=0`) | Adam Gashlin, bnnm and contributors | ISC (`vgmstream/vgmstream-COPYING.txt`) | Decodes Gears of War: E-Day's Wwise Vorbis voice media on the player's PC into a local cache |
| libvorbis 1.3.7 + libogg 1.3.5 (`vgmstream/libvorbis.dll`) | Xiph.Org Foundation | BSD-3-Clause (`vgmstream/libvorbis-COPYING.txt`, `vgmstream/libogg-COPYING.txt`) | Vorbis decoding for vgmstream |
| [raylib](https://github.com/raysan5/raylib) | Ramon Santamaria and contributors | zlib | Window, rendering, audio |
| [Raylib-cs](https://github.com/ChrisDill/Raylib-cs) 8.1.0 | Chris Dill and contributors | zlib | C# bindings for raylib |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | Microsoft / Xamarin | MIT | Image conversion |
| [AssetRipper.TextureDecoder](https://github.com/AssetRipper/TextureDecoder) | AssetRipper | MIT | BC/DXT texture decoding |
| [Serilog](https://github.com/serilog/serilog), Newtonsoft.Json, K4os LZ4, ZstdSharp, OffiUtils, GenericReader, other CUE4Parse NuGet dependencies | their authors | MIT / Apache-2.0 / BSD | Pulled in by CUE4Parse |
| .NET runtime | Microsoft | MIT | Self-contained runtime |

Three embedded resources are removed from the CUE4Parse build in this package: ACE7Key.bin, MogKeyPool.bin and ProSpiDecryptor.dll.gz. They hold keys and decryptors for other games, and the mashup doesn't use them.

The full source of this program, the CUE4Parse patch and the ooz source are in the `source` folder of the release package.
