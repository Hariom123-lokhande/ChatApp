using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ChatApp.Application.Services
{
  
    public interface IConnectionManager
    {
        void AddConnection(Guid userId, string connectionId);
        void RemoveConnection(string connectionId);
        List<string> GetConnections(Guid userId);
        bool IsOnline(Guid userId);
        Guid? GetUserId(string connectionId);
        List<Guid> GetAllOnlineUsers();
    }

    public class ConnectionManager : IConnectionManager
    {
       

        // ================= NEW (thread-safe, no global lock) =================
        // UserId → Connections (thread-safe)
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _userConnections = new();

        // ConnectionId → UserId (reverse lookup)
        private readonly ConcurrentDictionary<string, Guid> _connectionUsers = new();

        public void AddConnection(Guid userId, string connectionId)
        {

            // NEW LOGIC (no lock, fully concurrent)
            var connections = _userConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
            connections[connectionId] = 0;
            _connectionUsers[connectionId] = userId;
        }

        public void RemoveConnection(string connectionId)
        {
      

            // NEW LOGIC
            if (_connectionUsers.TryRemove(connectionId, out var userId))
            {
                if (_userConnections.TryGetValue(userId, out var connections))
                {
                    connections.TryRemove(connectionId, out _);

                    if (connections.IsEmpty)
                    {
                        _userConnections.TryRemove(userId, out _);
                    }
                }
            }
        }

        public List<string> GetConnections(Guid userId)
        {
            
            

            // NEW LOGIC
            if (_userConnections.TryGetValue(userId, out var connections))
            {
                return connections.Keys.ToList();
            }
            return new List<string>();
        }

        public bool IsOnline(Guid userId)
        {
            // SAME (safe)
            return _userConnections.TryGetValue(userId, out var connections) && !connections.IsEmpty;
        }

        public Guid? GetUserId(string connectionId)
        {
            return _connectionUsers.TryGetValue(connectionId, out var userId)
                ? userId
                : null;
        }

        public List<Guid> GetAllOnlineUsers()
        {
            // NEW LOGIC
            return _userConnections
                .Where(x => !x.Value.IsEmpty)
                .Select(x => x.Key)
                .ToList();
        }
    }
}