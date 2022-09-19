using System.Text.RegularExpressions;
using System.Text;

namespace SimpleTcpClient
{
    internal class Program
    {
        static void Main(string[] args)
        {

            Console.WriteLine("Hello, TCP!");

            //0x0D, 0x0A => CR, LR
            var c = new CustomTcpClient()
                .SetConnData("10.169.169.141", 10002)
                .SetDelimiter(new byte[] { 0x0D, 0x0A })
                .SetMsgPollFreq(50)
                .SetEncoder(Encoding.ASCII)
                .SetConnCheckInterval(1000)
                .SetSkipFirstBurst(false)
                .SetAutoTrim(true);

            c.OnDelimiterDataReceived += (sender, msg) => {

                Console.WriteLine("Received: " + msg);

            };

            c.OnDataReceived += (sender, msg) => {

                Console.WriteLine("Received undelimited: " + msg);

            };

            c.OnDisconnected += (sender, _) =>
            {
                Console.WriteLine("Disconnected");
            };

            c.OnConnected += (sender, _) =>
            {
                Console.WriteLine("Connected");
            };

            c.Connect();

            Console.ReadLine();

        }
    }
}