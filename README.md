# FocusFlow

Anpassbare lokale TODO-App mit **C#-Backend**, browserbasierter Oberfläche, Themen, Querverweisen, Zeiterfassung und Statistiken. Prototyp **v0.1.0**.

## Schnellstart

Voraussetzung: **.NET 10 SDK**, nicht nur die Runtime. Download: https://dotnet.microsoft.com/download/dotnet/10.0

1. Den **Inhalt** dieses Projektordners in dein Repository kopieren – `README.md`, `FocusFlow.slnx`, `src`, `tests` und die übrigen Dateien gehören in den Repository-Hauptordner. Deinen bestehenden `.git`-Ordner nicht überschreiben.
2. Terminal im Repository öffnen.
3. Starten:

```sh
dotnet run --project src/FocusFlow.Web --no-launch-profile
```

4. **http://localhost:5080** im Browser öffnen.
5. Mit **Strg+C** den Server beenden.

Alternativ Windows: `powershell -ExecutionPolicy Bypass -File scripts/start.ps1`.
macOS/Linux: `sh scripts/start.sh`.

Die App funktioniert unter Windows, macOS und Linux mit einem aktuellen Browser (Edge, Chrome, Firefox, Safari). Die GUI ist eine lokale Weboberfläche, kein natives Desktopfenster. Es sind weder Node.js noch npm, Datenbankserver, Cloud-Konto oder externe Webressourcen erforderlich. Nach Installation des SDK benötigt Build/Start keine NuGet-Pakete. Die Daten bleiben bei Code-Updates erhalten.

## Funktionsumfang

- Aufgaben im Popup anlegen, bearbeiten und löschen.
- Titel, Beschreibung, editierbares Erstellungsdatum mit Uhrzeit, Fälligkeit, Status, Priorität, Stunden-Schätzung und freies Ressourcenfeld.
- Mehrere Kategorien je Aufgabe, eine übergeordnete Aufgabe und mehrere Querverweise. Zyklen in der Elternhierarchie werden verhindert. Querverweise sind **keine** fachlich blockierenden Abhängigkeiten.
- Kategorien mit Beschreibung, Farbe, Zeitbudget, Ressource und Zieltermin anlegen, bearbeiten und löschen.
- Volltextsuche über Titel, Beschreibung, Ressourcen und Kategorien. Zusätzliche Filter für Status, Priorität, Kategorie, Ressourcen, Beschreibung, Fälligkeit, Timer, Plan-/Ist-Aufwand, Elternaufgabe, ausgehende Verknüpfung sowie Erstellungs-, Änderungs-, Fälligkeits- und Abschlussdatumsbereiche.
- Sortierung nach Titel, Datum, Priorität, Status, Plan-/Ist-Aufwand, Ressource, Beschreibung, Thema, Elternaufgabe und Anzahl ausgehender Verknüpfungen.
- Start-/Stop-Zeiterfassung mit sekundengenauer Liveanzeige für Aufgabe und Thema. Nur **ein** globaler Timer läuft gleichzeitig; ein neuer Start stoppt den bisherigen Timer automatisch.
- Timer bleibt beim Schließen des Browsers und bei Serverneustart aktiv. Bei erledigten/abgebrochenen Aufgaben stoppt er automatisch.
- Zeit manuell nachtragen (bis 24 Stunden je Buchung), inklusive Notiz. Zukunftsbuchungen und Überschneidungen werden abgewiesen. Fehlerhafte gestoppte Einträge können nach Bestätigung gelöscht werden.
- Detailpopup mit Metadaten, Unteraufgaben, ein- und ausgehenden Querverweisen und Zeitbuchungsverlauf.
- Statistik mit Fließtext, Status-Kreisdiagramm, Plan-/Ist-Diagramm, 14-Tage-Zeitverlauf, Überfälligem, kommenden Terminen, Ressourcenübersicht, Themenbudgets, zeitintensiven Aufgaben und zuletzt erledigten Aufgaben.
- Leere Startdaten, kein versteckter Demo-Seed. Alle Oberflächentexte auf Deutsch.

