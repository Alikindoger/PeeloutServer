

namespace  SharedLogic
{

        // movement update struct [client -> server]
        public class PlayerInputPacket
        {
            public uint Tick { get; set; }
        public float InputX { get; set; }
        public float InputY { get; set; }
    }

// [server -> clients]
        public class PlayerStatePacket
        {
            public int PlayerId { get; set; }
        public uint Tick { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
    }

    public class WelcomePacket
    {
        public int MyId { get; set; }
    }
}


