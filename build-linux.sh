#!/usr/bin/env bash
# =============================================================================
# build-linux.sh — Package PhotoComp for Linux x64
#
# Usage:
#   ./build-linux.sh
#   ./build-linux.sh 1.2.0
#
# Produces:  dist/PhotoComp-linux-x64-v<version>.zip
# Requires:  dotnet SDK 10+, zip
# =============================================================================
set -euo pipefail

VERSION="${1:-1.0.0}"

PROJECT_NAME="PhotoComp"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_FILE="$SCRIPT_DIR/PhotoComp/PhotoComp.csproj"
RUNTIME="linux-x64"
DIST_DIR="$SCRIPT_DIR/dist"
PUBLISH_DIR="$DIST_DIR/$RUNTIME"
ZIP_NAME="${PROJECT_NAME}-linux-x64-v${VERSION}.zip"
ZIP_PATH="$DIST_DIR/$ZIP_NAME"

echo ""
echo "=== PhotoComp Linux Packaging ==="
echo "  Version    : $VERSION"
echo "  Runtime    : $RUNTIME"
echo "  Output zip : dist/$ZIP_NAME"
echo ""

# ── 1. Clean previous artifacts ───────────────────────────────────────────────
if [ -d "$PUBLISH_DIR" ]; then
    echo "Cleaning $PUBLISH_DIR ..."
    rm -rf "$PUBLISH_DIR"
fi
if [ -f "$ZIP_PATH" ]; then
    echo "Removing old $ZIP_NAME ..."
    rm -f "$ZIP_PATH"
fi
mkdir -p "$DIST_DIR"

# ── 2. Restore & publish ──────────────────────────────────────────────────────
echo "Publishing (Release / self-contained) ..."

dotnet publish "$PROJECT_FILE" \
    --configuration Release \
    --runtime "$RUNTIME" \
    --self-contained true \
    -p:Version="$VERSION" \
    -p:PublishSingleFile=false \
    -p:PublishReadyToRun=true \
    --output "$PUBLISH_DIR"

# Make the binary executable (dotnet publish should set this, but be explicit)
chmod +x "$PUBLISH_DIR/$PROJECT_NAME" 2>/dev/null || true

# ── 3. Zip the publish directory ──────────────────────────────────────────────
echo ""
echo "Creating zip ..."

# Use a relative path inside the zip so it extracts into a single folder
(cd "$DIST_DIR" && zip -r "$ZIP_NAME" "$RUNTIME/")

SIZE_MB=$(du -m "$ZIP_PATH" | cut -f1)
echo ""
echo "Done!  dist/$ZIP_NAME  (~${SIZE_MB} MB)"
echo ""
