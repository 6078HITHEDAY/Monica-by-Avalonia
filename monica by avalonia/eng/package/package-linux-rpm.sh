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

if ! command -v rpmbuild >/dev/null; then
  echo "rpmbuild is required. Install the 'rpm' package (Debian/Ubuntu) or 'rpm-build' (Fedora)." >&2
  exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
linux_assets_dir="$script_dir/linux"

arch="x86_64"
case "$rid" in
  linux-x64) arch="x86_64" ;;
  linux-arm64) arch="aarch64" ;;
  *) echo "Unsupported Linux runtime identifier '$rid'." >&2; exit 1 ;;
esac

# RPM Version cannot contain '-' or some other characters. Keep a sanitized
# upstream version and fold any remainder into Release.
raw_version="$version"
rpm_version="${raw_version%%-*}"
if [[ -z "$rpm_version" ]]; then
  rpm_version="0.0.0"
fi
rpm_version="${rpm_version//[^A-Za-z0-9._]/}"
if [[ "$raw_version" == *"-"* ]]; then
  suffix="${raw_version#*-}"
  suffix="${suffix//[^A-Za-z0-9._]/}"
  rpm_release="1.${suffix}.${mode}"
else
  rpm_release="1.${mode}"
fi

package_name="monica"
workdir="${TMPDIR:-/tmp}/monica-rpm-${rid}-${mode}-$$"
rpmbuild_top="$workdir/rpmbuild"
spec_path="$workdir/${package_name}.spec"
staging_root="$workdir/staging"

cleanup() {
  rm -rf "$workdir"
}
trap cleanup EXIT

rm -rf "$workdir"
mkdir -p \
  "$rpmbuild_top"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS} \
  "$staging_root/usr/lib/monica" \
  "$staging_root/usr/bin" \
  "$staging_root/usr/share/applications" \
  "$staging_root/usr/share/icons/hicolor/256x256/apps" \
  "$staging_root/usr/share/metainfo" \
  "$output_dir"

cp -a "$publish_dir/." "$staging_root/usr/lib/monica/"
chmod +x "$staging_root/usr/lib/monica/Monica.App" || true

cat > "$staging_root/usr/bin/monica" <<'EOF'
#!/usr/bin/env sh
exec /usr/lib/monica/Monica.App "$@"
EOF
chmod 0755 "$staging_root/usr/bin/monica"

cp "$linux_assets_dir/monica.desktop" "$staging_root/usr/share/applications/monica.desktop"
cp "$linux_assets_dir/monica.png" "$staging_root/usr/share/icons/hicolor/256x256/apps/monica.png"
cp "$linux_assets_dir/com.monicapass.Monica.metainfo.xml" \
  "$staging_root/usr/share/metainfo/com.monicapass.Monica.metainfo.xml"

cat > "$spec_path" <<EOF
Name:           ${package_name}
Version:        ${rpm_version}
Release:        ${rpm_release}
Summary:        Monica password and secure vault manager
License:        GPL-3.0-or-later
URL:            https://github.com/6078HITHEDAY/Monica-by-Avalonia
BuildArch:      ${arch}
AutoReqProv:    no

Requires:       libX11
Requires:       libICE
Requires:       libSM
Requires:       fontconfig
Requires:       libsecret

%description
Monica by Avalonia desktop package built in ${mode} mode from ${rid}.
Sensitive settings use the desktop Secret Service (libsecret). Tray support
depends on StatusNotifier/AppIndicator.

%install
rm -rf %{buildroot}
mkdir -p %{buildroot}
cp -a ${staging_root}/. %{buildroot}/

%files
%defattr(-,root,root,-)
/usr/bin/monica
/usr/lib/monica
/usr/share/applications/monica.desktop
/usr/share/icons/hicolor/256x256/apps/monica.png
/usr/share/metainfo/com.monicapass.Monica.metainfo.xml

%changelog
* $(date -u '+%a %b %d %Y') Monica Maintainers <maintainers@example.com> - ${rpm_version}-${rpm_release}
- Automated package built from ${rid} ${mode} publish output (${raw_version}).
EOF

rpmbuild \
  --define "_topdir ${rpmbuild_top}" \
  --define "_rpmdir ${rpmbuild_top}/RPMS" \
  --define "_srcrpmdir ${rpmbuild_top}/SRPMS" \
  --define "_builddir ${rpmbuild_top}/BUILD" \
  --define "_sourcedir ${rpmbuild_top}/SOURCES" \
  --define "_specdir ${rpmbuild_top}/SPECS" \
  --target "${arch}-linux" \
  -bb "$spec_path"

built_rpm="$(find "$rpmbuild_top/RPMS" -type f -name '*.rpm' | head -n 1)"
if [[ -z "$built_rpm" ]]; then
  echo "rpmbuild did not produce an RPM." >&2
  exit 1
fi

rpm_path="$output_dir/Monica-${raw_version}-${rid}-${mode}.rpm"
cp "$built_rpm" "$rpm_path"
echo "Created $rpm_path"
