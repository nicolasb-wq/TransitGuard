# ADR-0012 — Deployment: rsync + Release-Symlinks + systemd; kein CI-Dienst
**Kontext:** Constraints: rsync über SSH, keine GH Actions/Registry/CI; Hetzner-VM.
**Optionen:** (a) Release-Verzeichnisse + current-Symlink + systemd + lokale Test-Pipeline, (b) Docker auf dem Server, (c) Git-Pull-Deploy, (d) Blue/Green-VMs.
**Entscheidung:** (a). Build lokal (dotnet publish self-contained), Test-Gate scripts/test.sh vor jedem Deploy, Health-Check + automatischer Symlink-Rollback.
**Konsequenzen:** Rollback <30 s; kein Container-Betrieb nötig (eine App, eine DB); Prozess-Split API/Ingest später durch zweite systemd-Unit desselben Builds möglich (Konfiguration, kein Umbau).
