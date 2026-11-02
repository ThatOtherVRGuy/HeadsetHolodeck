#!/usr/bin/env bash
# Order 06 evidence harness — docs-versus-behaviour claim audit and link check.
# Read-only apart from its own stdout. Never prints a credential value: the .env scan below counts
# matches per key name and prints no value, no line, and no surrounding context.
#
# Usage: bash docs/stabilization/evidence-06/docs-claim-audit.sh [claims|links|all]

set -u

MODE="${1:-all}"
FAILS=0
CHECKS=0

pass() { CHECKS=$((CHECKS + 1)); printf 'PASS  %s\n' "$1"; }
fail() { CHECKS=$((CHECKS + 1)); FAILS=$((FAILS + 1)); printf 'FAIL  %s\n' "$1"; }

# has <file> <fixed-string> <label>
has() {
  if grep -Fq -- "$2" "$1" 2>/dev/null; then pass "$3"; else fail "$3 (missing in $1: $2)"; fi
}

# lacks <file> <fixed-string> <label>
lacks() {
  if grep -Fq -- "$2" "$1" 2>/dev/null; then fail "$3 (unexpected in $1: $2)"; else pass "$3"; fi
}

claims() {
  echo "== Unity version and build entry point =="
  has ProjectSettings/ProjectVersion.txt "m_EditorVersion: 6000.2.10f1" "ProjectVersion.txt pins 6000.2.10f1"
  has AGENTS.md "6000.2.10f1" "AGENTS.md states the same Unity version"
  has Assets/App/Shared/Editor/CommandLineAndroidBuild.cs "public static void BuildApk()" "BuildApk entry point exists"
  has Assets/App/Shared/Editor/CommandLineAndroidBuild.cs 'GetArgument("-outputPath"' "BuildApk accepts -outputPath"
  has Assets/App/Shared/Editor/CommandLineAndroidBuild.cs "BuildEnvCredentials.ModeArgument" "BuildApk reads the credential-mode argument constant"
  has Packages/com.worldlabs.gaussian-splatting/Editor/WorldLabs/BuildEnvCredentials.cs 'ModeArgument = "-credentialMode"' "-credentialMode is the declared argument name"
  has Packages/com.worldlabs.gaussian-splatting/Editor/WorldLabs/BuildEnvCredentials.cs 'PrivateValue = "private"' "private intent exists"
  has Packages/com.worldlabs.gaussian-splatting/Editor/WorldLabs/BuildEnvCredentials.cs 'DistributionValue = "distribution"' "distribution intent exists"
  has Packages/com.worldlabs.gaussian-splatting/Editor/WorldLabs/BuildEnvCredentials.cs 'DeviceValue = "device"' "device intent exists"
  has AGENTS.md "-credentialMode device|private|distribution" "AGENTS.md documents the three intents"
  has README.md "-credentialMode device" "README documents the device intent"

  echo
  echo "== .env resolution =="
  has Packages/com.worldlabs.gaussian-splatting/Editor/WorldLabs/BuildEnvCopier.cs "Assets/Resources/WorldLabs" "build embeds under Assets/Resources/WorldLabs"
  has Packages/com.worldlabs.gaussian-splatting/Editor/WorldLabs/BuildEnvCredentials.cs 'EmbeddedFileName = "worldlabs_env.txt"' "embedded file is worldlabs_env.txt"
  has Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/EnvLoader.cs 'Path.Combine(Application.persistentDataPath, ".env")' "EnvLoader reads the device .env"
  has Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/EnvLoader.cs "embedded resources" "EnvLoader source order names the embedded copy"
  has Assets/App/Shared/Command/SpeechIntent/Runtime/RuntimeDotEnv.cs "RuntimeInitializeLoadType.BeforeSceneLoad" "RuntimeDotEnv loads before the first scene"
  has Assets/App/Shared/Command/SpeechIntent/Runtime/RuntimeDotEnv.cs "Application.persistentDataPath" "RuntimeDotEnv reads the persistent-data path"
  lacks Packages/com.worldlabs.gaussian-splatting/Editor/WorldLabs/BuildEnvCopier.cs "StreamingAssets" "BuildEnvCopier no longer touches StreamingAssets"
  lacks Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/EnvLoader.cs "StreamingAssets" "EnvLoader no longer reads StreamingAssets"
  has README_Holodeck_DirectPlugin.md "Superseded" "direct-plugin README is marked superseded"
  lacks AGENTS.md "StreamingAssets/.env" "AGENTS.md no longer claims a StreamingAssets copy"
  lacks README.md "StreamingAssets/.env" "README no longer claims a StreamingAssets copy"
  lacks USER_MANUAL.md "StreamingAssets/.env" "USER_MANUAL no longer claims a StreamingAssets copy"
  has .env.example "HOLODECK_CREDENTIAL_MODE=device" ".env.example documents the device intent"
  has README.md "adb push .env /sdcard/Android/data/com.QLC.HeadsetHolodeck/files/.env" "README documents the device .env push"

  echo
  echo "== Wake-word alias set =="
  aliases="computer, pewter, pudor, peter, impewter, pumpor"
  for word in computer pewter pudor peter impewter pumpor; do
    if grep -q "^  - $word\$" Assets/App/Shared/Command/SpeechIntent/VoiceActivationConfig.asset; then
      pass "VoiceActivationConfig.asset lists alias '$word'"
    else
      fail "VoiceActivationConfig.asset is missing alias '$word'"
    fi
  done
  count="$(grep -c '^  - ' Assets/App/Shared/Command/SpeechIntent/VoiceActivationConfig.asset)"
  if [ "$count" -eq 6 ]; then
    pass "the alias list is exactly six deliberate aliases"
  else
    fail "expected exactly six aliases in VoiceActivationConfig.asset, found $count"
  fi
  for doc in README.md AGENTS.md USER_MANUAL.md; do
    has "$doc" "$aliases" "$doc reproduces the full deliberate alias set"
  done

  echo
  echo "== Vendored packages and versions =="
  has Packages/manifest.json '"com.worldlabs.gaussian-splatting": "file:com.worldlabs.gaussian-splatting"' "World Labs package is vendored"
  has Packages/manifest.json '"com.ponyudev.sherpa-onnx": "file:com.ponyudev.sherpa-onnx"' "Sherpa package is vendored"
  has README.md "Packages/com.worldlabs.gaussian-splatting" "README names the vendored World Labs path"
  has README.md "Packages/com.ponyudev.sherpa-onnx" "README names the vendored Sherpa path"
  check_manifest_version() { # key documented-in-agents
    local key="$1" doc="$2" version
    version="$(grep -F "\"$key\": \"" Packages/manifest.json | head -1 | sed 's/.*: "//; s/".*//')"
    if [ -z "$version" ]; then fail "manifest has no $key"; return; fi
    if grep -Fq "\`$key\` $version" "$doc"; then
      pass "AGENTS.md version for $key matches manifest ($version)"
    else
      fail "AGENTS.md version for $key does not match manifest ($version)"
    fi
  }
  check_manifest_version com.unity.xr.interaction.toolkit AGENTS.md
  check_manifest_version com.unity.xr.openxr AGENTS.md
  check_manifest_version com.unity.xr.hands AGENTS.md
  check_manifest_version com.unity.render-pipelines.universal AGENTS.md
  check_manifest_version com.unity.inputsystem AGENTS.md
  check_manifest_version com.unity.xr.androidxr-openxr AGENTS.md

  echo
  echo "== Save journal, object cache, providers =="
  has Assets/App/Shared/Save/Runtime/WorldConfigFile.cs '".bak"' "save journal rotates a .bak generation"
  has Assets/App/Shared/Save/Runtime/WorldConfigFile.cs "world.json" "save file is still world.json"
  has Assets/App/Shared/Save/Runtime/WorldConfigStore.cs 'Path.Combine(Application.persistentDataPath, "Worlds")' "saves live under persistentDataPath/Worlds"
  has Assets/App/Shared/Save/Runtime/WorldConfigStore.cs 'CachedObjectsPath' "store exposes a cached-objects path"
  has Assets/App/Shared/Scripts/Direct/ObjectGeneration/CachedObjectStore.cs "class CachedObjectStore" "object cache implementation exists"
  has Assets/App/Shared/Scripts/Direct/ObjectGeneration/HitemObjectGenerationProvider.cs "class HitemObjectGenerationProvider" "HiTem object provider exists"
  has Assets/App/Shared/Scripts/Direct/ObjectGeneration/ThreeDAIStudioObjectGenerationProvider.cs "class ThreeDAIStudioObjectGenerationProvider" "3dAIStudio object provider exists"
  has Assets/App/Shared/Command/SpeechIntent/Runtime/Audio/FreesoundProvider.cs "class FreesoundProvider" "Freesound provider exists"
  has Assets/App/Shared/Command/SpeechIntent/Runtime/Audio/XenoCantoSoundProvider.cs "class XenoCantoSoundProvider" "xeno-canto provider exists"
  has Assets/App/Shared/Command/SpeechIntent/Runtime/Audio/OpenverseProvider.cs "class OpenverseProvider" "Openverse audio provider exists"
  has Assets/App/Shared/Scripts/Direct/PixabayImageSearchService.cs "class PixabayImageSearchService" "Pixabay image search exists"
  if rg -q "class Meshy" Assets/App Packages 2>/dev/null; then
    fail "a Meshy provider class exists; docs must not call MESHY_API_KEY reserved"
  else
    pass "no Meshy provider class (MESHY_API_KEY/TRIPO_API_KEY are reserved keys)"
  fi
  has USER_MANUAL.md "reserved" "USER_MANUAL marks the unused object keys reserved"

  echo
  echo "== Hand interaction =="
  for f in \
    Assets/App/Shared/Command/SpeechIntent/Runtime/HandInput/HandOnlyInputCoordinator.cs \
    Assets/App/Shared/Command/SpeechIntent/Runtime/HandInput/XRHandsGestureSource.cs \
    Assets/App/Shared/Command/SpeechIntent/Runtime/WorldManipulationController.cs \
    Assets/App/Shared/Command/SpeechIntent/Runtime/SnapWhileGrabbed.cs ; do
    if [ -f "$f" ]; then pass "hand-interaction file present: $(basename "$f")"; else fail "missing $f"; fi
  done
  has USER_MANUAL.md "two-hand" "USER_MANUAL describes two-hand world manipulation"
  has USER_MANUAL.md "grab world" "USER_MANUAL documents the grab-world voice command"

  echo
  echo "== Scene wiring =="
  has Assets/Scenes/Holodeck.unity "SpeechIntent.VoiceActivation.VadAsrWakeTrigger" "scene hosts the VAD/ASR wake trigger"
  has Assets/Scenes/Holodeck.unity "SpeechIntent.HandInput.HandOnlyInputCoordinator" "scene hosts the hand-only input coordinator"
  has Assets/Scenes/Holodeck.unity "Holodeck.Save.WorldConfigStore" "scene hosts the world config store"
  has Assets/Scenes/Holodeck.unity "Holodeck.Direct.CachedObjectStore" "scene hosts the cached object store"
  has Assets/Scenes/Holodeck.unity "WorldLabs.Runtime.WorldLabsWorldManager" "scene hosts the World Labs world manager"
  has Assets/Scenes/Holodeck.unity "GeneratedWorldRoot" "scene has GeneratedWorldRoot"

  echo
  echo "== Secret hygiene of the changed documents =="
  if [ -f .env ]; then
    while IFS= read -r line; do
      case "$line" in ''|\#*) continue ;; esac
      key="${line%%=*}"
      value="${line#*=}"
      key="$(printf '%s' "$key" | tr -d '[:space:]')"
      value="$(printf '%s' "$value" | sed 's/^[[:space:]]*//; s/[[:space:]]*$//')"
      [ "${#value}" -lt 8 ] && continue
      # Only credential-shaped keys are secret; *_URL endpoint overrides are documented
      # configuration on purpose (they are what a distribution build embeds).
      case "$key" in
        *_URL) continue ;;
      esac
      upper="$(printf '%s' "$key" | tr '[:lower:]' '[:upper:]')"
      case "$upper" in
        *KEY*|*TOKEN*|*SECRET*|*PASSWORD*|*CREDENTIAL*|*AUTH*) ;;
        *) continue ;;
      esac
      hits=0
      for doc in AGENTS.md README.md USER_MANUAL.md README_Holodeck_DirectPlugin.md \
                 spz-renderer-handoff-notes.md .env.example \
                 docs/stabilization/STATUS.md docs/stabilization/handoff-06.md \
                 docs/project-learning-log.md ; do
        [ -f "$doc" ] || continue
        n="$(grep -F -c -- "$value" "$doc" 2>/dev/null || true)"
        hits=$((hits + ${n:-0}))
      done
      if [ "$hits" -eq 0 ]; then
        pass "no secret value for $key in the documentation set (counted, never printed)"
      else
        fail "the value of $key appears $hits time(s) in the documentation set"
      fi
    done < .env
  else
    echo "NOTE  no .env present; value scan skipped"
  fi

  echo
  echo "== Carried-forward status statements =="
  has docs/stabilization/handoff-06.md "unagreed" "handoff 06 marks the performance targets unagreed"
  has docs/stabilization/handoff-06.md "rotate" "handoff 06 carries the key-rotation recommendation"
  has docs/stabilization/handoff-06.md "headset-off" "handoff 06 carries the headset-off caveat"
  has docs/stabilization/STATUS.md "handoff-06" "STATUS points at handoff 06"
  has docs/stabilization/STATUS.md "07" "STATUS still lists order 07 separately"
  has docs/superpowers/plans/README.md "not a backlog" "plans directory disclaims unchecked boxes"
}

