#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

if [ ! -x .venv/bin/python ]; then
    trap 'rm -rf .venv' ERR
    if command -v uv >/dev/null; then
        uv venv --python 3.13 .venv
        uv pip install --python .venv/bin/python -r requirements.txt
    else
        python3 -m venv .venv
        .venv/bin/pip install -r requirements.txt
    fi
    trap - ERR
fi

exec .venv/bin/python tracker.py --show "$@"
