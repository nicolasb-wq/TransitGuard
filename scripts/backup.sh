#!/usr/bin/env bash
# Backup (10-deployment §8): pg_dump → lokal halten → rsync zur Storage Box (Rotation via --link-dest).
# Nutzung auf dem SERVER (cron 03:00): backup.sh [keep_local=7]
set -euo pipefail
KEEP="${1:-7}"
ROOT=/opt/transitguard
STAMP=$(date -u +%Y%m%d-%H%M%S)
DEST="$ROOT/shared/backups"
BOX="${BACKUP_BOX:-}"   # z.B. "u123456@u123456.your-storagebox.de:transitguard" (ssh-key hinterlegt)

mkdir -p "$DEST"
sudo -u postgres pg_dump -Fc transitguard -f "$DEST/tg-$STAMP.dump"
gpg --symmetric --yes -o "$DEST/tg-$STAMP.dump.gpg" "$DEST/tg-$STAMP.dump" && rm "$DEST/tg-$STAMP.dump"   # Backup-Verschlüsselung (DSFA §2)

ls -1t "$DEST"/tg-*.dump.gpg | tail -n +$((KEEP + 1)) | xargs -r rm    # lokale Rotation
if [ -n "$BOX" ]; then rsync -az "$DEST/tg-$STAMP.dump.gpg" "$BOX/daily/"; fi
echo "backup ok: tg-$STAMP.dump.gpg"