## Datenspeicherung und Sicherung

Standard-Datenordner: `Environment.SpecialFolder.LocalApplicationData/FocusFlow`.

- Windows: typischerweise `%LOCALAPPDATA%\FocusFlow`.
- Linux: typischerweise `~/.local/share/FocusFlow`.
- macOS: der von .NET zurückgegebene lokale Anwendungsdatenordner.

Der genaue Pfad wird beim Start im Terminal protokolliert. Eigener Pfad:

```sh
dotnet run --project src/FocusFlow.Web --no-launch-profile -- --TodoData:Directory="/dein/pfad/FocusFlow"
```

Oder Umgebungsvariable `TodoData__Directory`. Verwende einen **lokalen Datenträger**, keinen Netzwerkordner und keine gleichzeitig synchronisierte Datei.

`focusflow.json` enthält Aufgaben, Kategorien und Zeiten. Speichern erfolgt unter einer Schreibsperre auf einer Kopie mit anschließendem atomarem Dateiaustausch. `focusflow.json.bak` ist der **vorherige Speicherstand**, kein vollständiges Backup-Archiv. Eine exklusive Prozesssperre verhindert zwei Server mit demselben Datenverzeichnis. Beschädigte JSON-Daten werden, falls möglich, aus `.bak` wiederhergestellt; die Oberfläche zeigt einen Hinweis und die beschädigte Datei wird aufbewahrt. Unbekannte Schemaversionen führen zu einem Fehler, statt Daten stillschweigend zu überschreiben.

**Datensicherung exportieren** lädt den aktuellen Datenstand als JSON herunter. Für die Wiederherstellung: Server stoppen, den aktuellen Ordner zusätzlich sichern, Export als `focusflow.json` in den Datenordner kopieren und Server wieder starten. Kein Importbutton im Prototyp. Vor größeren Updates Daten sichern. Exporte können personenbezogene Inhalte enthalten.

### Regeln für Zeit und Statistik

- Ein laufender Timer zählt reale Kalenderzeit, auch bei geschlossenem Browser oder heruntergefahrenem Rechner. Das ist beabsichtigt. Vor Feierabend stoppen; bei vergessener Sitzung stoppen, Eintrag löschen und die korrekte Zeit manuell nachtragen.
- Zeit wird über UTC-Zeitstempel dauerhaft gespeichert. Formulare und Terminanzeigen verwenden deine lokale Zeitzone. **Der Tageszeitverlauf hat ausdrücklich UTC-Tagesgrenzen.** Zeit über Mitternacht wird aufgeteilt.
- Bei Mehrfachkategorisierung zählt die volle Aufgabenzeit in **jedes** zugeordnete Thema. Globale Gesamtzeit wird nur einmal gezählt. Themenzeiten deshalb nicht summieren.
- Eine nachträgliche Kategorien- oder Ressourcenzuordnung ordnet auch die bisherigen Aufgabenzeiten dem neuen Thema bzw. Ressourcenwert zu. Keine historischen Zuordnungssnapshots.
- Elternaufgaben zählen nicht automatisch Zeiten ihrer Kinder. Für gemeinsame Zeitaggregation dasselbe Thema zuweisen. Damit entstehen keine unsichtbaren Doppelzählungen.
- Restaufwand ist pro aktiver Aufgabe `max(0, Planstunden − Iststunden)`, kein belastbares Fertigstellungsmodell. Aufgaben ohne Schätzung haben Restaufwand 0. Budgets 0 bedeuten „nicht festgelegt“.
- „Ressource“ ist ein freies Personen-/Team-/Werkzeugfeld, **keine** Personal- oder Kapazitätsplanung mit mehreren Zuweisungen.
- Diagramme/Berechnungen aktualisieren sich alle 15 Sekunden, Live-Zeiten jede Sekunde. Inaktive Tabs werden bei Rückkehr aktualisiert. Listenfilter beeinflussen nicht die Gesamtstatistik.

