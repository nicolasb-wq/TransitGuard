#!/usr/bin/env bash
# Lokales Test-Gate (kein CI-Dienst — ADR-0012). deploy.sh verlangt deren Erfolg.
set -euo pipefail
dotnet test tests/TransitGuard.Core.Tests -c Release
dotnet test tests/TransitGuard.Api.Tests -c Release
npm --prefix web run --silent test --if-present
dotnet build TransitGuard.sln -c Release --nologo -v q
echo "ALL TESTS GREEN $(date -u +%FT%TZ)" > .test-gate
