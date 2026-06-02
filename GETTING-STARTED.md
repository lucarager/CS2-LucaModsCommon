# Using LucaModsCommon in a new mod

This repo is shared code for Luca's Cities: Skylines 2 mods. It is consumed **as a git submodule**, not
as a NuGet package or npm dependency:

- **C#** is compiled *into your mod's own assembly* (source inclusion) — avoids CS2 duplicate-assembly /
  type-load conflicts when several of the mods are enabled together.
- **TypeScript** is pulled in via path mappings so the shared files build inside your mod's UI bundle.

This guide assumes you start from the **CS2 modding template** (`create-csii-ui-mod`). The running
example is a mod called **Stats**, laid out exactly like a fresh template:

```
CS2-Stats/
  Stats/                 <- C# project folder (contains Stats.csproj)
    Stats.csproj
    Mod.cs               <- class Mod : IMod
    Setting.cs           <- class Setting : ModSetting  +  class LocaleEN : IDictionarySource
    Properties/
    UI/                  <- TypeScript / React
      mod.json
      tsconfig.json
      webpack.config.js
      src/index.tsx
```

What you get from the submodule:

| Area | Provides |
|---|---|
| `Mod/LucaModBase.cs` | `LucaModBase<TSelf>` — IMod base that wires logging, settings, localization, Harmony, tests, asset/UI host registration |
| `Extensions/` | `ValueBindingHelper`, `ReflectionExtensions`, `EnumReader`, `GenericUIReader/Writer`, `ExtendedInfoSectionBase` |
| `Utils/PrefixedLogger.cs` | `[Prefix] message` logger |
| `Systems/` | `CommonGameSystemBase` (`GameSystemBase` + a ready `m_Log`); `CommonUISystemBase` (`UISystemBase` + the same `m_Log` + `CreateBinding`/`CreateTrigger` helpers) |
| `Rendering/CustomOverlayRenderSystem.cs` | custom overlay renderer (optional) |
| `ui/utils/` | `TwoWayBinding`, `TriggerBuilder`, `c()` classnames, `uiMapper` |
| `ui/vanilla/` | vanilla component registry (`initialize`, `VC`/`VT`/`VF`) with a shared base set |
| `LucaModsCommon.props` / `.targets` | shared build config + BuildUI / CopyIcons / PublishConfig / PostProcessor steps the bare template lacks |

---

## 0. Prerequisites

- CSII Modding Toolchain installed, with `CSII_TOOLPATH` and `CSII_USERDATAPATH` user environment
  variables set.
- Your new mod is its own git repository.

---

## 1. Add the submodule

Place it at `Common/` **inside the C# project folder** (the one with `Stats.csproj`):

```sh
cd Stats
git submodule add https://github.com/lucarager/CS2-LucaModsCommon.git Common
git submodule update --init --recursive
```

