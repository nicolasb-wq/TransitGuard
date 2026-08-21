using Xunit;

// Integrationstests bauen echte Hosts. Hangfire setzt beim Start JobStorage.Current
// PROZESSWEIT — parallele Hosts kollidieren dort auf denselben Sperren
// ("DistributedLockTimeoutException" auf lock:recurring-job:ttl-sweep).
// Diese Tests laufen deshalb bewusst seriell; die Laufzeit liegt trotzdem im
// Sekundenbereich, weil kein Netz und keine Datenbank im Spiel sind.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
