# TapTapLootPatcher

`TapTapLootPatcher` is a .NET 8 CLI tool for inspecting Unity assemblies and applying targeted Mono.Cecil patches for TapTapLoot to Auto run and loot rare items.

## Prerequisites

- .NET 8 SDK
- The target Unity assembly you want to inspect or patch
- The Unity `Managed` directory when dependency resolution needs extra assemblies

## Build

```powershell
$env:DOTNET_CLI_HOME='.\.dotnet-home'; dotnet build .\TapTapLootPatcher.csproj
```

The executable is emitted at `.\bin\Debug\net8.0\TapTapLootPatcher.exe`.

## Usage

```text
TapTapLootPatcher scan-input <assembly-path>
TapTapLootPatcher scan-strings <assembly-path> <term>
TapTapLootPatcher list-types <assembly-path> <term>
TapTapLootPatcher list-methods <assembly-path> <term>
TapTapLootPatcher dump-method <assembly-path> <term>
TapTapLootPatcher dump-type <assembly-path> <term>
TapTapLootPatcher patch-autorun <input-assembly> <output-assembly> [managed-dir]
TapTapLootPatcher patch-mythic <input-assembly> <output-assembly> [managed-dir]
TapTapLootPatcher patch-both <input-assembly> <output-assembly> [managed-dir]
TapTapLootPatcher simulate-high-tier <count> [legendary-ratio-0-to-1]
```

## Examples

```powershell
.\bin\Debug\net8.0\TapTapLootPatcher.exe scan-input "Assembly-CSharp.dll"
.\bin\Debug\net8.0\TapTapLootPatcher.exe scan-strings "Assembly-CSharp.dll" "GlobalKeyHook"
.\bin\Debug\net8.0\TapTapLootPatcher.exe list-types "Assembly-CSharp.dll" "ItemRegistry"
.\bin\Debug\net8.0\TapTapLootPatcher.exe list-methods "Assembly-CSharp.dll" "GlobalKeyHook"
.\bin\Debug\net8.0\TapTapLootPatcher.exe dump-method "Assembly-CSharp.dll" "GetRandomRarityByLevel"
.\bin\Debug\net8.0\TapTapLootPatcher.exe dump-type "Assembly-CSharp.dll" "ItemRegistrySO"
.\bin\Debug\net8.0\TapTapLootPatcher.exe patch-autorun "Assembly-CSharp.dll" "Assembly-CSharp.autorun.dll" "C:\Managed"
.\bin\Debug\net8.0\TapTapLootPatcher.exe patch-mythic "Assembly-CSharp.dll" "Assembly-CSharp.patched.dll" "C:\Managed"
.\bin\Debug\net8.0\TapTapLootPatcher.exe patch-both "Assembly-CSharp.dll" "Assembly-CSharp.patched.dll" "C:\Managed"
.\bin\Debug\net8.0\TapTapLootPatcher.exe simulate-high-tier 20 0.5
```

The optional `[managed-dir]` argument should point to the Unity `Managed` folder when `Assembly-CSharp.dll` references sibling assemblies that are not in the same directory.
