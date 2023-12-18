using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ThreadState = System.Threading.ThreadState;

namespace Defix.Framework.Network
{
    public class Server : INetworkObject
    {
        public class ServerSocket
        {
            public Socket Socket => _serverSocket;

            private readonly Socket _serverSocket;
            private readonly IPEndPoint _endPoint;

            public ServerSocket(IPEndPoint endPoint)
            {
                _serverSocket = new(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _endPoint = endPoint;
            }

            public void Start()
            {
                _serverSocket.Bind(_endPoint);
                _serverSocket.Listen(10);
            }
        }

        public ServerConnectionHandler ConnectionHandler
        {
            get => _connectionHandler;
        }

        public ServerProcessHandler Process
        {
            get => _processHandler;
        }

        public INetworkDataSender Sender
        {
            get => _dataSender;
        }

        public INetworkDataReader Reader
        {
            get => _dataReader;
        }

        private readonly ServerSocket _serverSocket;
        private readonly ServerConnectionHandler _connectionHandler;
        private readonly ServerProcessHandler _processHandler;
        private readonly ServerPacketDataReader _dataReader;
        private readonly ServerPacketDataSender _dataSender;

        public Server(IPEndPoint endPoint)
        {
            _serverSocket =         new(endPoint);
            _connectionHandler =    new(_serverSocket);
            _processHandler =       new(_serverSocket);
            _dataReader =           new(_connectionHandler.ConnectionCollection.Connections);
            _dataSender =           new(_connectionHandler.ConnectionCollection.Connections);

            SubscribeReceivingRequests();
        }

        ~Server()
        {
            UnsubscribeReceivingRequests();
        }

        private void SubscribeReceivingRequests()
        {
            _connectionHandler.OnConnected      += (e) => _dataReader.UpdateServerConnections(_connectionHandler.ConnectionCollection.Connections);
            _connectionHandler.OnDisconnected   += (e) => _dataReader.UpdateServerConnections(_connectionHandler.ConnectionCollection.Connections);
            _connectionHandler.OnConnected      += (e) => _dataSender.UpdateServerConnections(_connectionHandler.ConnectionCollection.Connections);
            _connectionHandler.OnDisconnected   += (e) => _dataSender.UpdateServerConnections(_connectionHandler.ConnectionCollection.Connections);

            _processHandler.OnStartConnection   += () => _connectionHandler.StartConnectionHandleProcess();
            _processHandler.OnAbortConnection   += () => _connectionHandler.StopConnectionHandleProcess();
        }

        private void UnsubscribeReceivingRequests()
        {
            _connectionHandler.OnConnected      -= (e) => _dataReader.UpdateServerConnections(_connectionHandler.ConnectionCollection.Connections);
            _connectionHandler.OnDisconnected   -= (e) => _dataReader.UpdateServerConnections(_connectionHandler.ConnectionCollection.Connections);
            _connectionHandler.OnConnected      -= (e) => _dataSender.UpdateServerConnections(_connectionHandler.ConnectionCollection.Connections);
            _connectionHandler.OnDisconnected   -= (e) => _dataSender.UpdateServerConnections(_connectionHandler.ConnectionCollection.Connections);

            _processHandler.OnStartConnection   -= () => _connectionHandler.StartConnectionHandleProcess();
            _processHandler.OnAbortConnection   -= () => _connectionHandler.StopConnectionHandleProcess();
        }

        public void Run()
        {
            ConnectionHandler.DisconnectionCollection.Observe();
            Reader.ReadPacketData();
        }
    }

    public class ServerProcessHandler
    {
        public event Action OnStartConnection;
        public event Action OnAbortConnection;

        private readonly Server.ServerSocket _serverSocket;

        public ServerProcessHandler(Server.ServerSocket serverSocket)
        {
            _serverSocket = serverSocket;
        }

        public void StartServerConnection()
        {
            _serverSocket.Start();

            OnStartConnection?.Invoke();
        }

        public void AbortServerConnection()
        {
            _serverSocket.Socket.Close();

            OnAbortConnection?.Invoke();
        }
    }

    public class ServerDisconnectionObserver
    {
        private ServerConnectionCollection _connections;

        public ServerDisconnectionObserver(ServerConnectionCollection connections)
        {
            _connections = connections;
        }

        public void UpdateServerConnections(ServerConnectionCollection connections)
        {
            _connections = connections;
        }

        public async void Observe()
        {
            for (int i = 0; i < _connections.Connections.Count; i++)
            {
                if (_connections.Connections[i] == null)
                    continue;

                try
                {
                    if (await _connections.Connections[i].ReceiveAsync(new byte[128], SocketFlags.Peek) == 0)
                    {
                        _connections.Remove(_connections.Connections[i]);
                    }
                } catch { continue; }
            }
        }
    }

    public class ServerConnectionHandler
    {
        public ServerConnectionCollection ConnectionCollection
        {
            get => _connectionsCollection;
        }

        public ServerDisconnectionObserver DisconnectionCollection
        {
            get => _disconnectionObserver;
        }

        public event Action<Socket> OnConnected;
        public event Action<Socket> OnDisconnected;

