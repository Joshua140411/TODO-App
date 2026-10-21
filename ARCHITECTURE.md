# Architektur und Erweiterungspunkte

## Entscheidung

ASP.NET Core Minimal API plus statische HTML/CSS/JavaScript-Oberfläche. Für einen ersten plattformübergreifenden Prototyp sind damit keine GUI-Runtime außerhalb des Browsers, keine npm-Toolchain und keine NuGet-Pakete nötig. Das Backend bleibt vollständig C#. Der Browser wird vom C#-Prozess auf demselben Ursprung bedient.

## Schichten

| Schicht | Verantwortung | Darf nicht |
| --- | --- | --- |
| Core/Models | Datenmodell, Eingabe-DTOs, Status/Priorität | HTTP oder DOM kennen |
| Core/TodoService | Validierung, Aufgaben/Kategorien, Timer, Beziehungen | Browser vertrauen |
| Core/JsonStore | atomare Speicherung, Kopien, globale Version, Prozesssperre | Geschäftslogik im Frontend ersetzen |
| Core/Statistics | serverseitige aggregierte Auswertungen | Zeit mehreren Kategorien global doppelt zählen |
| Web/Program | HTTP, Fehlermapping, lokale Zugriffsgrenzen, Dependency Injection | Eingabe ohne Service speichern |
| Web/wwwroot | Darstellung, Eingaben, Filter/Sortierung, Liveanzeige | autoritative Persistenz enthalten |

`TimeProvider` ist injiziert; Tests nutzen eine kontrollierte Uhr. Snapshot-Lesen liefert Kopien. Jede Mutation arbeitet auf einem Klon, prüft Geschäftsregeln und persistiert erst danach. Validierungs- und Schreibfehler ändern den In-Memory-Stand nicht.

## Parallelität

Alle Änderungen verlangen die zuletzt gelesene globale `Version`. Gleichzeitige Tabs können so keine veralteten Daten überschreiben. Bei HTTP 409 lädt die Oberfläche den aktuellen Stand; der geöffnete Formularentwurf bleibt erhalten. Danach Eingaben prüfen und nochmals speichern. Das ist bewusst gröber als einzelne Entity-Versionsnummern. Bei einem echten Mehrbenutzersystem auf Datensatzversionen und Datenbanktransaktionen wechseln.

Das Repository hält eine OS-Dateisperre für den Datenordner. Ein zweiter Prozess wird abgewiesen. Es gibt keinen verteilten Lock; Netzlaufwerke sind nicht unterstützt. Vorheriger JSON-Speicherstand ist `.bak`, alle übrigen Exporte müssen separat archiviert werden. Atomarer Austausch minimiert Teilwrites, ersetzt aber keine Hardware-/Dateisystemgarantien oder externen Backups.

## Erweiterungen

### Weitere Aufgabenattribute

1. Property in `TodoItem` und `TodoInput` ergänzen.
2. Validierung und Übernahme in `TodoService.Apply` implementieren.
3. Popup `index.html`, Payload `app.js`, Darstellung, Filter und Sortierung ergänzen.
4. Tests hinzufügen. Bei erforderlicher Datentransformation Schema erhöhen und bewusste Migration in `JsonStore.Load` hinzufügen – unbekannte Schemas bleiben sonst gesperrt.

### SQLite oder PostgreSQL

Für größere Datenmengen als nächsten Schritt ein `IAppRepository` mit Lesen und transaktionalen Mutationen herauslösen; aktuell ist `JsonStore` bewusst eine kleine konkrete Klasse. Erst dann Datenbankadapter, migrationsfähiges Schema, Indizes und serverseitige Filter/Pagination hinzufügen. Zeit-/Geschäftsregeln im Service behalten. Bestehende Semantik zu Mehrfachkategorien und Versionskonflikten mit Migrationstests sichern.

### Native GUI

`FocusFlow.Core` hat keine ASP.NET- oder Browserabhängigkeit. Avalonia, WPF oder MAUI können dieselbe Geschäftslogik nutzen. Dafür eine separate Präsentationsschicht und passende Deploymentstrategie ergänzen.

### Mehrbenutzer, Sync und Cloud

Nicht einfach `UseUrls` auf `0.0.0.0` ändern. Zuerst Authentifizierung, Autorisierung, TLS, Benutzer-/Mandantenzuordnung, robustes Repository, Datensatzkonflikte, CSRF-/Sessionkonzept, Backup-/Restorestrategie und Betriebskonzept implementieren. Die aktuelle Hostprüfung würde Netzwerkhostnamen ohnehin abweisen.

### Zeiterfassung ausbauen

Für Abrechnungen Sessions mit Rechnungsstatus, Historie, Kommentaren und Auditlog statt zerstörbarer Zeitbuchungen versehen. Für Ressourcenplanung eine Ressourcenentität und gewichtete Mehrfachzuordnung ergänzen. Für historisch genaue Themenerfassung Zuordnungen pro Session speichern. UTC-Tagesauswertung bei Bedarf auf explizit konfigurierbare Benutzerzeitzonen umstellen; Tests für Sommer-/Winterzeit ergänzen.

## Bewusste Vereinfachungen

- Keine blockierenden TODO-Abhängigkeiten; Querverweise sind gerichtet gespeichert und in Details in beide Richtungen sichtbar. Der Listenfilter prüft die ausgehenden Verweise.
- Keine automatische Zeitaggregation einer Elternaufgabe über alle Unteraufgaben.
- Keine Kapazitäts- oder Kostenplanung; Ressource ist Freitext, Budget ist Zeit.
- Alle Daten befinden sich lokal in einer versionierten JSON-Datei. Die Liste filtert/sortiert den vollständigen geladenen Stand im Browser.
- Global genau ein Timer. Uhrzeitänderungen werden gegen negative Dauer abgesichert, sind aber keine manipulationssichere Erfassung.
- Keine vollständige Schema-Migrationspipeline. Schema 1 wird unterstützt; neuere Versionen werden sicher abgewiesen.
