# Independent package validation

Use a separate Unity 2022.3 or Unity 6 project for validation. Install the repository root package.json through Package Manager (install from disk), and keep the UGUI version supplied by that editor. The host project belongs outside this repository.

Install Unity Test Framework and add `"com.shanflyer.ui-effects"` to the project manifest's `testables` array. Run the package's EditMode tests from **Window > General > Test Runner**.

For D3D11 GPU checks, launch Unity with `-batchmode -force-d3d11 -projectPath <validation-project> -executeMethod ShanFlyer.UIEffects.UIEffectSpriteMaskValidation.RunBatch -logFile <log>`.

For generated-shader checks, copy `ParticleShaderValidation.cs.template` to the validation package's `Editor/ParticleShaderValidation.cs`. Launch Unity with `-executeMethod ShanFlyer.UIEffects.ParticleShaderValidation.Run -shaderTarget BuiltIn -shaderSource <source-directory> -shaderVersion <version>` and the batch, graphics, project and log arguments above. For URP, use `-shaderTarget URP` and install the matching URP package in the validation project. Built-in sources must contain `DefaultResourcesExtra` and Unity's license; URP sources must contain `Shaders/Particles`, `package.json` and `LICENSE.md`.

For a Windows Player smoke test, copy PlayerSmoke.cs.txt to the validation project's Assets/PlayerSmoke.cs and SmokeBuild.cs.txt to Assets/Editor/SmokeBuild.cs. Run Unity with `-batchmode -nographics -projectPath <validation-project> -executeMethod SmokeBuild.Build -logFile <log>`. The builder creates a temporary scene and PlayerSmoke/UIEffectsSmoke.exe. Run that executable; it exits after three seconds and reports `UI_EFFECTS_PLAYER_SMOKE PASS` only if particles and bake calls were observed. This is a functional smoke check, not a performance benchmark.

Do not run these builder methods against an editing project: use the generated isolated project.
