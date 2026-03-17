#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

PROJECT_FILE="${ROOT_DIR}/src/Luban/Luban.csproj"
OUTPUT_DIR="${ROOT_DIR}/build/output"
CONFIGURATION="Release"

if [[ ! -f "${PROJECT_FILE}" ]]; then
  echo "[ERROR] Project file not found: ${PROJECT_FILE}"
  exit 1
fi

echo "[INFO] Root      : ${ROOT_DIR}"
echo "[INFO] Project   : ${PROJECT_FILE}"
echo "[INFO] Output    : ${OUTPUT_DIR}"
echo "[INFO] Config    : ${CONFIGURATION}"

if [[ -d "${OUTPUT_DIR}" ]]; then
  echo "[INFO] Cleaning output directory..."
  rm -rf "${OUTPUT_DIR}"
fi

mkdir -p "${OUTPUT_DIR}"

echo "[INFO] Restoring dependencies..."
dotnet restore "${PROJECT_FILE}"

echo "[INFO] Publishing Luban..."
dotnet publish "${PROJECT_FILE}" -c "${CONFIGURATION}" -o "${OUTPUT_DIR}" --self-contained false

echo
echo "[SUCCESS] Build completed."
if [[ -f "${OUTPUT_DIR}/Luban.exe" ]]; then
  echo "[INFO] Executable : ${OUTPUT_DIR}/Luban.exe"
fi
if [[ -f "${OUTPUT_DIR}/Luban.dll" ]]; then
  echo "[INFO] DLL entry   : ${OUTPUT_DIR}/Luban.dll"
  echo "[INFO] Run with    : dotnet \"${OUTPUT_DIR}/Luban.dll\" --help"
fi
