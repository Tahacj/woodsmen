using System;
using System.Collections.Generic;
using Mirror;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

// Disambiguate NetworkConnection between Mirror and Unity Transport
using NetworkConnection = Unity.Networking.Transport.NetworkConnection;

namespace Woodsmen.Networking
{
    /// <summary>
    /// Mirror Transport implementation using Unity Transport Package (UTP) and Unity Relay.
    /// Supports both:
    /// 1. Cloud Unity Relay (NAT punch-through / global internet without port forwarding)
    /// 2. Direct UDP / Localhost fallback for rapid local testing
    /// </summary>
    [DisallowMultipleComponent]
    public class UnityRelayTransport : Transport
    {
        public static UnityRelayTransport Instance { get; private set; }

        public const string Scheme = "udp";

        [Header("Direct / Local Fallback Config")]
        [SerializeField] private ushort port = 7777;
        public ushort Port { get => port; set => port = value; }

        [Header("Buffer Sizes")]
        [SerializeField] private int maxPacketSize = 32768;

        // Relay Allocation Data (cached before connecting or hosting)
        private RelayServerData? _serverRelayData;
        private RelayServerData? _clientRelayData;

        // Server driver & pipelines
        private NetworkDriver _serverDriver;
        private NetworkPipeline _serverReliablePipeline;
        private NetworkPipeline _serverUnreliablePipeline;
        private readonly Dictionary<int, NetworkConnection> _idToConnection = new Dictionary<int, NetworkConnection>();
        private readonly Dictionary<NetworkConnection, int> _connectionToId = new Dictionary<NetworkConnection, int>();
        private int _nextConnectionId = 1;

        // Client driver & pipelines
        private NetworkDriver _clientDriver;
        private NetworkPipeline _clientReliablePipeline;
        private NetworkPipeline _clientUnreliablePipeline;
        private NetworkConnection _clientConnection;
        private bool _clientConnected;

        private RelayConnectionStatus _lastServerRelayStatus = RelayConnectionStatus.NotUsingRelay;
        private RelayConnectionStatus _lastClientRelayStatus = RelayConnectionStatus.NotUsingRelay;

        public RelayConnectionStatus ServerRelayStatus => _serverDriver.IsCreated ? _serverDriver.GetRelayConnectionStatus() : RelayConnectionStatus.NotUsingRelay;
        public RelayConnectionStatus ClientRelayStatus => _clientDriver.IsCreated ? _clientDriver.GetRelayConnectionStatus() : RelayConnectionStatus.NotUsingRelay;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Shutdown();
        }

        #region Relay Configuration

        public void SetServerRelayData(RelayServerData serverData)
        {
            _serverRelayData = serverData;
            Debug.Log($"[UnityRelayTransport] Server Relay data configured. Endpoint: {serverData.Endpoint}, AllocationId: {serverData.AllocationId}");
        }

        public void SetClientRelayData(RelayServerData clientData)
        {
            _clientRelayData = clientData;
            Debug.Log($"[UnityRelayTransport] Client Relay data configured. Endpoint: {clientData.Endpoint}, AllocationId: {clientData.AllocationId}");
        }

        public void ClearRelayData()
        {
            if (!_serverRelayData.HasValue && !_clientRelayData.HasValue) return;

            _serverRelayData = null;
            _clientRelayData = null;
            _lastServerRelayStatus = RelayConnectionStatus.NotUsingRelay;
            _lastClientRelayStatus = RelayConnectionStatus.NotUsingRelay;
            Debug.Log("[UnityRelayTransport] Relay data cleared. Reverting to direct UDP.");
        }

        public bool IsUsingRelay => _serverRelayData.HasValue || _clientRelayData.HasValue;

        #endregion

        #region Transport Interface Implementation

        public override bool Available() => true;

        public override int GetMaxPacketSize(int channelId = Channels.Reliable) => maxPacketSize;

        public override Uri ServerUri()
        {
            return new Uri($"udp://127.0.0.1:{port}");
        }

        public override void Shutdown()
        {
            ServerStop();
            ClientDisconnect();
            ClearRelayData();
        }

        #endregion

        #region Server Lifecycle

        public override bool ServerActive()
        {
            return _serverDriver.IsCreated && _serverDriver.Listening;
        }

