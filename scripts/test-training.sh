#!/usr/bin/env bash
set -euo pipefail

# Explicitly run later, after approval. Docker supplies Linux/amd64 PyTorch on x86_64 macOS.
cd "$(dirname "$0")/.."
docker build --platform linux/amd64 -f training/Dockerfile -t gomoku-training-test:local training
docker run --rm --platform linux/amd64 --entrypoint python gomoku-training-test:local \
  -m unittest discover -s training/tests -v
