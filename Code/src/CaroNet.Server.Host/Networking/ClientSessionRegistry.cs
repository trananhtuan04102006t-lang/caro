using System.Collections.Concurrent;

namespace CaroNet.Server.Host.Networking;

public sealed class ClientSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, ClientSession>
        _sessions = new();

    public void Add(ClientSession session)
    {
        _sessions.TryAdd(
            session.Id,
            session);
    }

    public void Remove(Guid id)
    {
        _sessions.TryRemove(
            id,
            out _);
    }

    public IReadOnlyCollection<ClientSession> GetAll()
    {
        return _sessions.Values.ToArray();
    }

    public int Count =>
        _sessions.Count;
}