        public override void ServerStart()
        {
            ServerStop();

            _idToConnection.Clear();
            _connectionToId.Clear();
            _nextConnectionId = 1;

            var settings = new NetworkSettings();
            settings.WithFragmentationStageParameters(payloadCapacity: maxPacketSize);

            if (_serverRelayData.HasValue)
            {
                var relayData = _serverRelayData.Value;
                settings.WithRelayParameters(ref relayData);
                _serverDriver = NetworkDriver.Create(settings);

                var bindEndpoint = relayData.Endpoint.Family == NetworkFamily.Ipv6 ? NetworkEndpoint.AnyIpv6 : NetworkEndpoint.AnyIpv4;
                int bindResult = _serverDriver.Bind(bindEndpoint);
                if (bindResult != 0)
                {
                    Debug.LogError($"[UnityRelayTransport] Failed to bind server driver to {bindEndpoint}, error code {bindResult}");
                    return;
                }

                _serverReliablePipeline = _serverDriver.CreatePipeline(
                    typeof(FragmentationPipelineStage),
                    typeof(ReliableSequencedPipelineStage)
                );

                _serverUnreliablePipeline = _serverDriver.CreatePipeline(
                    typeof(FragmentationPipelineStage),
                    typeof(UnreliableSequencedPipelineStage)
                );

                _serverDriver.Listen();
                _lastServerRelayStatus = _serverDriver.GetRelayConnectionStatus();

                // Kick off initial handshake update and flush immediately
                _serverDriver.ScheduleUpdate().Complete();
                _serverDriver.ScheduleFlushSend(default).Complete();

                Debug.Log($"[UnityRelayTransport] Server started listening with Unity Relay to {relayData.Endpoint}! Initial status: {_lastServerRelayStatus}");
            }
            else
            {
                _serverDriver = NetworkDriver.Create(settings);
                var bindEndpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
                int bindResult = _serverDriver.Bind(bindEndpoint);
                if (bindResult != 0)
                {
                    Debug.LogError($"[UnityRelayTransport] Failed to bind server driver on port {port}, error code {bindResult}");
                    return;
                }

                _serverReliablePipeline = _serverDriver.CreatePipeline(
                    typeof(FragmentationPipelineStage),
                    typeof(ReliableSequencedPipelineStage)
                );

                _serverUnreliablePipeline = _serverDriver.CreatePipeline(
                    typeof(FragmentationPipelineStage),
                    typeof(UnreliableSequencedPipelineStage)
                );

                _serverDriver.Listen();
                _serverDriver.ScheduleFlushSend(default).Complete();
                Debug.Log($"[UnityRelayTransport] Server started listening on direct port {port} (Local/LAN).");
            }
        }

