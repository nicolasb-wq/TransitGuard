# ADR-0005 — Anonymes Besitz-Token-Entitlement (kein Konto, keine Geräte-Verkettung)
**Kontext:** Produktversprechen „Nutzung ohne Registrierung" gegen Abo-Identität (08 §3).
**Optionen:** (a) Besitz-Token + Restore-Code (Hashes serverseitig), (b) Konten mit E-Mail, (c) gerätegebundene Käufe, (d) Trust-Übertragung zwischen Geräten.
**Entscheidung:** (a); (d) explizit NICHT in v1 (Trust bleibt an Gerät; v2-Option Both-Online-Handshake). Connections-Cap 3 dämpft Token-Sharing.
**Konsequenzen:** Verlust von Token+Code = Abo bis Renewal unterbrochen (kommunizieren); Receipt-Dedup verhindert Doppelaktivierung; Refund/ABO-Ende über Store-Server-Notifications; DSGVO: Token pseudonym, Receipt-Hash = Abrechnungsbeleg.
