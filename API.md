# HTTP-API

Basis: `http://localhost:5080/api`. JSON verwendet camelCase. Enumwerte: Status `Open`, `InProgress`, `Done`, `Cancelled`; Priorität `Low`, `Normal`, `High`, `Urgent`.

## Zugriffs- und Versionsregeln

Jede Änderung braucht:

```http
X-FocusFlow: 1
X-Data-Version: 0
Content-Type: application/json
```

Den Versionswert vorher aus `GET /api/state` unter `data.version` lesen. Schreibantworten sind vollständige `AppData`-Snapshots mit der neuen `version`. Bei DELETE oder Timer-Endpunkten ohne Payload ist Content-Type nicht erforderlich. Browseranfragen müssen vom gleichen Ursprung kommen. Kein CORS und keine Benutzeranmeldung; ausschließlich lokale Nutzung.

| Methode | Pfad | Zweck |
| --- | --- | --- |
| GET | `/state` | `{ data, serverNow, recoveryNotice }` |
| GET | `/statistics` | Aggregierte Auswertungen |
| GET | `/export` | Herunterladbarer JSON-Snapshot |
| POST | `/todos` | Aufgabe erstellen |
| PUT | `/todos/{id}` | Vollständige editierbare Aufgabenfelder ersetzen |
| PATCH | `/todos/{id}/status` | `{ "status": "Done" }` |
| DELETE | `/todos/{id}` | Aufgabe und ihre Zeiten löschen, Beziehungen bereinigen |
| POST | `/todos/{id}/timer/start` | Timer starten, gegebenenfalls vorherigen stoppen |
| POST | `/todos/{id}/timer/stop` | Timer dieser Aufgabe stoppen |
| POST | `/todos/{id}/time` | Manuelle Zeit hinzufügen |
| DELETE | `/time/{id}` | Gestoppte Zeitbuchung löschen |
| POST | `/categories` | Kategorie erstellen |
| PUT | `/categories/{id}` | Kategorie bearbeiten |
| DELETE | `/categories/{id}` | Kategorie und Zuordnungen löschen, Aufgaben behalten |

## Aufgabe

```json
{
  "title": "Prototyp testen",
  "description": "Alle Kernfunktionen prüfen",
  "createdAt": "2026-10-21T08:00:00+02:00",
  "dueAt": "2026-10-23T17:00:00+02:00",
  "status": "Open",
  "priority": "High",
  "estimatedHours": 2.5,
  "resource": "Ich",
  "categoryIds": [],
  "relatedTodoIds": [],
  "parentTodoId": null
}
```

`createdAt: null` verwendet beim Erstellen die Serverzeit und behält beim Bearbeiten das bisherige Datum. Titel ist Pflicht, maximal 200 Zeichen; Beschreibung 10.000; Ressource 200. Stunden 0–100.000. Datumsjahre 2000–2100. ID-Listen werden dedupliziert und referenziell validiert. `CompletedAt`/`UpdatedAt` sind serverseitig. PUT ist keine partielle Änderung – ausgelassene editierbare Felder können auf Defaultwerte fallen.

## Kategorie

```json
{
  "name": "Produktentwicklung",
  "description": "Alle Aufgaben zum neuen Produkt",
  "color": "#f97316",
  "resource": "Engineering",
  "budgetHours": 80,
  "targetDate": null
}
```

Name ist Pflicht (100 Zeichen), case-insensitiv eindeutig. Beschreibung bis 5.000 Zeichen. Farbe als `#RRGGBB`. Budget 0–100.000 Stunden.

## Manuelle Buchung

```json
{
  "startedAt": "2026-10-20T10:00:00+02:00",
  "minutes": 30,
  "note": "Recherche nachgetragen"
}
```

Dauer > 0 und ≤ 1.440 Minuten, Notiz ≤ 1.000 Zeichen. Keine zukünftigen oder überlappenden Buchungen, auch nicht zwischen unterschiedlichen Aufgaben. Gestoppte Sessions lassen sich löschen; aktive Sessions zuerst stoppen.

## Fehler

`application/problem+json`, nutzerfreundliche Erklärung im Feld `title`:

- **400**: Eingabe/Enum/Referenz ungültig, negative Stunden, Kreisbeziehung, Zeitüberschneidung.
- **403**: Nichtlokaler Host oder fehlender Browser-Schreibschutzheader/fremder Origin.
- **404**: Datensatz nicht gefunden.
- **409**: Versionskonflikt – neu laden und erneut versuchen.
- **413**: Anfrage größer als 1 MiB.
- **428**: Datenversion fehlt oder ist ungültig.
- **500**: unerwarteter Lese-/Schreibfehler; Details nur im Serverlog.