        public override void ServerStop()
        {
            if (_serverDriver.IsCreated)
            {
                try
                {
                    _serverDriver.ScheduleFlushSend(default).Complete();
                    _serverDriver.ScheduleUpdate().Complete();
                    _serverDriver.ScheduleFlushSend(default).Complete();

                    foreach (var conn in _idToConnection.Values)
                    {
                        if (conn.IsCreated)
                        {
                            _serverDriver.Disconnect(conn);
                        }
                    }

                    _serverDriver.ScheduleFlushSend(default).Complete();
                    _serverDriver.ScheduleUpdate().Complete();
                    _serverDriver.ScheduleFlushSend(default).Complete();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UnityRelayTransport] Exception during ServerStop flush: {ex.Message}");
                }
                finally
                {
                    _idToConnection.Clear();
                    _connectionToId.Clear();
                    _serverDriver.Dispose();
                    Debug.Log("[UnityRelayTransport] Server stopped.");
                }
            }
        }

        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            if (!_serverDriver.IsCreated) return;

            if (_idToConnection.TryGetValue(connectionId, out NetworkConnection conn))
            {
                if (!conn.IsCreated) return;

                NetworkPipeline pipeline = channelId == Channels.Reliable ? _serverReliablePipeline : _serverUnreliablePipeline;
                int result = _serverDriver.BeginSend(pipeline, conn, out DataStreamWriter writer, segment.Count);
                if (result == 0)
                {
                    writer.WriteBytes(new Span<byte>(segment.Array, segment.Offset, segment.Count));
                    _serverDriver.EndSend(writer);
                    OnServerDataSent?.Invoke(connectionId, segment, channelId);
                }
                else
                {
                    Debug.LogWarning($"[UnityRelayTransport] ServerSend failed for connection {connectionId}, error code {result}");
                }
            }
        }

        public override void ServerDisconnect(int connectionId)
        {
            if (!_serverDriver.IsCreated) return;

            if (_idToConnection.TryGetValue(connectionId, out NetworkConnection conn))
            {
                if (conn.IsCreated)
                {
                    _serverDriver.Disconnect(conn);
                    _serverDriver.ScheduleFlushSend(default).Complete();
                    _serverDriver.ScheduleUpdate().Complete();
                    _serverDriver.ScheduleFlushSend(default).Complete();
                }
                _idToConnection.Remove(connectionId);
                _connectionToId.Remove(conn);
                OnServerDisconnected?.Invoke(connectionId);
            }
        }

        public override string ServerGetClientAddress(int connectionId)
        {
            if (_idToConnection.TryGetValue(connectionId, out NetworkConnection conn))
            {
                if (_serverDriver.IsCreated && conn.IsCreated)
                {
                    return _serverDriver.GetRemoteEndpoint(conn).ToString();
                }
            }
            return string.Empty;
        }

        #endregion

        #region Client Lifecycle

        public override bool ClientConnected() => _clientConnected;

        public override void ClientConnect(string address)
        {
            ClientDisconnect();

            var settings = new NetworkSettings();
            settings.WithFragmentationStageParameters(payloadCapacity: maxPacketSize);

            NetworkEndpoint connectEndpoint;

            if (_clientRelayData.HasValue)
            {
                var relayData = _clientRelayData.Value;
                settings.WithRelayParameters(ref relayData);
                _clientDriver = NetworkDriver.Create(settings);

                var bindEndpoint = relayData.Endpoint.Family == NetworkFamily.Ipv6 ? NetworkEndpoint.AnyIpv6 : NetworkEndpoint.AnyIpv4;
                int bindResult = _clientDriver.Bind(bindEndpoint);
                if (bindResult != 0)
                {
                    Debug.LogError($"[UnityRelayTransport] Failed to bind client driver to {bindEndpoint}, error code {bindResult}");
                    return;
                }

                _clientReliablePipeline = _clientDriver.CreatePipeline(
                    typeof(FragmentationPipelineStage),
                    typeof(ReliableSequencedPipelineStage)
                );

                _clientUnreliablePipeline = _clientDriver.CreatePipeline(
                    typeof(FragmentationPipelineStage),
                    typeof(UnreliableSequencedPipelineStage)
                );

                _clientConnection = _clientDriver.Connect();
                _clientConnected = false;
                _lastClientRelayStatus = _clientDriver.GetRelayConnectionStatus();

                // Kick off initial handshake update and flush immediately
                _clientDriver.ScheduleUpdate().Complete();
                _clientDriver.ScheduleFlushSend(default).Complete();

                Debug.Log($"[UnityRelayTransport] Client bound and connecting via Unity Relay to {relayData.Endpoint}... Connection created: {_clientConnection.IsCreated}, Initial status: {_lastClientRelayStatus}");
            }
            else
            {
                _clientDriver = NetworkDriver.Create(settings);
                if (!NetworkEndpoint.TryParse(address, port, out connectEndpoint))
                {
                    connectEndpoint = NetworkEndpoint.LoopbackIpv4.WithPort(port);
                }

                _clientReliablePipeline = _clientDriver.CreatePipeline(
                    typeof(FragmentationPipelineStage),
                    typeof(ReliableSequencedPipelineStage)
                );

                _clientUnreliablePipeline = _clientDriver.CreatePipeline(
                    typeof(FragmentationPipelineStage),
                    typeof(UnreliableSequencedPipelineStage)
                );

                _clientConnection = _clientDriver.Connect(connectEndpoint);
                _clientConnected = false;
                _clientDriver.ScheduleFlushSend(default).Complete();
                Debug.Log($"[UnityRelayTransport] Client connecting directly to {connectEndpoint}...");
            }
        }

        public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            if (!_clientDriver.IsCreated || !_clientConnection.IsCreated) return;

            NetworkPipeline pipeline = channelId == Channels.Reliable ? _clientReliablePipeline : _clientUnreliablePipeline;
            int result = _clientDriver.BeginSend(pipeline, _clientConnection, out DataStreamWriter writer, segment.Count);
            if (result == 0)
            {
                writer.WriteBytes(new Span<byte>(segment.Array, segment.Offset, segment.Count));
                _clientDriver.EndSend(writer);
                OnClientDataSent?.Invoke(segment, channelId);
            }
            else
            {
                Debug.LogWarning($"[UnityRelayTransport] ClientSend failed, error code {result}");
            }
        }

        public override void ClientDisconnect()
        {
            if (_clientDriver.IsCreated)
            {
                try
                {
                    if (_clientConnection.IsCreated)
                    {
                        // Flush pending messages (such as CmdLeaveLobby)
                        _clientDriver.ScheduleFlushSend(default).Complete();
                        _clientDriver.ScheduleUpdate().Complete();
                        _clientDriver.ScheduleFlushSend(default).Complete();

                        _clientDriver.Disconnect(_clientConnection);

                        // Flush disconnect packet so host receives disconnect event immediately
                        _clientDriver.ScheduleFlushSend(default).Complete();
                        _clientDriver.ScheduleUpdate().Complete();
                        _clientDriver.ScheduleFlushSend(default).Complete();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UnityRelayTransport] Exception during ClientDisconnect flush: {ex.Message}");
                }
                finally
                {
                    _clientDriver.Dispose();
                    _clientConnected = false;
                    Debug.Log("[UnityRelayTransport] Client disconnected and disposed.");
                }
            }
        }

        #endregion

        #region Mirror Network Loop Updates

        public override void ServerEarlyUpdate()
        {
            if (!_serverDriver.IsCreated) return;

            _serverDriver.ScheduleUpdate().Complete();
            _serverDriver.ScheduleFlushSend(default).Complete();

            if (IsUsingRelay)
            {
                var currentStatus = _serverDriver.GetRelayConnectionStatus();
                if (currentStatus != _lastServerRelayStatus)
                {
                    Debug.Log($"[UnityRelayTransport] Server Relay Status changed: {_lastServerRelayStatus} -> {currentStatus}");
                    _lastServerRelayStatus = currentStatus;
                }
            }

            // 1. Accept any pending connections
            NetworkConnection incoming;
            while ((incoming = _serverDriver.Accept()) != default)
            {
                RegisterServerConnection(incoming);
            }

            // 2. Process incoming packets & connection events
            NetworkConnection conn;
            DataStreamReader reader;
            NetworkPipeline pipeline;
            NetworkEvent.Type cmd;

            while ((cmd = _serverDriver.PopEvent(out conn, out reader, out pipeline)) != NetworkEvent.Type.Empty)
            {
                switch (cmd)
                {
                    case NetworkEvent.Type.Connect:
                        RegisterServerConnection(conn);
                        break;

                    case NetworkEvent.Type.Data:
                        if (_connectionToId.TryGetValue(conn, out int connId))
                        {
                            byte[] buffer = new byte[reader.Length];
                            reader.ReadBytes(new Span<byte>(buffer));
                            int channel = pipeline == _serverReliablePipeline ? Channels.Reliable : Channels.Unreliable;
                            OnServerDataReceived?.Invoke(connId, new ArraySegment<byte>(buffer), channel);
                        }
                        break;

                    case NetworkEvent.Type.Disconnect:
                        if (_connectionToId.TryGetValue(conn, out int dcId))
                        {
                            _connectionToId.Remove(conn);
                            _idToConnection.Remove(dcId);
                            OnServerDisconnected?.Invoke(dcId);
                        }
                        break;
                }
            }
        }

        public override void ServerLateUpdate()
        {
            if (_serverDriver.IsCreated)
            {
                _serverDriver.ScheduleFlushSend(default).Complete();
            }
        }

        private void RegisterServerConnection(NetworkConnection conn)
        {
            if (!_connectionToId.ContainsKey(conn))
            {
                int id = _nextConnectionId++;
                _idToConnection[id] = conn;
                _connectionToId[conn] = id;
                string address = _serverDriver.GetRemoteEndpoint(conn).ToString();
                OnServerConnectedWithAddress?.Invoke(id, address);
                Debug.Log($"[UnityRelayTransport] Client connected to server: ID {id} ({address})");
            }
        }

        public override void ClientEarlyUpdate()
        {
            if (!_clientDriver.IsCreated || !_clientConnection.IsCreated) return;

            _clientDriver.ScheduleUpdate().Complete();
            _clientDriver.ScheduleFlushSend(default).Complete();

            if (IsUsingRelay)
            {
                var currentStatus = _clientDriver.GetRelayConnectionStatus();
                if (currentStatus != _lastClientRelayStatus)
                {
                    Debug.Log($"[UnityRelayTransport] Client Relay Status changed: {_lastClientRelayStatus} -> {currentStatus}");
                    _lastClientRelayStatus = currentStatus;
                }
            }

            NetworkEvent.Type cmd;
            DataStreamReader reader;
            NetworkPipeline pipeline;

            while ((cmd = _clientConnection.PopEvent(_clientDriver, out reader, out pipeline)) != NetworkEvent.Type.Empty)
            {
                switch (cmd)
                {
                    case NetworkEvent.Type.Connect:
                        _clientConnected = true;
                        OnClientConnected?.Invoke();
                        Debug.Log("[UnityRelayTransport] Client connected to host!");
                        break;

                    case NetworkEvent.Type.Data:
                        byte[] buffer = new byte[reader.Length];
                        reader.ReadBytes(new Span<byte>(buffer));
                        int channel = pipeline == _clientReliablePipeline ? Channels.Reliable : Channels.Unreliable;
                        OnClientDataReceived?.Invoke(new ArraySegment<byte>(buffer), channel);
                        break;

                    case NetworkEvent.Type.Disconnect:
                        _clientConnected = false;
                        var relayStatus = _clientDriver.GetRelayConnectionStatus();
                        Debug.LogWarning($"[UnityRelayTransport] Client disconnected from host. Relay status: {relayStatus}");
                        OnClientDisconnected?.Invoke();
                        break;
                }
            }
        }

        public override void ClientLateUpdate()
        {
            if (_clientDriver.IsCreated)
            {
                _clientDriver.ScheduleFlushSend(default).Complete();
            }
        }

        #endregion
    }
}
