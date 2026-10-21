# Validierungsstand

Dieser Stand enthält den vollständigen Quellcode des ersten Prototypen und einen C#-Testläufer.

Ein erfolgreicher Build, bestandene C#-Tests und ein vollständiger Browser-Funktionstest sind in der Erstellungsumgebung bislang nicht nachgewiesen. Bei der Einrichtung der .NET- und Browser-Testumgebung traten wiederholt Zeitüberschreitungen auf. Dieser Stand ist daher ausdrücklich ein **unverifizierter Prototyp**, keine als stabil getestete Lieferung.

## Lokal prüfen

.NET 10 SDK installieren und im Projektordner ausführen:

```sh
dotnet build FocusFlow.slnx --configuration Release
dotnet run --project tests/FocusFlow.Tests --configuration Release --no-build
dotnet run --project src/FocusFlow.Web --no-launch-profile
```

Danach http://localhost:5080 öffnen und Aufgaben, Kategorien, Filter, Timer, Neustart-Persistenz und Statistik prüfen. Bei Build- oder Laufzeitfehlern die vollständige Fehlermeldung zur Korrektur weitergeben.
