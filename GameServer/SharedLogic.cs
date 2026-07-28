namespace SharedLogic
{

    // movement update struct [client -> server]
    public class PlayerInputPacket
    {
        public uint Tick { get; set; }
        public float InputX { get; set; }
        public float InputY { get; set; }

        public float LookInputX { get; set;}
        public float LookInputZ { get; set;}
    }

    public class PlayerStatsPacket
    {
        public int PlayerId {get; set;}
        public float MoveSpeed { get; set;}
        public float MaxHealth { get; set;}
        public float CurrentHealth { get; set;}
        public float AttackDamage { get; set;}
    }

    // [server -> clients]
    public class PlayerStatePacket
    {
        public int PlayerId { get; set; }
        public uint Tick { get; set; }

        // Posicion
        public float PosX { get; set; }
        public float PosZ { get; set; }

        // Rotación
        public float LookDirX { get; set; }
        public float LookDirZ { get; set; }
    }

    public class WelcomePacket
    {
        public int MyId { get; set; }
    }

}


