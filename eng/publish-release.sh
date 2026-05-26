#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="$repo_root/src/AzImg.Cli/AzImg.Cli.csproj"
rid=""
version="0.2.1"
output_root="$repo_root/artifacts"
mode="single-file"

usage() {
  cat <<'USAGE'
Usage: eng/publish-release.sh --rid <rid> [options]

Options:
  --rid <rid>          Target RID: win-x64, win-arm64, osx-arm64, or linux-x64.
  --version <version>  Version to stamp into the published binary. Default: 0.2.1.
  --output <path>      Artifact root. Default: ./artifacts.
  --mode <mode>        single-file. Default: single-file.
  -h, --help           Show this help.
USAGE
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --rid)
      rid="$2"
      shift 2
      ;;
    --version)
      version="$2"
      shift 2
      ;;
    --output)
      output_root="$2"
      shift 2
      ;;
    --mode)
      mode="$2"
      shift 2
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

if [ -z "$rid" ]; then
  echo "--rid is required." >&2
  usage >&2
  exit 1
fi

case "$output_root" in
  /*) ;;
  *) output_root="$repo_root/$output_root" ;;
esac

case "$rid" in
  win-x64|win-arm64)
    executable="azimg.exe"
    archive_type="zip"
    ;;
  osx-arm64|linux-x64)
    executable="azimg"
    archive_type="tar.gz"
    ;;
  *)
    echo "Unsupported RID: $rid" >&2
    exit 1
    ;;
esac

publish_dir="$output_root/publish/$rid"
package_dir="$output_root/packages"
rm -rf "$publish_dir"
mkdir -p "$publish_dir" "$package_dir"

publish_args=(
  publish "$project_path"
  --configuration Release
  --runtime "$rid"
  --self-contained true
  --output "$publish_dir"
  -p:Version="$version"
  -p:PublishSingleFile=true
  -p:EnableCompressionInSingleFile=true
)

if [ "$mode" != "single-file" ]; then
  echo "Unsupported mode: $mode" >&2
  exit 1
fi

dotnet "${publish_args[@]}"

archive_path="$package_dir/azimg-$rid.$archive_type"
rm -f "$archive_path"

if [ "$archive_type" = "zip" ]; then
  (cd "$publish_dir" && zip -q "$archive_path" "$executable")
else
  chmod +x "$publish_dir/$executable"
  (cd "$publish_dir" && tar -czf "$archive_path" "$executable")
fi

if command -v shasum >/dev/null 2>&1; then
  (cd "$package_dir" && shasum -a 256 "$(basename "$archive_path")" > "azimg-$rid.sha256")
else
  (cd "$package_dir" && sha256sum "$(basename "$archive_path")" > "azimg-$rid.sha256")
fi

echo "$archive_path"
