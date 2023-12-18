using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using ThreadState = System.Threading.ThreadState;
using System.Threading;
using UnityEditor.PackageManager;

namespace Defix.Framework.Network
{
    public class Client : INetworkObject
    {
        public class ClientSocket
        {
            public Socket Socket => _clientSocket;

            private readonly Socket _clientSocket;
            private readonly IPEndPoint _endPoint;

            public ClientSocket(IPEndPoint endPoint)
            {
                _clientSocket = new(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _endPoint = endPoint;
            }

            public async void Start()
            {
                await _clientSocket.ConnectAsync(_endPoint);
            }
        }

        public ClientProcessHandler Process
        {
            get => _processHandler;
        }

        public INetworkDataReader Reader
        {
            get => _dataReader;
        }

        public INetworkDataSender Sender
        {
            get => _dataSender;
        }

        private readonly ClientSocket _clientSocket;
        private readonly ClientProcessHandler _processHandler;
        private readonly ClientPacketDataReader _dataReader;
        private readonly ClientPacketDataSender _dataSender;

        public Client(IPEndPoint endPoint)
        {
            _clientSocket =         new(endPoint);
            _processHandler =       new(_clientSocket);
            _dataReader =           new(_clientSocket);
            _dataSender =           new(_clientSocket);
        }

        public void Run()
        {
            Reader.ReadPacketData();
        }
    }

    public class ClientProcessHandler
    {
        public event Action OnConnect;
        public event Action OnDisconnect;

        private readonly Client.ClientSocket _clientSocket;

        public ClientProcessHandler(Client.ClientSocket clientSocket)
        {
            _clientSocket = clientSocket;
        }

        public void Connect()
        {
            _clientSocket.Start();

            OnConnect?.Invoke();
        }

        public void Disconnect()
        {
            _clientSocket.Socket.Send(new byte[1], 0, SocketFlags.None);

            OnDisconnect?.Invoke();
        }
    }

    public class ClientPacketDataSender : INetworkDataSender
    {
        private readonly Client.ClientSocket _clientSocket;

        public ClientPacketDataSender(Client.ClientSocket clientSocket)
        {
            _clientSocket = clientSocket;
        }

        public void SendPacketData(Packet packetToSend)
        {
            _clientSocket.Socket.Send(packetToSend.Data);
        }

        public async void SendPacketDataAsync(Packet packetToSend)
        {
            await _clientSocket.Socket.SendAsync(packetToSend.Data, SocketFlags.None);
        }
    }

    public class ClientPacketDataReader : INetworkDataReader
    {
        public event Action<Packet> OnGetData;

        private readonly Client.ClientSocket _clientSocket;

        public ClientPacketDataReader(Client.ClientSocket clientSocket)
        {
            _clientSocket = clientSocket;
        }

        public async void ReadPacketData()
        {
            if (_clientSocket == null || _clientSocket.Socket.Available == 0 || !_clientSocket.Socket.Connected)
                return;

            var buffer = new byte[Packet.DefaultPacketSize];
            int receiveIndex;

            do
            {
                receiveIndex = await _clientSocket.Socket.ReceiveAsync(buffer, SocketFlags.None);
            }
            while (_clientSocket.Socket.Available > 0);

            OnGetData?.Invoke(new(buffer, receiveIndex));
        }
    }
}
