using System;
using System.Net;
using System.Net.Sockets;



namespace ConsoleApp1.Data
{
    public class Conn
    {
        //缓冲区大小
        public const int BUFFER_SIZE = 1024;
        public Socket socket;
        public bool isUse = false;
        //缓冲区 
        public byte[] readbuffer = new byte[BUFFER_SIZE];
        public int buffCount = 0;

        public Conn()
        {
            readbuffer = new byte[BUFFER_SIZE];
        }

        public void Init(Socket socket)
        {
            this.socket = socket;
            isUse = true;
            buffCount = 0;
        }

        public int BuffRemain()
        {
            return BUFFER_SIZE - buffCount;
        }
        //获取客户端地址
        public string GetAddress()
        {
            if (isUse)
            {
                return socket.RemoteEndPoint.ToString();
            }
            else
            {
                return "无法获取地址";
            }
        }

        public void Close()
        {
            if (isUse)
            {
                Console.WriteLine("断开连接" + GetAddress());
                socket.Close();
                isUse = false;
            }
            else
            {
                return;
            }
        }
    }
}