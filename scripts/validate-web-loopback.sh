#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/validate-web-loopback.sh [options]

Options:
  --port PORT             Loopback port. Default: 8787.
  --base-url URL          Full loopback URL. Default: http://127.0.0.1:<port>.
  --project PATH          Web project path. Default: src/CodexWidget.Web/CodexWidget.Web.csproj.
  --timeout-seconds N     Startup timeout. Default: 90.
  -h, --help              Show this help.

Thin loopback smoke helper for CodexWidget.Web. It starts the web host on
127.0.0.1, waits for /health and /health/status to respond successfully, then
shuts the process down.

The script resolves the repository root relative to its own location, so it can
be run from any working directory. Required tools: bash, dotnet, and curl.
EOF
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "${script_dir}/.." && pwd -P)"

resolve_path() {
  local path="$1"
  if [[ "${path}" = /* ]]; then
    printf '%s\n' "${path}"
  else
    printf '%s\n' "${repo_root}/${path}"
  fi
}

project_path="src/CodexWidget.Web/CodexWidget.Web.csproj"
port="8787"
base_url=""
timeout_seconds="90"

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    --port)
      port="${2:-}"
      if [[ -z "${port}" ]]; then
        echo "Missing value for --port" >&2
        exit 1
      fi
      shift 2
      ;;
    --base-url)
      base_url="${2:-}"
      if [[ -z "${base_url}" ]]; then
        echo "Missing value for --base-url" >&2
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
    --timeout-seconds)
      timeout_seconds="${2:-}"
      if [[ -z "${timeout_seconds}" ]]; then
        echo "Missing value for --timeout-seconds" >&2
        exit 1
      fi
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

project_path="$(resolve_path "${project_path}")"
if [[ -z "${base_url}" ]]; then
  base_url="http://127.0.0.1:${port}"
fi
log_dir="${repo_root}/artifacts/logs"
mkdir -p "${log_dir}"
log_file="${log_dir}/web-loopback-$(date +%Y%m%d-%H%M%S).log"

echo "Repository root: ${repo_root}"
echo "Project: ${project_path}"
echo "Loopback URL: ${base_url}"
echo "Log file: ${log_file}"

cleanup() {
  if [[ -n "${app_pid:-}" ]] && kill -0 "${app_pid}" >/dev/null 2>&1; then
    kill "${app_pid}" >/dev/null 2>&1 || true
    wait "${app_pid}" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

CodexWidgetWeb__BindUrls="${base_url}" \
CodexWidgetWeb__AllowLanBinding="false" \
ASPNETCORE_URLS="${base_url}" \
dotnet run --project "${project_path}" >"${log_file}" 2>&1 &
app_pid=$!

deadline=$((SECONDS + timeout_seconds))
while (( SECONDS < deadline )); do
  if curl -fsS "${base_url}/health" >/dev/null && curl -fsS "${base_url}/health/status" >/dev/null; then
    echo "Loopback validation passed."
    exit 0
  fi
  if ! kill -0 "${app_pid}" >/dev/null 2>&1; then
    echo "Web host exited early. See ${log_file}" >&2
    exit 1
  fi
  sleep 1
done

echo "Timed out waiting for loopback health checks. See ${log_file}" >&2
exit 1
