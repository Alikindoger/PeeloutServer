using LiteNetLib;
using LiteNetLib.Utils;
using SharedLogic; // Asumo que aquí están tus paquetes (PlayerInputPacket, PlayerStatePacket, WelcomePacket)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

public class PlayerServerState
{
    public int Id { get; private set; }
    public NetPeer Peer { get; private set; }
    public InputRingBuffer InputBuffer { get; private set; } 
    public PlayerInputStruct LastValidInput { get; set; }
    public int MissedTicksCount { get; set; }

    public Vector3 Position { get; set; }
    public Vector3 LookDirection { get; set; }
    public float MoveSpeed { get; set; } = 5f;

    public int MaxHealth { get; set; } = 100;
    public int CurrentHealth { get; set; }
    public bool IsDead => CurrentHealth <= 0;
    
    public uint LastReceivedTick { get; set; } 
    
    public PlayerServerState(int id, NetPeer peer, Vector3 spawnPosition)
    {
        Id = id;
        Peer = peer;
        Position = spawnPosition;

        LookDirection = Vector3.UnitZ;
        
        InputBuffer = new InputRingBuffer(64); 
        CurrentHealth = MaxHealth;
        LastReceivedTick = 0;
    }

    public void TakeDamage(int amount)
    {
        if (IsDead) return;

        CurrentHealth -= amount;
        if (CurrentHealth < 0) CurrentHealth = 0;
    }
}

public struct PlayerInputStruct
{
    public uint Tick; 
    public float InputX;
    public float InputY;

    public float InputDirX;
    public float InputDirZ;
}

public class InputRingBuffer
{
    private readonly PlayerInputStruct[] _buffer;
    private readonly int _capacity;

    public InputRingBuffer(int capacity)
    {
        _capacity = capacity;
        _buffer = new PlayerInputStruct[_capacity];
    }

    public void AddInput(PlayerInputStruct input)
    {
        int index = (int)(input.Tick % _capacity);
        _buffer[index] = input;
    }

    public PlayerInputStruct GetInput(uint tick, out bool isValid)
    {
        int index = (int)(tick % _capacity);
        PlayerInputStruct storedInput = _buffer[index];

        if (storedInput.Tick == tick)
        {
            isValid = true;
            return storedInput;
        }
        else
        {
            isValid = false;
            return new PlayerInputStruct
            {
              Tick = tick,
              InputX = 0,
              InputY = 0,

              InputDirX = 0,
              InputDirZ = 0,
            };
        }


    }
}

class Program
{
    static Dictionary<int, PlayerServerState> connectedPlayers = new Dictionary<int, PlayerServerState>();
    
    static NetPacketProcessor packetProcessor = new NetPacketProcessor();
    static NetDataWriter writer = new NetDataWriter();

    // TICK STUFF
    static uint _serverTick = 0;
    const float TICK_RATE = 30f;
    const float TIME_PER_TICK = 1f / TICK_RATE; 

    static void Main(string[] args)
    {
        EventBasedNetListener listener = new EventBasedNetListener();
        NetManager server = new NetManager(listener);

        // LISTENERS
        listener.ConnectionRequestEvent += request =>
        {
            Console.WriteLine($"Conexion entrante de: {request.RemoteEndPoint}");
            request.AcceptIfKey("root");
        };

        listener.PeerConnectedEvent += peer =>
        {
            Console.WriteLine($"Jugador conectado ID asignado: {peer.Id}");
            
            PlayerServerState newPlayer = new PlayerServerState(peer.Id, peer, Vector3.Zero);
            connectedPlayers.Add(peer.Id, newPlayer);

            // send his id to the client
            WelcomePacket welcome = new WelcomePacket { MyId = peer.Id };
            writer.Reset();
            packetProcessor.Write(writer, welcome);
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
        };

        listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
        {
            Console.WriteLine("Jugador " + peer.Id + " se ha desconectado");
            connectedPlayers.Remove(peer.Id);
        };

        listener.NetworkReceiveEvent += (peer, reader, channel, deliveryMethod) =>
        {
            try
            {
                packetProcessor.ReadAllPackets(reader, peer);
            }
            catch (Exception ex) 
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n--- ERROR AL RECIBIR PAQUETE ---");
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }
        };

