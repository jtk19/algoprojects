using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Net.Http;
using System.Net;
using System.IO;


namespace ArtBase
{

    public class HTTPServer
    {

        protected int port;
        TcpListener listener;

        public HTTPServer(int port)
        {
            this.port = port;
        }

        public void listen()
        {
            IPAddress ipAddress = System.Net.Dns.GetHostAddresses("localhost")[0];
            listener = new TcpListener( ipAddress, port);
            listener.Start();
            while (!Common.Util.EndApplication )
            {
                while ( !listener.Pending() )
                {
                    Thread.Sleep(50);
                    if (Common.Util.EndApplication)
                        return;
                }
                TcpClient s = listener.AcceptTcpClient();
                HTTPProcessor processor = new HTTPProcessor(s, this);
                Thread thread = new Thread(new ThreadStart(processor.process));
                thread.Start();
                Thread.Sleep(1);
            }
        }

        public void handleGETRequest(HTTPProcessor p)
        {
            Console.WriteLine("request: {0}", p.http_url);
        }

        public void handlePOSTRequest(HTTPProcessor p, StreamReader inputData)
        {
            Console.WriteLine("POST request: {0}", p.http_url);
            string data = inputData.ReadToEnd();

        }


    } 
}
