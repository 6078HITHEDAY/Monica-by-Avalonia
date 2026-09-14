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

case "$rid" in
  linux-x64|linux-arm64) ;;
  *) echo "Unsupported Linux runtime identifier '$rid'." >&2; exit 1 ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
linux_assets_dir="$script_dir/linux"
workdir="${TMPDIR:-/tmp}/monica-flatpak-${rid}-${mode}-$$"
repo_dir="$workdir/repo"
build_dir="$workdir/build"
state_dir="$workdir/state"
export FLATPAK_USER_DIR="$workdir/flatpak-user"

cleanup() {
  rm -rf "$workdir"
}
trap cleanup EXIT

rm -rf "$workdir"
mkdir -p "$workdir/sources/publish" "$repo_dir" "$build_dir" "$state_dir" "$output_dir" "$FLATPAK_USER_DIR"

cp -a "$publish_dir/." "$workdir/sources/publish/"
cp "$linux_assets_dir/monica.png" "$workdir/sources/monica.png"
cp "$linux_assets_dir/com.monicapass.Monica.metainfo.xml" "$workdir/sources/com.monicapass.Monica.metainfo.xml"
cp "$linux_assets_dir/com.monicapass.Monica.yml" "$workdir/sources/com.monicapass.Monica.yml"

cat > "$workdir/sources/monica-wrapper" <<'EOF'
#!/usr/bin/env sh
set -eu
exec /app/lib/monica/Monica.App "$@"
EOF
chmod 0755 "$workdir/sources/monica-wrapper"

cat > "$workdir/sources/monica.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Monica
Comment=Monica password and secure vault manager
Exec=monica
Icon=com.monicapass.Monica
Terminal=false
Categories=Utility;Security;
StartupWMClass=monica
EOF

if ! command -v flatpak >/dev/null || ! command -v flatpak-builder >/dev/null; then
  echo "flatpak and flatpak-builder are required to create the Flatpak bundle." >&2
  exit 1
fi

flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak install -y --user flathub \
  org.freedesktop.Platform//24.08 \
  org.freedesktop.Sdk//24.08

flatpak-builder \
  --user \
  --force-clean \
  --state-dir="$state_dir" \
  --repo="$repo_dir" \
  "$build_dir" \
  "$workdir/sources/com.monicapass.Monica.yml"

bundle_path="$output_dir/Monica-${version}-${rid}-${mode}.flatpak"
flatpak build-bundle "$repo_dir" "$bundle_path" com.monicapass.Monica
echo "Created $bundle_path"
