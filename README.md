# SimpleTcpClient
C# -> TCP Client Only (but generic and configurable)

## Origin:
https://github.com/BrandonPotter/SimpleTCP

## Fluent API

### Connection Sample

```csharp
var client = new CustomTcpClient()
                .SetConnData("10.169.169.141", 10002)
                .SetDelimiter(new byte[] { 0x0D, 0x0A })
                .SetMsgPollFreq(50)
                .SetEncoder(Encoding.ASCII)
                .SetConnCheckInterval(1000)
                .SetSkipFirstBurst(false)
                .SetAutoTrim(true);
                
client.Connect();
```

### Event Handling
```csharp
client.OnDelimiterDataReceived += (sender, msg) => {
    Console.WriteLine("Received: " + msg);
};

client.OnDataReceived += (sender, msg) => {
    Console.WriteLine("Received undelimited: " + msg);
};

client.OnDisconnected += (sender, _) =>
{
    Console.WriteLine("Disconnected");
};

client.OnConnected += (sender, _) =>
{
    Console.WriteLine("Connected");
};   
```
