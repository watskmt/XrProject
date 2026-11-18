#!/usr/bin/env bash
#
# XREAL SDK 3.1.0 ships 8 AARs that all declare package="nrsdk.pack".
# The AGP that Unity 6000.5 uses rejects a namespace shared across modules:
#
#   Namespace 'nrsdk.pack' is used in multiple modules and/or libraries:
#   :nr_loader:, :nr_common:.
#
# Older Unity (2022.3 LTS, AGP 7.x) tolerates this, which is why the SDK ships this way.
# Each AAR carries an empty R.txt, no res/ directory, no compiled R class and no relative
# component names, so the package attribute is unused beyond namespacing: giving each AAR
# its own namespace is inert.
#
# Re-run this after updating the XREAL SDK, since it patches the embedded package in place.

set -euo pipefail

PLUGIN_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/Packages/com.xreal.xr/Runtime/Plugins/Android"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

patched=0
for aar in "$PLUGIN_DIR"/*.aar; do
    name="$(basename "$aar" .aar)"

    if ! unzip -p "$aar" AndroidManifest.xml 2>/dev/null | grep -q 'package="nrsdk.pack"'; then
        continue
    fi

    # nr_hand_tracking -> nrsdk.pack.nr_hand_tracking (dashes are illegal in Java packages)
    namespace="nrsdk.pack.$(echo "$name" | tr '-' '_')"

    rm -rf "${WORK:?}/x" && mkdir -p "$WORK/x"
    ( cd "$WORK/x" && unzip -qo "$aar" AndroidManifest.xml )
    sed -i '' "s|package=\"nrsdk.pack\"|package=\"$namespace\"|" "$WORK/x/AndroidManifest.xml"
    ( cd "$WORK/x" && zip -qj "$aar" AndroidManifest.xml )

    echo "  $name -> $namespace"
    patched=$((patched + 1))
done

echo "Patched $patched AAR(s)."