        private readonly ServerConnectionCollection _connectionsCollection = new();
        private readonly ServerConnectionObserver _connectionsObserver;
        private readonly ServerDisconnectionObserver _disconnectionObserver;

        public ServerConnectionHandler(Server.ServerSocket serverSocket)
        {
            _connectionsObserver = new(serverSocket);
            _disconnectionObserver = new(_connectionsCollection);
        }

        public void StartConnectionHandleProcess()
        {
            InitializeConnectionCollection();
            InitializeObserver();

            _connectionsObserver.ObserveAsync();
        }

        public void StopConnectionHandleProcess()
        {
            AbortObserverProcess();
            AbortConnectionCollection();
        }

        private void InitializeConnectionCollection()
        {
            _connectionsCollection.OnConnected      += (e) => OnConnected?.Invoke(e);
            _connectionsCollection.OnConnected      += (e) => _disconnectionObserver.UpdateServerConnections(_connectionsCollection);
            _connectionsCollection.OnDisconnected   += (e) => OnDisconnected?.Invoke(e);
        }

        private void InitializeObserver()
        {
            _connectionsObserver.OnAccept           += (e) => _connectionsCollection.Add(e);
            _connectionsObserver.OnAccept           += (e) => _connectionsObserver.ObserveAsync();
        }

        private void AbortObserverProcess()
        {
            _connectionsObserver.OnAccept           -= (e) => _connectionsCollection.Add(e);
            _connectionsObserver.OnAccept           -= (e) => _connectionsObserver.ObserveAsync();
        }

        private void AbortConnectionCollection()
        {
            _connectionsCollection.OnConnected      -= (e) => OnConnected?.Invoke(e);
            _connectionsCollection.OnDisconnected   -= (e) => OnDisconnected?.Invoke(e);
        }
    }

    public class ServerConnectionObserver
    {
        public event Action<Socket> OnAccept;

        private readonly Server.ServerSocket _serverSocket;

        public ServerConnectionObserver(Server.ServerSocket serverSocket)
        {
            _serverSocket = serverSocket;
        }

        public async void ObserveAsync()
        {
            var clientSocket = await _serverSocket.Socket.AcceptAsync();

            OnAccept?.Invoke(clientSocket);
        }
    }

    public class ServerPacketDataReader : INetworkDataReader
    {
        public event Action<Packet> OnGetData;

        private IReadOnlyList<Socket> _connections;

        public ServerPacketDataReader(IReadOnlyList<Socket> connections)
        {
            _connections = connections;
        }

        public void UpdateServerConnections(IReadOnlyList<Socket> connections)
        {
            _connections = connections;
        }

        public async void ReadPacketData()
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                if (_connections[i] == null || _connections[i].Available == 0 || !_connections[i].Connected)
                    continue;

                var buffer = new byte[Packet.DefaultPacketSize];
                int receiveIndex;

                try
                {
                    do
                    {
                        receiveIndex = await _connections[i].ReceiveAsync(buffer, SocketFlags.None);
                    }
                    while (_connections[i].Available > 0);
                } catch { return; }

                OnGetData?.Invoke(new(buffer, receiveIndex));
            }
        }
    }

    public class ServerPacketDataSender : INetworkDataSender
    {
        private IReadOnlyList<Socket> _connections;

        public ServerPacketDataSender(IReadOnlyList<Socket> connections)
        {
            _connections = connections;
        }

        public void UpdateServerConnections(IReadOnlyList<Socket> connections)
        {
            _connections = connections;
        }

        public void SendPacketData(Packet packetToSend)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                _connections[i].Send(packetToSend.Data);
            }
        }

        public async void SendPacketDataAsync(Packet packetToSend)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                await _connections[i].SendAsync(packetToSend.Data, SocketFlags.None);
            }
        }
    }

    public class ServerConnectionCollection
    {
        public event Action<Socket> OnConnected;
        public event Action<Socket> OnDisconnected;

        public IReadOnlyList<Socket> Connections => _connections;

        private readonly List<Socket> _connections = new();

        public void Add(Socket connection)
        {
            if (!_connections.Contains(connection))
            {
                _connections.Add(connection);

                OnConnected?.Invoke(connection);
            }
        }

        public void Remove(Socket socket)
        {
            if(_connections.Contains(socket))
            {
                OnDisconnected?.Invoke(socket);

                _connections.Remove(socket);
            }
        }

        public void RemoveAt(int index)
        {
            OnDisconnected?.Invoke(_connections[index]);

            _connections.RemoveAt(index);
        }
    }

    public readonly struct Packet
    {
        public const int DefaultPacketSize = 256;
        public readonly byte[] Data;
        public readonly int DataIndex;

        public Packet(byte[] data, int dataIndex)
        {
            Data = data;
            DataIndex = dataIndex;
        }
    }

    public interface INetworkObject
    {
        INetworkDataReader Reader { get; }
        INetworkDataSender Sender { get; }
    }

    public interface INetworkDataReader
    {
        event Action<Packet> OnGetData;
        void ReadPacketData();
    }

    public interface INetworkDataSender
    {
        void SendPacketData(Packet data);
        void SendPacketDataAsync(Packet data);
    }
}
