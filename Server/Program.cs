using System;
using System.Net;
using System.Net.Sockets;
using ConsoleApp1.Data;
using System.Collections.Generic;
using System.Text;
using Microsoft.Data.Sqlite;
using System.IO;

namespace ConsoleApp1
{
    class Program
    {
        static void Main(string[] args)
        {
            Serv server = new Serv();

            string host = "127.0.0.1";
            int port = 1123;

            try
            {
                server.Start(host, port);
                Console.WriteLine("按任意键退出服务器...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FATAL] 服务器启动失败: {ex.Message}");
                Console.ReadKey();
            }
        }
    }

    public class Serv
    {
        public Socket listenfd;
        public Conn[] conns;
        public int maxCount = 50;
        private readonly string _dbPath = "chat_messages.db";

        public Serv()
        {
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            // 确保数据库目录存在
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Messages (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AddressPrefix TEXT NOT NULL,  -- e.g., '127.0.0.1:54321'
                    Content TEXT NOT NULL,
                    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
                )";
            cmd.ExecuteNonQuery();
        }

        public int NewIndex()
        {
            for (int i = 0; i < conns.Length; i++)
            {
                if (conns[i] == null)
                {
                    conns[i] = new Conn();
                    return i;
                }
                else if (!conns[i].isUse)
                {
                    return i;
                }
            }
            return -1;
        }

        public void Start(string host, int port)
        {
            conns = new Conn[maxCount];
            for (int i = 0; i < conns.Length; i++)
            {
                conns[i] = new Conn();
            }

            listenfd = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPAddress ip = IPAddress.Parse(host);
            IPEndPoint ipEndPoint = new IPEndPoint(ip, port);
            listenfd.Bind(ipEndPoint);
            listenfd.Listen(maxCount);
            listenfd.BeginAccept(AcceptCb, null);
            Console.WriteLine($"[服务器]启动成功，监听 {host}:{port}");
        }

        public void AcceptCb(IAsyncResult ar)
        {
            try
            {
                Socket socket = listenfd.EndAccept(ar);
                int index = NewIndex();

                if (index < 0)
                {
                    socket.Close();
                    Console.WriteLine("[服务器]连接数已达上限");
                }
                else
                {
                    Conn conn = conns[index];
                    conn.Init(socket);
                    string adr = conn.GetAddress();
                    Console.WriteLine($"[服务器]新连接：{adr}，ID={index}");
                    conn.socket.BeginReceive(conn.readbuffer, conn.buffCount, conn.BuffRemain(), SocketFlags.None, ReceiveCb, conn);
                }
                listenfd.BeginAccept(AcceptCb, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Accept]错误: {ex.Message}");
            }
        }

        public void ReceiveCb(IAsyncResult ar)
        {
            Conn conn = (Conn)ar.AsyncState;
            try
            {
                int count = conn.socket.EndReceive(ar);
                if (count <= 0)
                {
                    Console.WriteLine($"[服务器]客户端断开: {conn.GetAddress()}");
                    conn.Close();
                    return;
                }

                conn.buffCount += count;
                string allData = Encoding.UTF8.GetString(conn.readbuffer, 0, conn.buffCount);
                string[] lines = allData.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                string remaining = "";
                if (!allData.EndsWith("\n"))
                {
                    remaining = lines[lines.Length - 1];
                    Array.Resize(ref lines, lines.Length - 1);
                }

                foreach (string line in lines)
                {
                    ProcessMessage(conn, line.Trim());
                }

                Array.Clear(conn.readbuffer, 0, conn.readbuffer.Length);
                if (!string.IsNullOrEmpty(remaining))
                {
                    byte[] remBytes = Encoding.UTF8.GetBytes(remaining);
                    Buffer.BlockCopy(remBytes, 0, conn.readbuffer, 0, remBytes.Length);
                    conn.buffCount = remBytes.Length;
                }
                else
                {
                    conn.buffCount = 0;
                }

                if (conn.isUse)
                {
                    conn.socket.BeginReceive(conn.readbuffer, conn.buffCount, conn.BuffRemain(), SocketFlags.None, ReceiveCb, conn);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Receive]错误: {ex.Message}");
                conn?.Close();
            }
        }

        void ProcessMessage(Conn conn, string str)
        {
            Console.WriteLine($"[服务器]收到来自 {conn.GetAddress()} 的消息: '{str}'");

            if (str == "GetMsg")
            {
                var recentMessages = GetRecentMessages(5); // 最近5条
                string response = "GetMsg";
                // 注意：从旧到新（因为客户端按顺序显示）
                foreach (var msg in recentMessages)
                {
                    response += " " + msg;
                }
                response += "\n";
                byte[] data = Encoding.UTF8.GetBytes(response);
                try
                {
                    conn.socket.Send(data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Send GetMsg] 错误: {ex.Message}");
                }
            }
            else
            {
                string addressPrefix = conn.GetAddress(); // e.g., "127.0.0.1:54321"
                string fullMsg = $"{addressPrefix}:{str}";

                // 保存到数据库（分开存地址和内容，便于查询）
                SaveMessage(addressPrefix, str);

                // 广播给所有客户端
                byte[] broadcastData = Encoding.UTF8.GetBytes(fullMsg + "\n");
                for (int i = 0; i < conns.Length; i++)
                {
                    if (conns[i] == null || !conns[i].isUse) continue;
                    try
                    {
                        conns[i].socket.Send(broadcastData);
                    }
                    catch
                    {
                        // 忽略发送失败
                    }
                }
            }
        }

        private void SaveMessage(string addressPrefix, string content)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO Messages (AddressPrefix, Content) VALUES (@addr, @content)";
            cmd.Parameters.AddWithValue("@addr", addressPrefix);
            cmd.Parameters.AddWithValue("@content", content);
            cmd.ExecuteNonQuery();
        }

        private List<string> GetRecentMessages(int count)
        {
            var messages = new List<string>();
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var cmd = connection.CreateCommand();
            // 按 Id 升序（旧→新），取最后 count 条
            cmd.CommandText = @"
                SELECT AddressPrefix, Content 
                FROM Messages 
                ORDER BY Id DESC 
                LIMIT @limit";
            cmd.Parameters.AddWithValue("@limit", count);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string addr = reader.GetString(0);
                string content = reader.GetString(1);
                messages.Add($"{addr}:{content}");
            }
            messages.Reverse(); // 变成 旧→新，客户端显示顺序自然
            return messages;
        }
    }
}