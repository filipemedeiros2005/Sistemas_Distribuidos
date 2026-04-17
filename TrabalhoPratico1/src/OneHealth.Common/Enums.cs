namespace OneHealth.Common
{
    public enum MsgType : byte
    {
        HELO = 1,
        DATA = 2,
        ACK = 3,
        BYE = 4,
        NACK = 5,
        ALERT = 6,
        STATUS = 7
    }

    public enum DataType : byte
    {
        Unknown = 0,
        PM10 = 1,
        PM25 = 2,
        Temp = 3,
        Hum = 4,
        Ruido = 5,
        Lum = 6,
        Ar = 7,
        Video = 8
    }
}