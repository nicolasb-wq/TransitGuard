using TransitGuard.Core.Models;
using TransitGuard.Core.Trust;

namespace TransitGuard.Api.Services;

/// <summary>Brücke EF-TrustStore (Data) → Api-ITrustStore (Save-Semantik, EF-Modus).</summary>
public interface ITrustStoreAdapter
{
    TrustState GetOrCreate(Guid deviceId);
    void Save(TrustState state);
    void Delete(Guid deviceId);
}

public sealed class EfTrustStoreAdapter(Data.EfTrustStore inner) : ITrustStore
{
    public TrustState GetOrCreate(Guid deviceId) => inner.GetOrCreate(deviceId);
    public void Save(TrustState state) => inner.Save(state);
    public void Delete(Guid deviceId) => inner.Delete(deviceId);
}
