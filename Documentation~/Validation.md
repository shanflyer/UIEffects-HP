# Package validation

Use separate Unity 2022.3 LTS and Unity 6 projects. This repository is a UPM package, not a Unity project.

## Setup

1. Create a project outside the package repository.
2. In Package Manager, install this repository's root `package.json` from disk. Keep the editor's UGUI version: 1.0 for Unity 2022.3, 2.0 for Unity 6.
3. Install a compatible Unity Test Framework and add `"com.shanflyer.ui-effects"` to the host project's `Packages/manifest.json` `testables` array.
4. Run the package's EditMode tests from **Window > General > Test Runner**.

For batch tests:

```text
Unity.exe -batchmode -force-d3d11 -projectPath <host-project> -runTests -testPlatform EditMode -testResults <results.xml> -logFile <tests.log>
```

Do not combine `-quit` with `-runTests`. Use a graphics device for geometry and GPU checks.

## Verified behavior

The results below predate automatic mesh depth groups and the screen-space-only support boundary. They do not validate depth resets. The current depth-group changes pass standalone C# compilation against Unity 2022.3.62f3 and Unity 6000.4.7f1, including runtime/editor assemblies and player conditional paths. Scene/GPU tests and a Player build were not run for these changes. Older World Space rendering fixtures and comparison tools need migration before they can validate the current bridge contract; World Space now deliberately keeps native rendering.

The adaptive, source-ordered particle merge implementation has also passed standalone C# compilation for both versions, including runtime/editor assemblies and player conditional paths. Scene/GPU validation and frame-time benchmarks have not been run for this change. Older tests that expect immediate merging must be updated for the capture warm-up and workload policy; historical pass counts below do not validate adaptive merging.

The automatic-sizing implementation passed **180/180 EditMode tests** on both Unity 2022.3.62f3 and Unity 6000.4.7f1 using D3D11, with no failures or skipped tests. Coverage includes renderer placement, sprite texture resolution, automatic unit conversion, old prefab migration, paused geometry invalidation, sharing, masking and scheduling.

Earlier separate D3D11 GPU comparisons passed **123/123** on both versions. Shader generation and a Windows Player smoke build were also checked on Unity 2022.3.62f3. These separate GPU and Player checks were not rerun for automatic sizing.

After moving the package to the repository root, both host projects installed it directly from that root through a local UPM dependency and reran the full suite: **180/180 passed on each version**. Package metadata, preserved resource GUIDs and documentation links were also checked.

## Additional checks

Run `ShanFlyer.UIEffects.UIEffectSpriteMaskValidation.RunBatch` in a separate host project for GPU comparisons. See [validation tools](ValidationTools/README.md) for shader generation and Player smoke checks. Helper templates are under `Documentation~/ValidationTools`; Unity does not import that directory as runtime code.
