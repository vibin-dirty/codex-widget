#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/publish-win-x64-package.sh [options]

Options:
  --configuration NAME      Build configuration. Default: Release.
  --runtime RID             .NET runtime identifier. Default: win-x64.
  --artifact-set NAME       Artifact folder under artifacts/. Default: desktop.
  --project PATH            Desktop project path. Default: src/CodexWidget.App/CodexWidget.App.csproj.
  --publish-dir PATH        Publish output directory. Default: artifacts/publish/<artifact-set>/<runtime>.
  --package-path PATH       Package archive path. Default: artifacts/packages/<artifact-set>/codex-widget-<runtime>-<kind>.zip.
  --self-contained BOOL     Publish self-contained output. Default: true.
  --package                 Create a zip package. Default.
  --no-package              Skip package creation.
  -h, --help                Show this help.

Required tools: bash, dotnet, and either zip or bsdtar when packaging is enabled.
EOF
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "${script_dir}/.." && pwd -P)"

create_zip() {
  local source_dir="$1"
  local archive_path="$2"

  mkdir -p "$(dirname "${archive_path}")"
  (
    cd "${source_dir}"
    if command -v zip >/dev/null 2>&1; then
      zip -qr "${archive_path}" .
    elif command -v bsdtar >/dev/null 2>&1; then
      bsdtar -a -cf "${archive_path}" .
    else
      echo "No archive tool found. Install zip or bsdtar, or rerun with --no-package." >&2
      exit 1
    fi
  )
}

resolve_path() {
  local path="$1"
  if [[ "${path}" = /* ]]; then
    printf '%s\n' "${path}"
  else
    printf '%s\n' "${repo_root}/${path}"
  fi
}

runtime="win-x64"
configuration="Release"
artifact_set="desktop"
project_path="src/CodexWidget.App/CodexWidget.App.csproj"
publish_dir=""
package_path=""
self_contained="true"
package_output="1"

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    --configuration)
      configuration="${2:-}"
      if [[ -z "${configuration}" ]]; then
        echo "Missing value for --configuration" >&2
        exit 1
      fi
      shift 2
      ;;
    --runtime)
      runtime="${2:-}"
      if [[ -z "${runtime}" ]]; then
        echo "Missing value for --runtime" >&2
        exit 1
      fi
      shift 2
      ;;
    --artifact-set)
      artifact_set="${2:-}"
      if [[ -z "${artifact_set}" ]]; then
        echo "Missing value for --artifact-set" >&2
        exit 1
      fi
      shift 2
      ;;
    --project)
      project_path="${2:-}"
      if [[ -z "${project_path}" ]]; then
        echo "Missing value for --project" >&2
        exit 1
      fi
      shift 2
      ;;
    --publish-dir)
      publish_dir="${2:-}"
      if [[ -z "${publish_dir}" ]]; then
        echo "Missing value for --publish-dir" >&2
        exit 1
      fi
      shift 2
      ;;
    --package-path)
      package_path="${2:-}"
      if [[ -z "${package_path}" ]]; then
        echo "Missing value for --package-path" >&2
        exit 1
      fi
      shift 2
      ;;
    --self-contained)
      self_contained="${2:-}"
      if [[ "${self_contained}" != "true" && "${self_contained}" != "false" ]]; then
        echo "Expected --self-contained to be true or false" >&2
        exit 1
      fi
      shift 2
      ;;
    --package)
      package_output="1"
      shift
      ;;
    --no-package)
      package_output="0"
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

project_path="$(resolve_path "${project_path}")"
if [[ -z "${publish_dir}" ]]; then
  publish_dir="${repo_root}/artifacts/publish/${artifact_set}/${runtime}"
else
  publish_dir="$(resolve_path "${publish_dir}")"
fi

if [[ -z "${package_path}" ]]; then
  publish_kind="framework-dependent"
  if [[ "${self_contained}" == "true" ]]; then
    publish_kind="self-contained"
  fi
  package_path="${repo_root}/artifacts/packages/${artifact_set}/codex-widget-${runtime}-${publish_kind}.zip"
else
  package_path="$(resolve_path "${package_path}")"
fi

echo "Repository root: ${repo_root}"
echo "Project: ${project_path}"
echo "Runtime: ${runtime}"
echo "Configuration: ${configuration}"
echo "Self-contained: ${self_contained}"
echo "Preparing publish output at: ${publish_dir}"
rm -rf "${publish_dir}"
mkdir -p "${publish_dir}"

echo "Restoring ${project_path} for ${runtime}"
dotnet restore "${project_path}" -r "${runtime}"

echo "Publishing ${project_path}"
dotnet publish "${project_path}" \
  -c "${configuration}" \
  -r "${runtime}" \
  --self-contained "${self_contained}" \
  -o "${publish_dir}"

if [[ "${package_output}" == "1" ]]; then
  echo "Creating package: ${package_path}"
  rm -f "${package_path}"
  create_zip "${publish_dir}" "${package_path}"
else
  echo "Package archive: disabled"
fi

echo "Publish directory: ${publish_dir}"
if [[ "${package_output}" == "1" ]]; then
  echo "Package archive: ${package_path}"
fi
