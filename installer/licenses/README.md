# Supplemental upstream notices

These unmodified files supplement license texts missing from NuGet packages.
The packaging script also copies notices actually present in dependency packages
and in the self-contained .NET runtime pack. Review this mapping on upgrades.

| File | Covered dependency | Source |
| --- | --- | --- |
| [Avalonia.txt](Avalonia.txt) | Avalonia packages 12.1.2 | [Package source commit licence](https://github.com/AvaloniaUI/Avalonia/blob/d3c867a9e2de379249b03dbeb3495bd7f076a81a/licence.md) |
| [Avalonia-NOTICE.md](Avalonia-NOTICE.md) | Embedded third-party code in Avalonia 12.1.2 | [Package source commit notices](https://github.com/AvaloniaUI/Avalonia/blob/d3c867a9e2de379249b03dbeb3495bd7f076a81a/NOTICE.md) |
| [Manrope OFL](../../branding/fonts/OFL.txt) | Bundled Manrope 500/700/800 fonts; copied to licenses/Manrope-OFL.txt by Sessions.App | [Pinned source and regeneration](../../branding/fonts/README.md) |
| [MicroCom.txt](MicroCom.txt) | MicroCom.Runtime 0.11.6 | [Package source commit license](https://github.com/kekekeks/MicroCom/blob/76785efcafd91b5902fd19dd11145f6dd655b7b4/LICENSE) |
| [Tmds.DBus.txt](Tmds.DBus.txt) | Tmds.DBus.Protocol 0.94.1 | [Package source commit license](https://github.com/tmds/Tmds.DBus/blob/b4a7fed0b878f74cb54f7cca84d2889af4e596ba/COPYING) |
| [WiX.txt](WiX.txt) | WiX 7 native utility custom actions (close-app detection, launch after setup) and standard UI dialogs and strings embedded in the MSI | [WiX 7 source commit license](https://github.com/wixtoolset/wix/blob/b8977d6f88e7b68e000bac226a2814f236770570/LICENSE.TXT) |

Microsoft.Win32.SystemEvents uses the MIT license included with the bundled .NET
runtime; its own package also supplies third-party notices. Package metadata and
copyright declarations are retained as `.nuspec` files in the generated inventory.