## Testen

```sh
dotnet build FocusFlow.slnx --configuration Release
dotnet run --project tests/FocusFlow.Tests --configuration Release --no-build
```

Oder `sh scripts/test.sh` bzw. `powershell -ExecutionPolicy Bypass -File scripts/test.ps1`.

Der Testläufer ist eine kleine C#-Konsolenanwendung **ohne externe Testpakete**. Er liefert Exitcode 1 bei Fehlern. `dotnet test` ist deshalb hier **nicht** der Testbefehl. GitHub Actions führt Build und denselben Testläufer bei Push/PR aus. Details zum tatsächlich überprüften Stand: `docs/VALIDATION.md`.

## Architektur

```text
FocusFlow.slnx
├── src/FocusFlow.Core     C#-Modelle, Geschäftsregeln, JSON-Repository, Statistiken
├── src/FocusFlow.Web      ASP.NET Core Minimal API und statische Browseroberfläche
│   └── wwwroot           HTML, CSS, Vanilla JavaScript, lokal erzeugte SVG-Diagramme
├── tests/FocusFlow.Tests  Deterministische C#-Tests mit kontrollierbarer Uhr
├── scripts               Start- und Testskripte
└── docs                  Architektur, API und Validierungsnachweis
```

Das gesamte Backend ist C# mit der .NET-Standardbibliothek und dem ASP.NET-Core-Shared-Framework. Keine Entity-Framework-, Chart-, CSS- oder JavaScript-Fremdbibliotheken. Die getrennte Kernbibliothek kann auch für eine spätere Desktop-GUI verwendet werden. Architektur und konkrete Erweiterungspunkte stehen in `docs/ARCHITECTURE.md`.

## Sicherheit und Grenzen

Dies ist ein **lokaler Einzelbenutzer-Prototyp**, kein fertiger Mehrbenutzer- oder öffentlicher Cloudservice. Keine Anmeldung, Rollen, Verschlüsselung im Ruhezustand, Mandantenfähigkeit, revisionssichere Erfassung oder automatische Updates. Standardbindung an `localhost`; zusätzliche Hostprüfung und Schutz gegen browserbasierte Fremdschreibzugriffe. Nicht über Reverse Proxy/Tunnel ins Internet stellen. Andere lokale Prozesse mit Zugriff auf die API oder Dateien befinden sich außerhalb dieses Schutzmodells.

Keine externe Telemetrie der Anwendung. Dateiberechtigungen und Geräteverschlüsselung musst du selbst bzw. über eure IT sicherstellen. Das .NET SDK hat eigene Telemetrieeinstellungen, unabhängig von FocusFlow. Für MAIT-Unternehmensdaten gelten eure internen Freigaben.

Das JSON-Repository eignet sich für persönliche Listen und moderate Datenmengen. Noch keine Datenbankindizes, serverseitige Seitenaufteilung, wiederkehrenden Aufgaben, Benachrichtigungen, Anhänge, Kalenderintegration, offlinefähige PWA oder Drag-and-drop. Weitere Erweiterungen lassen sich gezielt ergänzen.

## Weiterentwicklung und neue ZIP-Versionen

1. Änderungen am aktuellen Projekt beauftragen.
2. Bei eigenständigen Codeänderungen deinen aktuellen Quellstand als ZIP mitgeben; sonst ist hier nur die zuvor gelieferte Version bekannt.
3. Neue vollständige ZIP ins Repository kopieren. Auf gelöschte/umbenannte Dateien im Changelog achten – bloßes Überkopieren entfernt alte Dateien nicht.
4. Daten sichern, Build und Tests ausführen, dann committen.

Beispiel:

```sh
git add .
git commit -m "Add FocusFlow prototype"
```

Die ZIP enthält keine `.git`-Historie, SDK-Dateien, Buildausgaben, Zugangsdaten oder persönlichen Aufgaben.
