#!/usr/bin/env bash
set -euo pipefail

publish_dir="${1:?publish directory is required}"
output_dir="${2:?output directory is required}"
version="${3:?version is required}"
rid="${4:?runtime identifier is required}"
mode="${5:?mode is required}"

if [[ ! -d "$publish_dir" ]]; then
  echo "Publish directory '$publish_dir' was not found." >&2
  exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
linux_assets_dir="$script_dir/linux"
workdir="${TMPDIR:-/tmp}/monica-appimage-${rid}-${mode}-$$"
appdir="$workdir/Monica.AppDir"
tools_dir="$workdir/tools"

cleanup() {
  rm -rf "$workdir"
}
trap cleanup EXIT

rm -rf "$workdir"
mkdir -p "$appdir/usr/bin" "$appdir/usr/lib/monica" "$appdir/usr/share/applications" \
  "$appdir/usr/share/icons/hicolor/256x256/apps" "$appdir/usr/share/metainfo" "$tools_dir" "$output_dir"

cp -a "$publish_dir/." "$appdir/usr/lib/monica/"
chmod +x "$appdir/usr/lib/monica/Monica.App" || true

cat > "$appdir/AppRun" <<'EOF'
#!/usr/bin/env sh
set -eu
HERE="$(dirname "$(readlink -f "$0")")"
export PATH="$HERE/usr/bin:$PATH"
exec "$HERE/usr/lib/monica/Monica.App" "$@"
EOF
chmod 0755 "$appdir/AppRun"

cat > "$appdir/usr/bin/monica" <<'EOF'
#!/usr/bin/env sh
set -eu
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/../lib/monica/Monica.App" "$@"
EOF
chmod 0755 "$appdir/usr/bin/monica"

cp "$linux_assets_dir/monica.desktop" "$appdir/monica.desktop"
cp "$linux_assets_dir/monica.desktop" "$appdir/usr/share/applications/monica.desktop"
cp "$linux_assets_dir/monica.png" "$appdir/monica.png"
cp "$linux_assets_dir/monica.png" "$appdir/usr/share/icons/hicolor/256x256/apps/monica.png"
cp "$linux_assets_dir/com.monicapass.Monica.metainfo.xml" "$appdir/usr/share/metainfo/com.monicapass.Monica.metainfo.xml"

arch="x86_64"
case "$rid" in
  linux-x64) arch="x86_64" ;;
  linux-arm64) arch="aarch64" ;;
  *) echo "Unsupported Linux runtime identifier '$rid'." >&2; exit 1 ;;
esac

appimagetool_url="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-${arch}.AppImage"
appimagetool="$tools_dir/appimagetool.AppImage"
curl -fsSL "$appimagetool_url" -o "$appimagetool"
chmod +x "$appimagetool"

appimage_path="$output_dir/Monica-${version}-${rid}-${mode}.AppImage"
ARCH="$arch" "$appimagetool" --appimage-extract-and-run "$appdir" "$appimage_path"
chmod +x "$appimage_path"
echo "Created $appimage_path"