links() {
  echo "== Markdown link check (local targets only) =="
  tmp_list="$(mktemp)"
  find . -name '*.md' \
    -not -path './Library/*' -not -path './Temp/*' -not -path './Builds/*' \
    -not -path './.git/*' -not -path './Logs/*' > "$tmp_list"
  total=0
  broken=0
  while IFS= read -r file; do
    dir="$(dirname "$file")"
    # extract targets from markdown links, one per line
    sed -n 's/.*](\([^)]*\)).*/\1/p' "$file" | while IFS= read -r target; do
      case "$target" in
        http://*|https://*|mailto:*|codex:*|'') continue ;;
      esac
      path="${target%%#*}"
      [ -z "$path" ] && continue
      case "$path" in
        /*) resolved="$path" ;;
        *)  resolved="$dir/$path" ;;
      esac
      if [ ! -e "$resolved" ]; then
        printf 'BROKEN  %s -> %s\n' "$file" "$target"
      fi
    done
  done < "$tmp_list"
  echo "NOTE  historical handoffs intentionally reference since-deleted APK artifacts (handoff 05b records the cleanup)."
  rm -f "$tmp_list"
}

echo "Headset Holodeck order 06 — docs claim audit"
echo "date: $(date -u +%Y-%m-%dT%H:%M:%SZ)  host: $(uname -s)/$(uname -m)  mode: $MODE"
echo

case "$MODE" in
  claims) claims ;;
  links)  links ;;
  all)    claims; echo; links ;;
esac

if [ "$MODE" = "claims" ]; then
  echo
  echo "SUMMARY checks=$CHECKS failures=$FAILS"
fi
exit 0
