using System;
using System.Collections;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Validator
{
    class Program
    {
        static void Main(string[] args)
        {
            string email = args[0];
            bool result;

            result = Validate(email);
            Console.WriteLine(result ? "1" : "0");
            
        }

        static bool Validate(string email)
        {
            //Проверка написания
            string strRegex = @"^([a-zA-Z0-9_\-\.]+)@((\[[0-9]{1,3}" +
            @"\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([a-zA-Z0-9\-]+\" +
            @".)+))([a-zA-Z]{2,4}|[0-9]{1,3})(\]?)$";
            Regex re = new Regex(strRegex);
            if (!re.IsMatch(email)) return (false);

            string host = email.Split("@")[1];
            int timeout = 1000;

            bool ok = false;

            //Получаем MX записи
            string[] mxRecords = null;
            try
            {
                mxRecords = DnsMx.GetMXRecords(host);
            }
            catch (Exception ex) { return false; }
            {
                //Проход по записям, пока не получим положительного ответа
                foreach (string record in mxRecords)
                {
                    var port = 25;

                    //Console.WriteLine(record);
                    IPHostEntry hostip = Dns.GetHostEntry(record);
                    IPAddress ipAddress = hostip.AddressList[0];
                    IPEndPoint remoteEP = new IPEndPoint(ipAddress, port);

                    var sender = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        // Connect to Remote EndPoint 
                        sender.ReceiveTimeout = 500;
                        var result = sender.BeginConnect(remoteEP, null, null);

                        bool success = result.AsyncWaitHandle.WaitOne(timeout, true);
                        if (success)
                        {
                            sender.EndConnect(result);
                        }
                        else
                        {
                            sender.Close(); // Connection timed out.
                            continue;
                        }

                        //Console.WriteLine("Socket connected to {0}", sender.RemoteEndPoint.ToString());

                        // Encode the data string into a byte array.    
                        string start = ReceiveReply(sender);
                        //SendCommand(sender, ".").Substring(0,3);
                        if (start.Substring(0, 3) == "220")
                        {
                            SendCommand(sender, "HELO " + start.Split(" ")[1]);
                            SendCommand(sender, "MAIL FROM:<" + email + ">");
                            string rcptto = SendCommand(sender, "RCPT TO:<" + email + ">").Substring(0, 3);
                            if (rcptto == "250") { ok = true; break; }
                        }
                        // Release the socket.    
                        sender.Shutdown(SocketShutdown.Both);
                        sender.Close();
                    }
                    catch (Exception ex) { }
                }
            }
            return ok;
        }

        public static string SendCommand(Socket s, string c)
        {
            //Console.WriteLine("You: {0}", c);
            s.Send(Encoding.UTF8.GetBytes(c + "\r\n"));
            return ReceiveReply(s);
        }
        public static string ReceiveReply(Socket s)
        {
            byte[] bytes = new byte[500];
            //Console.WriteLine("Wait for receive");
            int bytesRec = s.Receive(bytes);
            string resp = Encoding.UTF8.GetString(bytes, 0, bytesRec);
            //Console.WriteLine("Server: {0}", resp);
            return resp;
        }
    }

    //DNS штуки
    public class DnsMx
    {
        public DnsMx()
        {
        }

        [DllImport("dnsapi", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern int DnsQuery([MarshalAs(UnmanagedType.VBByRefStr)]ref string pszName, QueryTypes wType, QueryOptions options, int aipServers, ref IntPtr ppQueryResults, int pReserved);

        [DllImport("dnsapi", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void DnsRecordListFree(IntPtr pRecordList, int FreeType);

        public static string[] GetMXRecords(string domain)
        {
            IntPtr ptr1 = IntPtr.Zero;
            IntPtr ptr2 = IntPtr.Zero;
            MXRecord recMx;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                throw new NotSupportedException();
            }
            ArrayList list1 = new ArrayList();
            int num1 = DnsMx.DnsQuery(ref domain, QueryTypes.DNS_TYPE_MX, QueryOptions.DNS_QUERY_BYPASS_CACHE, 0, ref ptr1, 0);
            if (num1 != 0)
            {
                throw new Win32Exception(num1);
            }
            for (ptr2 = ptr1; !ptr2.Equals(IntPtr.Zero); ptr2 = recMx.pNext)
            {
                recMx = (MXRecord)Marshal.PtrToStructure(ptr2, typeof(MXRecord));
                if (recMx.wType == 15)
                {
                    string text1 = Marshal.PtrToStringAuto(recMx.pNameExchange);
                    list1.Add(text1);
                }
            }
            DnsMx.DnsRecordListFree(ptr1, 0);
            return (string[])list1.ToArray(typeof(string));
        }

        private enum QueryOptions
        {
            DNS_QUERY_ACCEPT_TRUNCATED_RESPONSE = 1,
            DNS_QUERY_BYPASS_CACHE = 8,
            DNS_QUERY_DONT_RESET_TTL_VALUES = 0x100000,
            DNS_QUERY_NO_HOSTS_FILE = 0x40,
            DNS_QUERY_NO_LOCAL_NAME = 0x20,
            DNS_QUERY_NO_NETBT = 0x80,
            DNS_QUERY_NO_RECURSION = 4,
            DNS_QUERY_NO_WIRE_QUERY = 0x10,
            DNS_QUERY_RESERVED = -16777216,
            DNS_QUERY_RETURN_MESSAGE = 0x200,
            DNS_QUERY_STANDARD = 0,
            DNS_QUERY_TREAT_AS_FQDN = 0x1000,
            DNS_QUERY_USE_TCP_ONLY = 2,
            DNS_QUERY_WIRE_ONLY = 0x100
        }

        private enum QueryTypes
        {
            DNS_TYPE_MX = 15
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MXRecord
        {
            public IntPtr pNext;
            public string pName;
            public short wType;
            public short wDataLength;
            public int flags;
            public int dwTtl;
            public int dwReserved;
            public IntPtr pNameExchange;
            public short wPreference;
            public short Pad;
        }
    }
}
