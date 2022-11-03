namespace Test.Integrity;

internal class Statistics
{
    public long MsgSent 
    { 
        get
        {
            lock (statsLock)
            {
                return msgSent;
            }
        }
        set => msgSent = value;
    }

    public long MsgRecv
    {
        get
        {
            lock (statsLock)
            {
                return msgRecv;
            }
        }
        set => msgRecv = value;
    }

    public long BytesSent
    {
        get
        {
            lock (statsLock)
            {
                return bytesSent;
            }
        }
        set => bytesSent = value;
    }
        
    public long BytesRecv
    {
        get
        {
            lock (statsLock)
            {
                return bytesRecv;
            }
        }
        set => bytesRecv = value;
    }

    private readonly object statsLock = new object();

    private long msgSent;
    private long msgRecv;
    private long bytesSent;
    private long bytesRecv;

    public Statistics()
    {
        MsgSent = 0;
        MsgRecv = 0;
        BytesSent = 0;
        BytesRecv = 0;
    }

    public override string ToString()
    {
        return "Sent [" + MsgSent + " msgs, " + BytesSent + " bytes] Received [" + MsgRecv + " msgs, " + BytesRecv + " bytes]";
    }

    public void AddSent(long len)
    {
        lock (statsLock)
        {
            MsgSent++;
            BytesSent += len;
        }
    }

    public void AddRecv(long len)
    {
        lock (statsLock)
        {
            MsgRecv++;
            BytesRecv += len;
        }
    }
}