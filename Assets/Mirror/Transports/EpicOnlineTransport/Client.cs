using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using Mirror;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EpicTransport {
    public class Client : Common {

        public SocketId socketId;
        public ProductUserId serverId;

        public bool Connected { get; private set; }
        public bool Error { get; private set; }

        private event Action<byte[], int> OnReceivedData;
        private event Action OnConnected;
        public event Action OnDisconnected;

        private TimeSpan ConnectionTimeout;

        public bool isConnecting = false;
        public string hostAddress = "";
        private ProductUserId hostProductId = null;
        private TaskCompletionSource<Task> connectedComplete;
        private CancellationTokenSource cancelToken;

        private Client(EosTransport transport) : base(transport) {
            ConnectionTimeout = TimeSpan.FromSeconds(Math.Max(1, transport.timeout));
        }

        public static Client CreateClient(EosTransport transport, string host) {
            Client c = new Client(transport);

            c.hostAddress = host;
            c.socketId = new SocketId() { SocketName = RandomString.Generate(20) };

            c.OnConnected += () => transport.OnClientConnected.Invoke();
            c.OnDisconnected += () => transport.OnClientDisconnected.Invoke();
            c.OnReceivedData += (data, channel) => transport.OnClientDataReceived.Invoke(new ArraySegment<byte>(data), channel);

            return c;
        }

        public async void Connect(string host) {
            cancelToken = new CancellationTokenSource();

            try {
                hostProductId = ProductUserId.FromString(host);
                serverId = hostProductId;
                connectedComplete = new TaskCompletionSource<Task>();

                OnConnected += SetConnectedComplete;

#if UNITY_EDITOR
                Debug.Log($"[EosTransport/Client] Sending CONNECT packet to host ProductId: '{host}', LocalUserId: '{EOSSDKComponent.LocalUserProductIdString}' (Socket: '{socketId.SocketName}')");
#endif
                SendInternal(hostProductId, socketId, InternalMessages.CONNECT);

                Task connectedCompleteTask = connectedComplete.Task;

                if (await Task.WhenAny(connectedCompleteTask, Task.Delay(ConnectionTimeout/*, cancelToken.Token*/)) != connectedCompleteTask) {
                    Debug.LogError($"[EosTransport/Client] Connection to host {host} timed out after {ConnectionTimeout.TotalSeconds:F0}s. Please verify the host is currently running and the Product User ID is correct.");
                    OnConnected -= SetConnectedComplete;
                    OnConnectionFailed(hostProductId);
                }

                OnConnected -= SetConnectedComplete;
            } catch (FormatException) {
                Debug.LogError($"Connection string was not in the right format. Did you enter a ProductId?");
                Error = true;
                OnConnectionFailed(hostProductId);
            } catch (Exception ex) {
                Debug.LogError(ex.Message);
                Error = true;
                OnConnectionFailed(hostProductId);
            } finally {
                if (Error) {
                    OnConnectionFailed(null);
                }
            }

        }

        public void Disconnect() {
            if (serverId != null) {
                CloseP2PSessionWithUser(serverId, socketId);

                serverId = null;
            } else {
                return;
            }

            SendInternal(hostProductId, socketId, InternalMessages.DISCONNECT);

            Dispose();
            cancelToken?.Cancel();

            WaitForClose(hostProductId, socketId);
        }

        private void SetConnectedComplete() => connectedComplete.SetResult(connectedComplete.Task);

        protected override void OnReceiveData(byte[] data, ProductUserId clientUserId, int channel) {
            if (ignoreAllMessages) {
                return;
            }

            if (clientUserId != hostProductId) {
                Debug.LogError("Received a message from an unknown");
                return;
            }

            OnReceivedData.Invoke(data, channel);
        }

        protected override void OnNewConnection(OnIncomingConnectionRequestInfo result) {
#if UNITY_EDITOR
            Debug.Log($"[EosTransport/Client] OnIncomingConnectionRequest from {result.RemoteUserId} on socket {result.SocketId?.SocketName}");
#endif

            if (ignoreAllMessages) {
                Debug.LogWarning("[EosTransport/Client] Dropping incoming connection request because ignoreAllMessages is true.");
                return;
            }

            if (deadSockets.Contains(result.SocketId.SocketName)) {
                Debug.LogError("Received incoming connection request from dead socket");
                return;
            }

            if (hostProductId == result.RemoteUserId) {
                var p2p = EOSSDKComponent.GetP2PInterface();
                if (p2p != null) {
                    Result res = p2p.AcceptConnection(
                        new AcceptConnectionOptions() {
                            LocalUserId = EOSSDKComponent.LocalUserProductId,
                            RemoteUserId = result.RemoteUserId,
                            SocketId = result.SocketId
                        });
#if UNITY_EDITOR
                    Debug.Log($"[EosTransport/Client] AcceptConnection from host {result.RemoteUserId} result: {res}");
#endif
                } else {
                    Debug.LogError("[EosTransport/Client] Cannot AcceptConnection: GetP2PInterface() returned null!");
                }
            } else {
                Debug.LogError($"[EosTransport/Client] P2P Acceptance Request from unknown host ID. Expected: {hostProductId}, Received: {result.RemoteUserId}");
            }
        }

        protected override void OnReceiveInternalData(InternalMessages type, ProductUserId clientUserId, SocketId socketId) {
            if (ignoreAllMessages) {
                return;
            }

            switch (type) {
                case InternalMessages.ACCEPT_CONNECT:
                    Connected = true;
                    OnConnected.Invoke();
#if UNITY_EDITOR
                    Debug.Log("[EosTransport/Client] Connection established.");
#endif
                    break;
                case InternalMessages.DISCONNECT:
                    Connected = false;
#if UNITY_EDITOR
                    Debug.Log("[EosTransport/Client] Disconnected.");
#endif
                    OnDisconnected.Invoke();
                    break;
                default:
#if UNITY_EDITOR
                    Debug.LogWarning($"[EosTransport/Client] Received unknown message type: {type}");
#endif
                    break;
            }
        }

        public void Send(byte[] data, int channelId) => Send(hostProductId, socketId, data, (byte) channelId);

        protected override void OnConnectionFailed(ProductUserId remoteId) => OnDisconnected.Invoke();
        public void EosNotInitialized() => OnDisconnected.Invoke();
    }
}