# TransitGuard — Datenschutz-Folgenabschätzung (DSFA) — Entwurf v0.1 (F-7)

**Zweck:** Art.-35-DSGVO-Struktur, befüllt mit Produktstand v1.2-Entscheidungen; vor Launch vom Betriebsverantwortlichen zu finalisieren und zu datieren. **Keine Rechtsberatung.**

## 1. Beschreibung der Verarbeitung (Art. 35 Abs. 3 lit. a)

| Verarbeitung | Datenkategorien | Zweck | Rechtsgrundlage (Vorschlag) | Speicherdauer |
|---|---|---|---|---|
| Kontroll-Meldungen | Station, Linie, Richtung, Zeit, Typ, ggf. grobe Geo (≤ Stationsebene) | Bereitstellung an Nutzer | Art. 6 (1) f / e (berechtigtes Interesse an Informationstransfer; true facts, vgl. Rechtsstand §1) | 90 Tage (Partition-Drop) |
| Geräte-Identität | zufällige UUID + Token-Hash (nur auf Server gehasht) | Missbrauchsschutz, Trust-Score | Art. 6 (1) f | bis Selbstlöschung + 90 Tage Inaktivität |
| Trust-Score | abgeleitete Verhaltenswerte (Meldungszahlen, Bestätigungen) | Qualitätssteuerung | Art. 6 (1) f | wie Geräte-Identität |
| Entitlements | Token-/Code-Hashes, Receipt-Hash, Abo-Status | Zugangsbeschränkung Pro, Fakturierungsnachweis | Art. 6 (1) b/c | bis Widerruf; Receipt-Hash nach steuerlicher Pflicht (F-6) |
| Server-Logs | IP (kontinuierlich), Pfade, Fehler | IT-Sicherheit | Art. 6 (1) f | 14 Tage (journald) |
| Ingest-Metriken | Feed-Statistiken, keine Personenbeüge | Betrieb | Art. 6 (1) f | 180 Tage |

**Standort:** bleibt ausschließlich auf dem Gerät; Server empfängt nur die für die Meldung bestimmten Daten. Keine Werbung, keine Profile, keine Drittweitergabe.

## 2. Systematische Bewertung der Risiken (Art. 35 Abs. 3 lit. b)

| Risiko für Betroffene | Bewertung | Minderungsmaßnahmen (implementiert/geplant) |
|---|---|---|
| **Re-Identifikation von Kontrolleur:innen** über Meldungsmuster (Ort+Zeit+Linie → wiederkehrende Einsätze) — von externen Juristen als Kipp-Punkt benannt (Rechtsstand §2) | **Hochrelevant, aber adressiert** | Keine Personen-/Team-Beschreibungen; keine Fotos; Meldungsauflösung bewusst grob (Station+Linie); Prognose/Heatmaps nur aggregiert (Schwelle ≥5–10 Meldungen, keine Einzel-„Routen"); 90-Tage-Rotation; Berichts-/Export-Sperren in API (T-EN-1-Kette) |
| Re-Identifikation von **Meldern** über Timing-Korrelation | Mittel | Token statt Konten; keine IP in Meldungs-Datensatz; Rate-Limits verhindern Verhaltens-Fingerprinting nur bedingt — Restrisiko dokumentiert |
|device-Diebstahl (Token missbraucht) | Niedrig | Rotation möglich (Gerät löschen → neu), Rate-Limits, Verbindungscaps |
| Kontroll-Daten als Hebel gegen Zahlenquoten (Betrieb erfragt Meldungslisten) | Mittel | Kein Einzel-Auskunftsweg ohne Device-Token; öffentliche nur aggregierte Statistik (A2-B2-Schwelle); Kill-Switch für den Fall behördlichen Drucks |
| Datenpanne (DB-Kompromittierung) | Mittel | RLS/Rollen, Token nur als Hash, Verschlüsselung der Backups (pg_dump + gpg), Hetzner DE, 14-Tage-Logs |

## 3. Abhilfemaßnahmen & Verhältnismäßigkeit (Art. 35 Abs. 3 lit. d)

Datenminimierung ist Architekturprinzip (kein Konto, kein Name, keine E-Mail — einziges Identifikationsmerkmal ist ein wegwerfbares Token). Auskunft/Löschung: selbstbedient über App (Export-JSON, „Gerät löschen") — technisch in T-API getestet. Betroffene, die nie Nutzer waren (Kontrolleur:innen), erhalten über das Impressum einen Beschwerdekanal; Konsistenz-Regel: solange aggregierte Darstellung + 90-Tage-Rotation, ist ein Einzel-Löschbegehren strukturell durch Rotationsfrist erfüllt (*anwaltlich gegenprüfen, F-1*).

## 4. Restdokumentation vor Launch (Checkliste)

- [ ] Verarbeitungsverzeichnis (Art. 30) als Tabelle aus §1 ableiten
- [ ] TOM-Katalog (Hostierung, Verschlüsselung, Zugänge) — Vorlage Deployment-Doc
- [ ] AVV Hetzner (F-5) + ggf. FCM/APNs-Anbieter (erst mit Push M8/T8.2)
- [ ] Datenschutztexte App/PWA (schlicht, deutsch zuerst) + Store-Labels wahrheitsgemäß
- [ ] Datenschutz-Prior-Consultation? Nur falls Risiko-Bewertung final „hoch" — derzeit: nein (Begründung §2)
