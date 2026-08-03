#!/usr/bin/env bash
#
# Builds AgentDeck in Release configuration and installs it into the system.
#
# Usage:
#   ./scripts/install.sh                 # install into ~/.local (no sudo)
#   ./scripts/install.sh --system        # install into /usr/local (needs sudo)
#   ./scripts/install.sh --prefix /opt/x # install into a custom prefix
#   ./scripts/install.sh --uninstall     # remove an installation
#
set -euo pipefail

readonly APP_NAME="AgentDeck"
readonly BIN_NAME="agentdeck"
readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
readonly PROJECT="${REPO_ROOT}/src/AgentDeck/AgentDeck.csproj"

PREFIX="${HOME}/.local"
UNINSTALL=0

die() { printf '\033[31merror:\033[0m %s\n' "$*" >&2; exit 1; }
info() { printf '\033[36m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[33mwarning:\033[0m %s\n' "$*" >&2; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --system)    PREFIX="/usr/local"; shift ;;
        --prefix)    PREFIX="${2:?--prefix requires a path}"; shift 2 ;;
        --uninstall) UNINSTALL=1; shift ;;
        -h|--help)   sed -n '2,10p' "${BASH_SOURCE[0]}" | sed 's/^# \?//'; exit 0 ;;
        *)           die "unknown option: $1 (try --help)" ;;
    esac
done

readonly BIN_DIR="${PREFIX}/bin"
readonly DESKTOP_DIR="${PREFIX}/share/applications"
readonly DESKTOP_FILE="${DESKTOP_DIR}/${BIN_NAME}.desktop"
readonly TARGET="${BIN_DIR}/${BIN_NAME}"
readonly ICON_ROOT="${PREFIX}/share/icons/hicolor"
readonly ICON_SOURCE_DIR="${REPO_ROOT}/assets/icons"
readonly ICON_SIZES=(16 22 24 32 48 64 128 256 512)

# Writing outside $HOME normally needs root, so re-exec through sudo.
SUDO=""
if [[ ! -w "$(dirname -- "${PREFIX}")" && ! -w "${PREFIX}" ]]; then
    command -v sudo >/dev/null || die "${PREFIX} is not writable and sudo is unavailable"
    SUDO="sudo"
fi

# Desktop shells serve launchers and icons from caches, so an install stays
# invisible (or keeps the previous icon) until those are rebuilt. KDE's cache
# belongs to the current user, hence no sudo for it.
refresh_caches() {
    command -v update-desktop-database >/dev/null &&
        ${SUDO} update-desktop-database -q "${DESKTOP_DIR}" 2>/dev/null || true
    command -v gtk-update-icon-cache >/dev/null &&
        ${SUDO} gtk-update-icon-cache -qtf "${ICON_ROOT}" 2>/dev/null || true
    command -v kbuildsycoca6 >/dev/null &&
        kbuildsycoca6 --noincremental >/dev/null 2>&1 || true
}

if [[ ${UNINSTALL} -eq 1 ]]; then
    info "Removing ${TARGET} and ${DESKTOP_FILE}"
    ${SUDO} rm -f -- "${TARGET}" "${DESKTOP_FILE}"

    info "Removing icons from ${ICON_ROOT}"
    for size in "${ICON_SIZES[@]}"; do
        ${SUDO} rm -f -- "${ICON_ROOT}/${size}x${size}/apps/${BIN_NAME}.png"
    done

    refresh_caches
    info "${APP_NAME} uninstalled."
    exit 0
fi

command -v dotnet >/dev/null || die ".NET SDK not found; install .NET 10 SDK first"
[[ -f "${PROJECT}" ]] || die "project not found: ${PROJECT}"

case "$(uname -m)" in
    x86_64)         RID="linux-x64" ;;
    aarch64|arm64)  RID="linux-arm64" ;;
    *)              die "unsupported architecture: $(uname -m)" ;;
esac

STAGE="$(mktemp -d)"
trap 'rm -rf -- "${STAGE}"' EXIT

info "Building ${APP_NAME} (Release, ${RID})"
dotnet publish "${PROJECT}" \
    --configuration Release \
    --runtime "${RID}" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    --output "${STAGE}" \
    --nologo

[[ -f "${STAGE}/${APP_NAME}" ]] || die "build produced no ${APP_NAME} binary"

info "Installing to ${TARGET}"
${SUDO} install -Dm755 "${STAGE}/${APP_NAME}" "${TARGET}"

# The launcher and the window manager pick the icon up from the hicolor theme by
# its name, so every prerendered size goes into the matching directory.
info "Installing icons to ${ICON_ROOT}"
for size in "${ICON_SIZES[@]}"; do
    icon="${ICON_SOURCE_DIR}/${BIN_NAME}-${size}.png"
    [[ -f "${icon}" ]] ||
        { warn "missing icon size: ${icon}"; continue; }
    ${SUDO} install -Dm644 "${icon}" "${ICON_ROOT}/${size}x${size}/apps/${BIN_NAME}.png"
done

# A launcher entry so the app shows up in the desktop menu, not just in $PATH.
info "Installing desktop entry to ${DESKTOP_FILE}"
${SUDO} install -d "${DESKTOP_DIR}"
${SUDO} tee "${DESKTOP_FILE}" >/dev/null <<EOF
[Desktop Entry]
Type=Application
Name=${APP_NAME}
Comment=A desktop cockpit for console LLM agents
Exec=${TARGET}
Icon=${BIN_NAME}
Terminal=false
Categories=Development;
Keywords=terminal;agent;llm;claude;codex;
StartupWMClass=${BIN_NAME}
EOF

refresh_caches

case ":${PATH}:" in
    *":${BIN_DIR}:"*) ;;
    *) warn "${BIN_DIR} is not in your PATH; add it to your shell profile:"
       warn "  export PATH=\"${BIN_DIR}:\$PATH\"" ;;
esac

info "Done. Run '${BIN_NAME}' or launch ${APP_NAME} from your application menu."
