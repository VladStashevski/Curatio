#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$root/documents"

for sample in complete needs-review; do
  (
    cd "$root/templates/$sample"
    zip -q -r "$root/documents/$sample.docx" .
  )
done