        packetProcessor.SubscribeReusable<PlayerInputPacket, NetPeer>(OnPlayerInputReceived);

        server.Start(9050);
        Console.WriteLine("Servidor iniciado | Esperando jugadores...");

        Stopwatch timer = new Stopwatch();
        timer.Start();
        double acum = 0.0;

        // MAIN LOOP
        while (!Console.KeyAvailable)
        {
            double deltaTime = timer.Elapsed.TotalSeconds;
            timer.Restart();
            acum += deltaTime;

            server.PollEvents();

            while (acum >= TIME_PER_TICK)
            {
                _serverTick++;
                
                SimulateWorld();
                UpdateClients(server);
                
                acum -= TIME_PER_TICK;
            }

            Thread.Sleep(1);
        }

        server.Stop();
        Console.WriteLine("Servidor detenido. Pulsa ENTER para salir.");
        Console.ReadLine();
    }

    private static void OnPlayerInputReceived(PlayerInputPacket packet, NetPeer peer)
    {
        if (connectedPlayers.TryGetValue(peer.Id, out PlayerServerState player))
        {
            PlayerInputStruct newInput = new PlayerInputStruct
            {
                Tick = packet.Tick,
                InputX = packet.InputX,
                InputY = packet.InputY,

                InputDirX = packet.LookInputX,
                InputDirZ = packet.LookInputZ,
            };

            player.InputBuffer.AddInput(newInput);
            player.LastReceivedTick = packet.Tick;
        }
    }

    // SIMULACIÓN
    static void SimulateWorld()
    {
        foreach (var player in connectedPlayers.Values)
        {
            PlayerInputStruct input = player.InputBuffer.GetInput(_serverTick, out bool isValid);
            
            if (isValid)
            {
                player.LastValidInput = input;
                player.MissedTicksCount = 0;
            }
            else
            {
                player.MissedTicksCount++;
                if (player.MissedTicksCount <= 15)
                {
                    input = player.LastValidInput;
                }
                else
                {
                    input.InputX = 0;
                    input.InputY = 0;
                    input.InputDirX = 0;
                    input.InputDirZ = 0;
                }
            }

            Vector2 inputDir = new Vector2(input.InputX, input.InputY);
            if (inputDir.LengthSquared() > 1f)
            {
                inputDir = Vector2.Normalize(inputDir);
            }

            Vector3 moveDirection = new Vector3(inputDir.X, 0, inputDir.Y);
            Vector3 lookDirection = new Vector3(input.InputDirX, 0, input.InputDirZ);

            player.Position += moveDirection * player.MoveSpeed * TIME_PER_TICK;

            if (lookDirection.LengthSquared() > 0.1f)
            {
                player.LookDirection = Vector3.Normalize(lookDirection);
            }

            Console.WriteLine($"Jugador {player.Id}: (X:{player.Position.X}, Z:{player.Position.Z}) | {player.LookDirection}");
        }
    }

    static void UpdateClients(NetManager server)
    {
        if (connectedPlayers.Count == 0) return;

        foreach (var player in connectedPlayers.Values) 
        {
            // Ahora mismo esto funcionará pero luego habrá que cambiarlo, por ejemplo hacer que el paquete de posicion se envie 30veces/s pero el de vida y demás stats solo cuando cambien (lo mismo aplica al cliente)
            // Para eso creo que hay una funcion de channels (para enviar cada cosa por un canal, no sé muy bien en que se tendría que hacer pero sí se que habría que usar uno distinto en lo que es el juego como tal y el chat por ejemplo)
            PlayerStatePacket state = new PlayerStatePacket
            {
                PlayerId = player.Id, 
                Tick = _serverTick, 

                PosX = player.Position.X,
                PosZ = player.Position.Z, 

                LookDirX = player.LookDirection.X,
                LookDirZ = player.LookDirection.Z,

                CurrentHealth = player.CurrentHealth,
            };

            writer.Reset();
            packetProcessor.Write(writer, state);
            server.SendToAll(writer, DeliveryMethod.Sequenced);
        }
    }
}