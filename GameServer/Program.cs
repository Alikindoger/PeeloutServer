using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Threading;
using System.Collections.Generic;
using SharedLogic;
using System.Diagnostics;

public class PlayerServerState
{
    public int Id;
    public float PosX;
    public float PosY;
    public uint UltimoTickRecibido;
}


class Program
{

    static Dictionary<int, PlayerServerState> connectedPlayers = new Dictionary<int, PlayerServerState>();
   

    static NetPacketProcessor packetProcessor = new NetPacketProcessor();
    static NetDataWriter writer = new NetDataWriter();

    //tick stuff
    static uint currentTick = 0;
    const float TICK_RATE = 30f;
    const float TIME_PER_TICK = 1f / TICK_RATE;

    static void Main(string[] args)
    {
        EventBasedNetListener listener = new EventBasedNetListener();
        NetManager server = new NetManager(listener);



        //listeners

        // try conn, credentials
        listener.ConnectionRequestEvent += request =>
        {
            Console.WriteLine($"Conexion entrante de: {request.RemoteEndPoint}");
            request.AcceptIfKey("root");
        };

        // conn established
        listener.PeerConnectedEvent += peer =>
        {
            Console.WriteLine($"Jugador conectado ID asignado: {peer.Id}");

            PlayerServerState newPlayer = new PlayerServerState
            {
                Id = peer.Id,
                PosX = 0f, // spawnea en x 0
                PosY = 0f, // spawnea en y 0
                UltimoTickRecibido = 0
            };

            connectedPlayers.Add(peer.Id,newPlayer); //list it

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

        listener.NetworkReceiveEvent += (peer, reader,channel, deliveryMethod) =>
        {
            //process packets
            try
            {
                packetProcessor.ReadAllPackets(reader, peer);
            }
            catch (Exception ex) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n--- ERROR AL RECIBIR PAQUETE ---");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();

            }
            };

        packetProcessor.SubscribeReusable<PlayerInputPacket, NetPeer>((packet, peer) =>
        {
           
            // get player
            if (connectedPlayers.TryGetValue(peer.Id, out PlayerServerState jugador))
            {

                // old tick = discarded
                if (packet.Tick >= jugador.UltimoTickRecibido)
                {
                    
                    jugador.UltimoTickRecibido = packet.Tick;

                    // apply movement (5f speed)
                    // TODO multiply with deltaTime
                    float velocidad = 5f * 0.015f; // 0.015 sleep thread

                    jugador.PosX += packet.InputX * velocidad;
                    jugador.PosY += packet.InputY * velocidad;

                    Console.WriteLine($"Jugador {peer.Id} movido a X:{jugador.PosX} Y:{jugador.PosY}");
                }
            }
        });

        //endlisteners

        // port start
        server.Start(9050);
        Console.WriteLine("Servidor iniciado | Esperando jugadores...");


        Stopwatch timer = new Stopwatch();
        timer.Start();
        double acum = 0.0;

        // main loop
        while (!Console.KeyAvailable)
        {
            //real time since last tick
            double deltaTime = timer.Elapsed.TotalSeconds;
            timer.Restart();

            acum += deltaTime;

            //get incoming net packets
            server.PollEvents(); // `process

            while (acum >= TIME_PER_TICK)
            {
                // we ticked
                currentTick++;


                UpdateClients(server);

                acum -= TIME_PER_TICK;
            }

            Thread.Sleep(1);
        }

        server.Stop();
        Console.WriteLine("Servidor detenido. Pulsa ENTER para salir.");
        Console.ReadLine();
    }

    static void UpdateClients(NetManager server)
    {

        if (connectedPlayers.Count == 0) return;

        foreach (var player in connectedPlayers.Values) {

            PlayerStatePacket state = new PlayerStatePacket
            {
                PlayerId = player.Id,
                Tick = currentTick, // ¡Usamos el reloj maestro!
                PosX = player.PosX,
                PosY = player.PosY
            };

            writer.Reset();

            packetProcessor.Write(writer, state);

            server.SendToAll(writer, DeliveryMethod.Sequenced);

        }

    }
}