You now have `Stats/Common/` (C# at its root, shared TS under `Stats/Common/ui/`).

> It must live inside the project folder: SDK-style projects compile every `.cs` under the project
> directory by default, so the shared C# is picked up automatically (step 2).

---

## 2. C# wiring (source inclusion)

A fresh template `Stats.csproj` carries an inline `<Reference>` list and no package refs / build steps:

```xml
<!-- BEFORE (template) -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Configurations>Debug;Release</Configurations>
    <PublishConfigurationPath>Properties\PublishConfiguration.xml</PublishConfigurationPath>
  </PropertyGroup>
  <Import Project="...\Mod.props" />
  <Import Project="...\Mod.targets" />
  <ItemGroup><Reference Include="Game">...</Reference> ...lots more... </ItemGroup>
  <ItemGroup><Reference Update="System">...</Reference> ...</ItemGroup>
  <ItemGroup><None Include="$(ModPropsFile)" .../> ...</ItemGroup>
</Project>
```

Replace it with this. The inline `<Reference>` blocks are **deleted** — `LucaModsCommon.props`
provides a superset (plus Harmony, PolySharp, `LangVersion`, configs, `DefineConstants`):

```xml
<!-- AFTER -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <Title>Stats</Title>
    <Version>1.0.0</Version>
    <AssemblyVersion>$(Version).0</AssemblyVersion>
    <FileVersion>$(Version).0</FileVersion>
    <PublishConfigurationPath>Properties\PublishConfiguration.xml</PublishConfigurationPath>
  </PropertyGroup>

  <!-- Don't compile the submodule's own build output -->
  <PropertyGroup>
    <DefaultItemExcludes>$(DefaultItemExcludes);Common\obj\**;Common\bin\**</DefaultItemExcludes>
  </PropertyGroup>

  <!-- Shared build config: package refs, LangVersion, configs (Debug;Release;I18N), <Reference> list -->
  <Import Project="Common\LucaModsCommon.props" />

  <!-- CSII toolchain -->
  <Import Project="$([System.Environment]::GetEnvironmentVariable('CSII_TOOLPATH', 'EnvironmentVariableTarget.User'))\Mod.props" />
  <Import Project="$([System.Environment]::GetEnvironmentVariable('CSII_TOOLPATH', 'EnvironmentVariableTarget.User'))\Mod.targets" />

  <!-- Shared build steps. MUST be imported AFTER Mod.targets so the shared
       CustomModPostProcessorConfig overrides the toolchain's stub (Windows-only build). -->
  <Import Project="Common\LucaModsCommon.targets" />

  <!-- Keep these links if you like seeing Mod.props/targets in the IDE (optional) -->
  <ItemGroup>
    <None Include="$(ModPropsFile)" Link="Properties\Mod.props" />
    <None Include="$(ModTargetsFile)" Link="Properties\Mod.targets" />
  </ItemGroup>

  <!-- OPTIONAL: skip the overlay renderer if your mod doesn't use it -->
  <!-- <ItemGroup><Compile Remove="Common\Rendering\**\*.cs" /></ItemGroup> -->

</Project>
```

> **Do not** add `<Compile Include="Common\**\*.cs" />` — the SDK default glob already compiles the
> shared sources because `Common/` is inside the project folder. An explicit include double-compiles
> and fails.

### Convert `Mod.cs` to `LucaModBase`

The template `Mod.cs` is a hand-rolled `IMod` that news up `Setting`, registers it, adds the en-US
source, registers key bindings, loads settings, and wires some demo input actions. **`LucaModBase`
already does all of that** (logger init, `ReflectionExtensions.Initialize`, `PrefixedLogger.DefaultLog`,
settings create/register/load, en-US + embedded-resource localization, key bindings, Harmony
`PatchAll`/`UnpatchAll`, test discovery, UI host registration). So the file collapses to:

```csharp
namespace Stats {
    using Colossal;       // IDictionarySource  (NOTE: Colossal, not Colossal.Localization)
    using Game;           // UpdateSystem
    using Game.Modding;   // IMod, ModSetting
    using LucaModsCommon.Mod;

    public sealed class Mod : LucaModBase<Mod>, IMod {   // <-- your class declares IMod, not the base
        public override  string ModName      => "Stats";
        public override  string Id           => "Stats";  // binding group + must match UI/mod.json "id"
        protected override string UiHostPrefix => "stats"; // AddHostLocation prefix for coui:// asset URLs

        protected override ModSetting CreateSettings(IMod mod) => new Setting(mod);

        protected override IDictionarySource CreateEnUsLocalization(ModSetting settings) =>
            new LocaleEN((Setting)settings);

        protected override void RegisterSystems(UpdateSystem updateSystem) {
            // register your ECS systems, e.g.:
            // updateSystem.UpdateAt<MyStatsUISystem>(SystemUpdatePhase.UIUpdate);
        }

        // Optional: OnAfterLoad(UpdateSystem) — put the template's ProxyAction/input-action demo here
        // if you want to keep it. OnBeforeDispose(), GenerateLanguageFile() are also available.
    }
}
```

> **Declare `, IMod` on your class — this is required.** `LucaModBase` deliberately does **not**
> implement `IMod`. The game instantiates *every* `IMod`-derived type in the assembly
> (`GetTypesDerivedFrom<IMod>()` → `GetUninitializedObject`), and since shared code is compiled into your
> assembly, an abstract `IMod` base would get picked up and crash on load with *"Type cannot be
> instantiated"*. Your concrete class declares `IMod` and inherits `OnLoad`/`OnDispose` from the base to
> satisfy it.

Two follow-ups in the template's `Setting.cs`:

- It logs via the old static `Mod.log` (e.g. `Mod.log.Info("Button clicked")`). That field no longer
  exists — use `Mod.Instance.Log` (an `ILog`) or `Mod.Instance.ModLog` (a `PrefixedLogger`).
- The template's `SetDefaults()` throws `NotImplementedException` — give it a real body (or empty).

Your `Setting` and `LocaleEN` classes otherwise carry over unchanged.

### Systems

```csharp
namespace Stats.Systems {
    using LucaModsCommon.Systems;

    public partial class MyStatsSystem : CommonGameSystemBase {
        protected override void OnCreate() {
            base.OnCreate();          // sets m_Log = new PrefixedLogger(GetType().Name)
            m_Log.Debug("ready");
        }
    }
}
```

Anywhere else, `new PrefixedLogger(nameof(MyThing))` works too — the single-arg ctor uses the ambient
`PrefixedLogger.DefaultLog` that `LucaModBase.OnLoad` sets. (Don't build loggers before the mod loads.)

---

## 3. TypeScript / UI wiring

Your UI is `Stats/UI/`; the shared TS is `Stats/Common/ui/`. Two configs must agree.

### `Stats/UI/tsconfig.json`

Add `paths` entries for the shared folders (keep `baseUrl: "src"` and `resolveJsonModule`):

```jsonc
{
  "compilerOptions": {
    "baseUrl": "src",
    "resolveJsonModule": true,
    "typeRoots": ["./types", "node_modules/@types"],   // bare template omits @types; add it
    "paths": {
      "mod.json":  ["../mod.json"],
      "utils/*":   ["utils/*",   "../../Common/ui/utils/*"],
      "vanilla/*": ["vanilla/*", "../../Common/ui/vanilla/*"],
      // shared TS lives OUTSIDE UI/, so node can't find UI/node_modules from it — map react explicitly:
      "react":     ["../node_modules/@types/react"],
      "react/*":   ["../node_modules/@types/react/*"]
    }
    // ...keep the rest (jsx, strict, target, etc.)
  }
}
```

`paths` resolve relative to `baseUrl` (`Stats/UI/src`), so `../../Common/ui` → `Stats/Common/ui` and
`../node_modules` → `Stats/UI/node_modules`.

> **Why the `react` mapping?** `vanilla/types.ts` imports from `react`. Because it lives in
> `Stats/Common/ui` (outside `Stats/UI/`), TypeScript's node resolution can't walk up to
> `Stats/UI/node_modules` to find `@types/react`, so it errors `TS2307: Cannot find module 'react'`.
> The `cs2/*` SDK modules don't hit this because they're ambient `declare module` types. Mapping
> `react` (and `react/*` for `jsx-runtime`) to the UI's `node_modules` fixes it. webpack already
> externalizes `react`, so this is purely for the type-checker.

### `Stats/UI/webpack.config.js`

Add the shared `ui` folder to `resolve.modules` (the `mod.json` alias is already there in the template):

```js
resolve: {
  extensions: [".tsx", ".ts", ".js"],
  modules: [
    "node_modules",
    path.join(__dirname, "src"),
    path.join(__dirname, "..", "Common", "ui"),   // <-- add this (Stats/Common/ui)
  ],
  alias: {
    "mod.json": path.resolve(__dirname, "mod.json"),
  },
},
```

The shared files import `mod.json` by bare specifier; the alias makes that resolve to **your**
`Stats/UI/mod.json`.

### Use it

```ts
// src/gameBindings.ts
import { TwoWayBinding } from "utils/bidirectionalBinding";

export const GAME_BINDINGS = {
    PANEL_OPEN: new TwoWayBinding<boolean>("PANEL_OPEN", false),
};
```

```tsx
// src/index.tsx  (template starts with: moduleRegistry.append('Menu', HelloWorldComponent))
import { ModRegistrar } from "cs2/modding";
import { initialize } from "vanilla/Components";

const register: ModRegistrar = (moduleRegistry) => {
    initialize(
        moduleRegistry,
        // extra vanilla MODULES beyond the shared base (optional):
        // [{ path: "game-ui/editor/widgets/fields/toggle-field.tsx", components: ["ToggleField"] }],
        // extra vanilla THEMES beyond the shared base (optional):
        // [{ path: "game-ui/.../x.module.scss", name: "x" }],
    );

    // ...append your panels/components
};

export default register;
```

```tsx
import { VC, VT, VF } from "vanilla/Components";
import { c } from "utils/classes";
```

To get strong typing for components beyond the shared base, augment the interface in a local `.d.ts`:

```ts
import "vanilla/types";
declare module "vanilla/types" {
    interface IVanillaComponents { ToggleField: React.FC<MyToggleFieldProps>; }
}
```

### C# ↔ UI two-way bindings

`TwoWayBinding<T>("KEY")` pairs with the shared `CommonUISystemBase.CreateBinding("KEY", initial, cb)`
— the naming lines up automatically (group = mod id, value binding = `BINDING:KEY`, setter trigger =
`TRIGGER:KEY`):

```csharp
// C#: a CommonUISystemBase-derived system (LucaModsCommon.Systems), registered in RegisterSystems().
// base.OnCreate() wires the inherited m_Log, just like CommonGameSystemBase.
public partial class StatsUISystem : CommonUISystemBase {
    protected override string ModId => Mod.Instance.Id;
    private ValueBindingHelper<int> m_Counter;
    protected override void OnCreate() {
        base.OnCreate();
        m_Counter = CreateBinding("COUNTER", 0, v => { /* UI pushed a new value */ });
    }
}
```
```tsx
// TS: read with useValue(.binding), write with .set(...)
const counter = useValue(GAME_BINDINGS.COUNTER.binding);
GAME_BINDINGS.COUNTER.set(counter + 1);
```

See `CS2-Stats` (`Systems/StatsUISystem.cs`, `UI/src/gameBindings.ts`, `UI/src/mods/statsPanel.tsx`)
for the full worked example.

> Not shared: `translations.ts` (its import targets each mod's own `en-US.json`) and `UI/types/*.d.ts`
> (SDK-provided per mod). Keep your own copies.
>
> Template quirks: a fresh `CS2-Stats` has a stray duplicate `UI/` at the repo root — the build uses
> `Stats/UI/`; ignore/remove the root one. Also set a real `"id"` in `Stats/UI/mod.json` (the template
> ships `"id": "UI"`); it must match `Mod.Id`.

---

## 4. Publish config, assets, localization

The shared `LucaModsCommon.targets` adds build steps the bare template doesn't have:

- **`GeneratePublishConfiguration`** generates `Properties/PublishConfiguration.xml` from a template.
  The CS2 template ships a *static* `PublishConfiguration.xml` instead, so this target is **guarded** and
  simply skips unless you opt in. To opt in (recommended, matches Platter/NetworkTools): add
  `Properties/PublishConfiguration.template.xml` (your static xml with `ModVersion`/`LongDescription`/
  `ChangeLog` left as placeholders), `Properties/LongDescription.md`, and
  `Properties/Changelog/<version>.md`. Otherwise your static `PublishConfiguration.xml` is used as-is.
- **`CopyIcons`** copies `Assets/**` to the deploy dir (no-op if you have no `Assets/`).
- **`BuildUI`** runs `npm run build` in `UI/` (run `npm install` in `Stats/UI/` once first).
- **`CustomModPostProcessorConfig`** overrides the toolchain stub to build Windows-only (faster).

**Localization.** en-US comes from your code `IDictionarySource` (the `CreateEnUsLocalization` override).
For other locales, `LoadNonEnglishLocalizations` loads embedded resources named
`<AssemblyName>.L10n.lang.<locale>.json` (or `<AssemblyName>.lang.<locale>.json`). Put your translation
JSON under `L10n/lang/` and embed it:

```xml
<ItemGroup>
  <EmbeddedResource Include="L10n\lang\*.json" />
</ItemGroup>
```

Optionally override `GenerateLanguageFile()` to export `L10n/lang/en-US.json` from your en-US source for
translators — it runs automatically on the **I18N** build configuration (which defines `EXPORT_EN_US`).
Use `[CallerFilePath]` to locate your source dir. (See `CS2-Stats/Stats/Mod.cs` for a working example.)

---

## 5. Cloning, updating, pinning

- Clone a consumer with submodules: `git clone --recurse-submodules <your-mod-url>`
  (or after a plain clone: `git submodule update --init --recursive`).
- Pull the latest shared code: from `Stats/`, `git submodule update --remote Common`, then commit the
  updated pointer.
- Each mod is pinned to a specific `Common` commit; bump it deliberately and rebuild.
