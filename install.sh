#!/usr/bin/env bash
set -euo pipefail

repo="Jcardif/azimg-cli"
version="latest"
install_dir="$HOME/.local/bin"
force="false"
dry_run="false"

usage() {
  cat <<'USAGE'
Usage: install.sh [options]

Options:
  --version <version>      Release tag or version. Default: latest.
  --install-dir <path>     Install directory. Default: $HOME/.local/bin.
  --force                  Overwrite an existing azimg executable.
  --dry-run                Print what would happen without changing files.
  -h, --help               Show this help.
USAGE
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --version)
      version="$2"
      shift 2
      ;;
    --install-dir)
      install_dir="$2"
      shift 2
      ;;
    --force)
      force="true"
      shift
      ;;
    --dry-run)
      dry_run="true"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

detect_rid() {
  os_name="$(uname -s)"
  arch_name="$(uname -m)"
  case "$os_name:$arch_name" in
    Darwin:arm64|Darwin:aarch64)
      echo "osx-arm64"
      ;;
    Darwin:x86_64|Darwin:amd64)
      echo "osx-x64"
      ;;
    Linux:x86_64|Linux:amd64)
      echo "linux-x64"
      ;;
    *)
      echo "Unsupported platform: $os_name $arch_name" >&2
      exit 1
      ;;
  esac
}

download_file() {
  url="$1"
  output="$2"
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL "$url" -o "$output"
  elif command -v wget >/dev/null 2>&1; then
    wget -q "$url" -O "$output"
  else
    echo "Install requires curl or wget." >&2
    exit 1
  fi
}

sha256_file() {
  file="$1"
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$file" | awk '{ print $1 }'
  else
    sha256sum "$file" | awk '{ print $1 }'
  fi
}

rid="$(detect_rid)"
asset="azimg-$rid.tar.gz"

if [ "$version" = "latest" ]; then
  release_base="https://github.com/$repo/releases/latest/download"
  release_label="latest"
else
  case "$version" in
    v*) tag="$version" ;;
    *) tag="v$version" ;;
  esac
  release_base="https://github.com/$repo/releases/download/$tag"
  release_label="$tag"
fi

target="$install_dir/azimg"
metadata_dir="$HOME/.azimg"
metadata_path="$metadata_dir/metadata.json"

echo "azimg installer"
echo "  release: $release_label"
echo "  rid:     $rid"
echo "  target:  $target"

if [ "$dry_run" = "true" ]; then
  exit 0
fi

if [ -e "$target" ] && [ "$force" != "true" ]; then
  echo "azimg already exists at $target. Re-run with --force to overwrite it." >&2
  exit 3
fi

tmp_root="$(mktemp -d "${TMPDIR:-/tmp}/azimg-install.XXXXXX")"
trap 'rm -rf "$tmp_root"' EXIT

archive_path="$tmp_root/$asset"
sums_path="$tmp_root/SHA256SUMS"
extract_dir="$tmp_root/extract"

download_file "$release_base/$asset" "$archive_path"
download_file "$release_base/SHA256SUMS" "$sums_path"

expected_hash="$(grep "  $asset$" "$sums_path" | awk '{ print $1 }' | head -n 1)"
if [ -z "$expected_hash" ]; then
  echo "Could not find $asset in SHA256SUMS." >&2
  exit 1
fi

actual_hash="$(sha256_file "$archive_path")"
if [ "$expected_hash" != "$actual_hash" ]; then
  echo "Checksum mismatch for $asset." >&2
  echo "Expected: $expected_hash" >&2
  echo "Actual:   $actual_hash" >&2
  exit 1
fi

mkdir -p "$extract_dir" "$install_dir" "$metadata_dir"
tar -xzf "$archive_path" -C "$extract_dir"
source_exe="$(find "$extract_dir" -type f -name azimg -print -quit)"
if [ -z "$source_exe" ]; then
  echo "The archive did not contain azimg." >&2
  exit 1
fi

cp "$source_exe" "$target"
chmod +x "$target"

cat > "$metadata_path" <<EOF
{
  "schemaVersion": 1,
  "install": {
    "schemaVersion": 1,
    "installPath": "$target",
    "rid": "$rid",
    "installedVersion": "$release_label",
    "sourceRepository": "$repo",
    "installMethod": "install.sh",
    "updatedAtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  },
  "update": {
    "schemaVersion": 1
  }
}
EOF

echo "Installed azimg to $target"
case ":$PATH:" in
  *":$install_dir:"*) ;;
  *) echo "Add $install_dir to PATH to run azimg from any shell." ;;
esac
"$target" version
